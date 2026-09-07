using System.Text;
using System.Xml;
using System.Xml.Linq;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Maintains the <c>&lt;ce folder="./db/ModTypes"&gt;</c> block inside a map's
/// <c>cfgeconomycore.xml</c>. Uses XML parsing (not text regexes) for robustness.
/// </summary>
public interface IEconomyCoreService
{
    /// <summary>
    /// Rewrites the ModTypes reference block to reference the given file names
    /// (leaf names only, e.g. "CF_types.xml"). <paramref name="desiredFileNames"/>
    /// is the set that should be referenced; <paramref name="ownedFileNames"/>
    /// is the set of leaf names this manager has previously generated for the
    /// map, so entries it owns but no longer wants can be removed while entries
    /// it does not own (the map's own files, entries added by other tools/mods)
    /// are preserved. Returns false if the file is missing or malformed.
    /// </summary>
    bool UpdateModTypes(string missionPath, IReadOnlyList<string> desiredFileNames, IReadOnlySet<string> ownedFileNames);

    /// <summary>
    /// Removes the ModTypes references for the given leaf names from
    /// cfgeconomycore.xml regardless of whether this manager owns them. Used to
    /// clean up orphaned files the manager does not track. Other references are
    /// preserved. Returns false if the file is missing or malformed.
    /// </summary>
    bool RemoveModTypesFiles(string missionPath, IReadOnlySet<string> fileNames);
}

public sealed class EconomyCoreService : IEconomyCoreService
{
    private const string ModTypesFolder = "./db/ModTypes";

    private readonly IFileSystem _fileSystem;

    public EconomyCoreService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public bool UpdateModTypes(string missionPath, IReadOnlyList<string> desiredFileNames, IReadOnlySet<string> ownedFileNames)
    {
        XDocument? doc = LoadEconomyCore(missionPath);
        if (doc?.Root is null)
        {
            return false;
        }

        // Preserve the caller's ordering (mod load order, types before
        // spawnabletypes) — a HashSet would destroy it. The set is used only for
        // case-insensitive membership checks.
        List<string> desired = desiredFileNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var desiredSet = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);
        var stale = new HashSet<string>(
            ownedFileNames.Where(name => !desiredSet.Contains(name)),
            StringComparer.OrdinalIgnoreCase);

        bool changed = RemoveFileEntries(doc, stale);

        if (desired.Count > 0)
        {
            XElement? existing = doc.Root.Descendants("ce").FirstOrDefault(IsModTypesCe);
            if (existing is not null)
            {
                (string newline, string indent) = DetectFormatting(doc.Root.Descendants("classes").FirstOrDefault());
                string fileIndent = indent + indent;

                // Entries this manager does not own (base/third-party) must survive
                // the rewrite; keep them in their original relative order.
                List<XElement> preserved = existing.Elements("file")
                    .Where(f => !desiredSet.Contains((string?)f.Attribute("name") ?? string.Empty))
                    .ToList();

                // Rewrite only when a desired entry is missing or the desired
                // entries already present are not in the exact order requested
                // (regular types before spawnabletypes, mod load order, ...).
                List<string> presentDesired = existing.Elements("file")
                    .Select(f => (string?)f.Attribute("name") ?? string.Empty)
                    .Where(desiredSet.Contains)
                    .ToList();

                if (!presentDesired.SequenceEqual(desired, StringComparer.OrdinalIgnoreCase))
                {
                    existing.RemoveNodes();
                    foreach (string name in desired)
                    {
                        existing.Add(new XText(newline + fileIndent));
                        existing.Add(new XElement("file",
                            new XAttribute("name", name),
                            new XAttribute("type", GetFileType(name))));
                    }

                    foreach (XElement keep in preserved)
                    {
                        existing.Add(new XText(newline + fileIndent));
                        existing.Add(keep);
                    }

                    existing.Add(new XText(newline + indent));
                    changed = true;
                }
            }
            else
            {
                XElement? classes = doc.Root.Descendants("classes").FirstOrDefault();
                (string newline, string indent) = DetectFormatting(classes);
                XElement ce = BuildCe(desired, newline, indent);

                if (classes is not null)
                {
                    if (classes.PreviousNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
                    {
                        classes.AddBeforeSelf(ce);
                        classes.AddBeforeSelf(new XText(whitespace.Value));
                    }
                    else
                    {
                        classes.AddBeforeSelf(ce);
                        classes.AddBeforeSelf(new XText(newline + indent));
                    }
                }
                else
                {
                    doc.Root.Add(new XText(newline + indent));
                    doc.Root.Add(ce);
                }

                changed = true;
            }
        }

        return !changed || SaveEconomyCore(missionPath, doc);
    }

    public bool RemoveModTypesFiles(string missionPath, IReadOnlySet<string> fileNames)
    {
        if (fileNames.Count == 0)
        {
            return true;
        }

        XDocument? doc = LoadEconomyCore(missionPath);
        if (doc?.Root is null)
        {
            return false;
        }

        var targets = new HashSet<string>(fileNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        return !RemoveFileEntries(doc, targets) || SaveEconomyCore(missionPath, doc);
    }

    /// <summary>
    /// Removes the <c>&lt;file&gt;</c> entries whose leaf name is in
    /// <paramref name="names"/> from every ModTypes <c>ce</c> block, dropping a
    /// block (and its surrounding whitespace) once empty. Returns whether the
    /// document changed.
    /// </summary>
    private static bool RemoveFileEntries(XDocument doc, IReadOnlySet<string> names)
    {
        if (doc.Root is null)
        {
            return false;
        }

        bool changed = false;
        foreach (XElement ce in doc.Root.Descendants("ce").Where(IsModTypesCe).ToList())
        {
            foreach (XElement file in ce.Elements("file")
                         .Where(f => names.Contains((string?)f.Attribute("name") ?? string.Empty))
                         .ToList())
            {
                file.Remove();
                changed = true;
            }

            if (!ce.Elements("file").Any())
            {
                XNode? preceding = ce.PreviousNode;
                ce.Remove();
                if (preceding is XText text && string.IsNullOrWhiteSpace(text.Value))
                {
                    text.Remove();
                }

                changed = true;
            }
        }

        return changed;
    }

    private static bool IsModTypesCe(XElement ce)
    {
        string? folder = (string?)ce.Attribute("folder");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        string normalized = folder.Trim().Replace('\\', '/');
        return normalized.Equals("./db/ModTypes", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("db/ModTypes", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("ModTypes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads and parses cfgeconomycore.xml, or null when missing/malformed.</summary>
    private XDocument? LoadEconomyCore(string missionPath)
    {
        string configPath = Path.Combine(missionPath, "cfgeconomycore.xml");
        if (!_fileSystem.FileExists(configPath))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(_fileSystem.ReadAllText(configPath), LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private bool SaveEconomyCore(string missionPath, XDocument doc)
    {
        _fileSystem.WriteAllText(Path.Combine(missionPath, "cfgeconomycore.xml"), Serialize(doc));
        return true;
    }

    private static (string Newline, string Indent) DetectFormatting(XElement? classes)
    {
        if (classes?.PreviousNode is XText text)
        {
            string value = text.Value;
            int newlineIndex = value.LastIndexOf('\n');
            if (newlineIndex >= 0)
            {
                string newline = newlineIndex > 0 && value[newlineIndex - 1] == '\r' ? "\r\n" : "\n";
                return (newline, value[(newlineIndex + 1)..]);
            }
        }

        return ("\n", "\t");
    }

    private static XElement BuildCe(IReadOnlyList<string> fileNames, string newline, string indent)
    {
        var ce = new XElement("ce", new XAttribute("folder", ModTypesFolder));
        string fileIndent = indent + indent;

        foreach (string name in fileNames)
        {
            ce.Add(new XText(newline + fileIndent));
            ce.Add(new XElement("file", new XAttribute("name", name), new XAttribute("type", GetFileType(name))));
        }

        ce.Add(new XText(newline + indent));
        return ce;
    }

    private static string GetFileType(string name) => TypesFileRoles.RoleOf(name);

    private static string Serialize(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Indent = false,
            OmitXmlDeclaration = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            doc.Save(writer);
        }

        return settings.Encoding.GetString(stream.ToArray());
    }
}
