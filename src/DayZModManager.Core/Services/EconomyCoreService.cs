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
        string configPath = Path.Combine(missionPath, "cfgeconomycore.xml");
        if (!_fileSystem.FileExists(configPath))
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(_fileSystem.ReadAllText(configPath), LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return false;
        }

        XElement? root = doc.Root;
        if (root is null)
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

        bool changed = false;

        // Remove only the entries this manager owns that are no longer desired;
        // keep the map's own entries and entries added by other tools/mods.
        foreach (XElement ce in root.Descendants("ce").Where(IsModTypesCe).ToList())
        {
            foreach (XElement file in ce.Elements("file")
                         .Where(f => stale.Contains((string?)f.Attribute("name") ?? string.Empty))
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

        if (desired.Count > 0)
        {
            XElement? existing = root.Descendants("ce").FirstOrDefault(IsModTypesCe);
            if (existing is not null)
            {
                (string newline, string indent) = DetectFormatting(root.Descendants("classes").FirstOrDefault());
                string fileIndent = indent + indent;
                bool added = false;

                foreach (string name in desired)
                {
                    if (existing.Elements("file")
                        .Any(f => string.Equals((string?)f.Attribute("name"), name, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    existing.Add(new XText(newline + fileIndent));
                    existing.Add(new XElement("file",
                        new XAttribute("name", name),
                        new XAttribute("type", GetFileType(name))));
                    added = true;
                }

                changed |= added;
            }
            else
            {
                XElement? classes = root.Descendants("classes").FirstOrDefault();
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
                    root.Add(new XText(newline + indent));
                    root.Add(ce);
                }

                changed = true;
            }
        }

        if (!changed)
        {
            return true;
        }

        _fileSystem.WriteAllText(configPath, Serialize(doc));
        return true;
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
