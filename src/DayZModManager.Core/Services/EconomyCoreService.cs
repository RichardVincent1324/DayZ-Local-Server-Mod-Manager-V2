using System.Text;
using System.Xml;
using System.Xml.Linq;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>
/// Maintains the <c>&lt;ce folder="./db/type_files"&gt;</c> block inside a map's
/// <c>cfgeconomycore.xml</c>. Uses XML parsing (not text regexes) for robustness.
/// </summary>
public interface IEconomyCoreService
{
    /// <summary>
    /// Rewrites the type_files reference block to reference the given file names
    /// (leaf names only, e.g. "CF_types.xml"). <paramref name="folder"/> is the
    /// path written to the block's <c>folder</c> attribute — <c>./db/type_files</c>
    /// for the configured types, or a path into the data directory for a loaded
    /// save. <paramref name="desiredFileNames"/> is the set that should be
    /// referenced; <paramref name="ownedFileNames"/> is the set of leaf names
    /// this manager has previously generated for the map, so entries it owns but
    /// no longer wants can be removed while entries it does not own (the map's own
    /// files, entries added by other tools/mods) are preserved.
    /// <paramref name="fileTypes"/> assigns each desired leaf its economy type
    /// (keyed case-insensitively); a missing entry is treated as a types file.
    /// Returns false if the file is missing or malformed.
    /// </summary>
    bool UpdateTypeFiles(
        string missionPath,
        string folder,
        IReadOnlyList<string> desiredFileNames,
        IReadOnlySet<string> ownedFileNames,
        IReadOnlyDictionary<string, string> fileTypes);

    /// <summary>
    /// Returns the <c>folder</c> value of the manager-owned type_files block in a
    /// map's cfgeconomycore.xml, or null when there is no such block. Used to
    /// rediscover which types folder (configured or a loaded save) is active.
    /// </summary>
    string? GetTypeFilesFolder(string missionPath);

    /// <summary>
    /// Removes the type_files references for the given leaf names from
    /// cfgeconomycore.xml regardless of whether this manager owns them. Used to
    /// clean up orphaned files the manager does not track. Other references are
    /// preserved. Returns false if the file is missing or malformed.
    /// </summary>
    bool RemoveTypeFiles(string missionPath, IReadOnlySet<string> fileNames);
}

public sealed class EconomyCoreService : IEconomyCoreService
{
    /// <summary>The <c>folder</c> value used for the configured (world-less) types folder.</summary>
    public const string ConfiguredFolder = "./db/type_files";

    private readonly IFileSystem _fileSystem;

    public EconomyCoreService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public bool UpdateTypeFiles(string missionPath, string folder, IReadOnlyList<string> desiredFileNames, IReadOnlySet<string> ownedFileNames, IReadOnlyDictionary<string, string> fileTypes)
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
            XElement? existing = doc.Root.Descendants("ce").FirstOrDefault(IsTypeFilesCe);
            if (existing is not null)
            {
                // The block may already exist for a different active source (e.g.
                // switching between the configured folder and a loaded save), so the
                // folder attribute must be rewritten to the requested one.
                if (!string.Equals((string?)existing.Attribute("folder"), folder, StringComparison.Ordinal))
                {
                    existing.SetAttributeValue("folder", folder);
                    changed = true;
                }

                (string newline, string indent) = DetectFormatting(doc.Root.Descendants("classes").FirstOrDefault());
                string fileIndent = indent + indent;

                // Entries this manager does not own (base/third-party) must survive
                // the rewrite; keep them in their original relative order.
                List<XElement> preserved = existing.Elements("file")
                    .Where(f => !desiredSet.Contains((string?)f.Attribute("name") ?? string.Empty))
                    .ToList();

                // Rewrite when a desired entry is missing, when the desired entries
                // are not in the exact order requested, or when a desired entry's
                // "type" no longer matches the role it should have.
                List<XElement> presentDesired = existing.Elements("file")
                    .Where(f => desiredSet.Contains((string?)f.Attribute("name") ?? string.Empty))
                    .ToList();

                bool orderAndTypeMatch =
                    presentDesired.Select(f => (string?)f.Attribute("name") ?? string.Empty)
                        .SequenceEqual(desired, StringComparer.OrdinalIgnoreCase)
                    && presentDesired.All(f =>
                        string.Equals(
                            (string?)f.Attribute("type") ?? string.Empty,
                            GetFileType((string?)f.Attribute("name") ?? string.Empty, fileTypes),
                            StringComparison.OrdinalIgnoreCase));

                if (!orderAndTypeMatch)
                {
                    existing.RemoveNodes();
                    foreach (string name in desired)
                    {
                        existing.Add(new XText(newline + fileIndent));
                        existing.Add(new XElement("file",
                            new XAttribute("name", name),
                            new XAttribute("type", GetFileType(name, fileTypes))));
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
                XElement ce = BuildCe(desired, folder, newline, indent, fileTypes);

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

    public string? GetTypeFilesFolder(string missionPath)
    {
        XDocument? doc = LoadEconomyCore(missionPath);
        XElement? ce = doc?.Root?.Descendants("ce").FirstOrDefault(IsTypeFilesCe);
        return ce is null ? null : (string?)ce.Attribute("folder");
    }

    /// <summary>Convenience overload that targets the configured <c>./db/type_files</c> folder.</summary>
    public bool UpdateTypeFiles(string missionPath, IReadOnlyList<string> desiredFileNames, IReadOnlySet<string> ownedFileNames, IReadOnlyDictionary<string, string> fileTypes) =>
        UpdateTypeFiles(missionPath, ConfiguredFolder, desiredFileNames, ownedFileNames, fileTypes);

    public bool RemoveTypeFiles(string missionPath, IReadOnlySet<string> fileNames)
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
    /// <paramref name="names"/> from every type_files <c>ce</c> block, dropping a
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
        foreach (XElement ce in doc.Root.Descendants("ce").Where(IsTypeFilesCe).ToList())
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

    private static bool IsTypeFilesCe(XElement ce)
    {
        string? folder = (string?)ce.Attribute("folder");
        return !string.IsNullOrWhiteSpace(folder) && IsManagerTypeFilesFolder(folder);
    }

    /// <summary>
    /// True when a <c>folder</c> value belongs to this manager: either the
    /// configured types folder (<c>./db/type_files</c>) or one of the per-save
    /// folders under the data directory that a loaded save points at.
    /// </summary>
    internal static bool IsManagerTypeFilesFolder(string folder)
    {
        string normalized = folder.Trim().Replace('\\', '/');
        return normalized.Equals(ConfiguredFolder, StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("db/type_files", StringComparison.OrdinalIgnoreCase)
            || (normalized.EndsWith("/type_files", StringComparison.OrdinalIgnoreCase)
                && normalized.Contains("DayZ-Mod-Manager-V2", StringComparison.OrdinalIgnoreCase));
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

    private static XElement BuildCe(IReadOnlyList<string> fileNames, string folder, string newline, string indent, IReadOnlyDictionary<string, string> fileTypes)
    {
        var ce = new XElement("ce", new XAttribute("folder", folder));
        string fileIndent = indent + indent;

        foreach (string name in fileNames)
        {
            ce.Add(new XText(newline + fileIndent));
            ce.Add(new XElement("file", new XAttribute("name", name), new XAttribute("type", GetFileType(name, fileTypes))));
        }

        ce.Add(new XText(newline + indent));
        return ce;
    }

    private static string GetFileType(string name, IReadOnlyDictionary<string, string> fileTypes) =>
        fileTypes.TryGetValue(name, out string? type) && !string.IsNullOrWhiteSpace(type)
            ? TypesFileRoles.NormalizeEconomyType(type)
            : TypesFileRoles.Types;

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
