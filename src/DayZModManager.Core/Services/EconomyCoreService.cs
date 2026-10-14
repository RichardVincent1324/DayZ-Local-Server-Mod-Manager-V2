using System.Text;
using System.Xml;
using System.Xml.Linq;
using DayZModManager.Core.Abstractions;

namespace DayZModManager.Core.Services;

/// <summary>Outcome of rewriting the manager-owned type_files block, plus any warnings.</summary>
public sealed record EconomyCoreUpdateResult(
    bool Success,
    IReadOnlyList<string> Warnings,
    bool UnmanagedLegacyBlockDetected = false)
{
    public static EconomyCoreUpdateResult Ok(params string[] warnings) => new(true, warnings);

    public static EconomyCoreUpdateResult Failed() => new(false, Array.Empty<string>());
}

/// <summary>
/// Maintains the manager-owned <c>&lt;ce&gt;</c> block inside a map's
/// <c>cfgeconomycore.xml</c>. Uses XML parsing (not text regexes) for robustness.
/// <para>
/// Ownership is declared by the <c>dzmmv2-managed-by="DayZ-Mod-Manager-V2"</c>
/// attribute: the manager fully rewrites the children of the block carrying that
/// marker and never touches any other <c>ce</c> block. This survives tool
/// reinstalls (the marker lives in the server's own file) and needs no external
/// ownership tracking.
/// </para>
/// </summary>
public interface IEconomyCoreService
{
    /// <summary>
    /// Rewrites the manager-owned <c>&lt;ce&gt;</c> block to reference exactly
    /// <paramref name="desiredFileNames"/> (leaf names only, e.g. "CF_types.xml").
    /// <paramref name="folder"/> is written to the block's <c>folder</c> attribute
    /// and is rewritten on every call (the active preset's <c>type_files</c> folder,
    /// or a save's). The block's children are fully replaced; the element, its
    /// marker and its <c>folder</c> are preserved. When no marked block exists, a
    /// new one is created; an existing unmarked <c>ce</c> block is left untouched
    /// and reported as a warning. <paramref name="fileTypes"/> assigns each desired
    /// leaf its economy type (keyed case-insensitively); a missing entry is treated
    /// as a types file.
    /// </summary>
    EconomyCoreUpdateResult UpdateTypeFiles(
        string missionPath,
        string folder,
        IReadOnlyList<string> desiredFileNames,
        IReadOnlyDictionary<string, string> fileTypes);

    /// <summary>
    /// Returns the <c>folder</c> value of the manager-owned (marked) type_files
    /// block, or null when there is no such block.
    /// </summary>
    string? GetTypeFilesFolder(string missionPath);

    /// <summary>
    /// Removes the named <c>&lt;file&gt;</c> entries from the manager-owned (marked)
    /// block only; other blocks are preserved. The marked block itself is kept even
    /// when it becomes empty. Returns false if the file is missing or malformed.
    /// </summary>
    bool RemoveTypeFiles(string missionPath, IReadOnlySet<string> fileNames);
}

public sealed class EconomyCoreService : IEconomyCoreService
{
    /// <summary>The <c>folder</c> value used for the configured (world-less) types folder.</summary>
    public const string ConfiguredFolder = "./db/type_files";

    /// <summary>Attribute marking a <c>&lt;ce&gt;</c> block as fully owned by this manager.</summary>
    public const string ManagerMarkerAttribute = "dzmmv2-managed-by";

    /// <summary>Exact value of <see cref="ManagerMarkerAttribute"/> identifying this manager.</summary>
    public const string ManagerMarkerValue = "DayZ-Mod-Manager-V2";

    private readonly IFileSystem _fileSystem;

    public EconomyCoreService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public EconomyCoreUpdateResult UpdateTypeFiles(
        string missionPath, string folder, IReadOnlyList<string> desiredFileNames, IReadOnlyDictionary<string, string> fileTypes)
    {
        XDocument? doc = LoadEconomyCore(missionPath);
        if (doc?.Root is null)
        {
            return EconomyCoreUpdateResult.Failed();
        }

        // Preserve the caller's ordering (mod load order, types before
        // spawnabletypes) — a HashSet would destroy it. The set is used only for
        // case-insensitive membership checks.
        List<string> desired = desiredFileNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var warnings = new List<string>();
        XElement? classes = doc.Root.Descendants("classes").FirstOrDefault();
        (string newline, string indent) = DetectFormatting(classes);

        XElement? managed = doc.Root.Descendants("ce").FirstOrDefault(IsManagedCe);
        bool changed;
        bool unmanagedLegacyBlock = false;
        if (managed is not null)
        {
            changed = RewriteManagedBlock(managed, folder, desired, newline, indent, fileTypes);
        }
        else
        {
            // Legacy or third-party block: never touch it. Warn only when it looks
            // like an older V2 block (a base-game "./db/type_files" block is not).
            unmanagedLegacyBlock = doc.Root.Descendants("ce").Any(LooksLikeLegacyManagerBlock);
            if (unmanagedLegacyBlock)
            {
                warnings.Add(
                    "WARNING: Found existing unmanaged <ce> block. Will create a brand-new V2-managed <ce> block. " +
                    "Please manually remove or migrate the old <ce> entry inside cfgeconomycore.xml.");
            }

            InsertCe(doc, BuildCe(desired, folder, newline, indent, fileTypes), classes, newline, indent);
            changed = true;
        }

        bool success = !changed || SaveEconomyCore(missionPath, doc);
        return new EconomyCoreUpdateResult(success, warnings, unmanagedLegacyBlock);
    }

    public string? GetTypeFilesFolder(string missionPath)
    {
        XDocument? doc = LoadEconomyCore(missionPath);
        XElement? ce = doc?.Root?.Descendants("ce").FirstOrDefault(IsManagedCe);
        return ce is null ? null : (string?)ce.Attribute("folder");
    }

    /// <summary>Convenience overload that targets the configured <c>./db/type_files</c> folder.</summary>
    public EconomyCoreUpdateResult UpdateTypeFiles(
        string missionPath, IReadOnlyList<string> desiredFileNames, IReadOnlyDictionary<string, string> fileTypes) =>
        UpdateTypeFiles(missionPath, ConfiguredFolder, desiredFileNames, fileTypes);

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

        var targets = new HashSet<string>(
            fileNames.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);

        bool changed = false;
        foreach (XElement ce in doc.Root.Descendants("ce").Where(IsManagedCe).ToList())
        {
            foreach (XElement file in ce.Elements("file")
                         .Where(f => targets.Contains((string?)f.Attribute("name") ?? string.Empty))
                         .ToList())
            {
                file.Remove();
                changed = true;
            }
        }

        return !changed || SaveEconomyCore(missionPath, doc);
    }

    /// <summary>
    /// Fully rewrites a marked block's children to exactly <paramref name="desired"/>,
    /// keeping the element, its marker and its (rewritten) <c>folder</c>. Returns
    /// whether anything changed, so an already-correct block is not rewritten.
    /// </summary>
    private static bool RewriteManagedBlock(
        XElement ce, string folder, IReadOnlyList<string> desired, string newline, string indent,
        IReadOnlyDictionary<string, string> fileTypes)
    {
        bool changed = false;

        if (!string.Equals((string?)ce.Attribute("folder"), folder, StringComparison.Ordinal))
        {
            ce.SetAttributeValue("folder", folder);
            changed = true;
        }

        if (!string.Equals(
            (string?)ce.Attribute(ManagerMarkerAttribute), ManagerMarkerValue, StringComparison.Ordinal))
        {
            ce.SetAttributeValue(ManagerMarkerAttribute, ManagerMarkerValue);
            changed = true;
        }

        List<XElement> current = ce.Elements("file").ToList();
        bool matches = current.Count == desired.Count
            && current.Select(f => (string?)f.Attribute("name") ?? string.Empty)
                .SequenceEqual(desired, StringComparer.OrdinalIgnoreCase)
            && current.All(f => string.Equals(
                (string?)f.Attribute("type") ?? string.Empty,
                GetFileType((string?)f.Attribute("name") ?? string.Empty, fileTypes),
                StringComparison.OrdinalIgnoreCase));

        if (!matches)
        {
            // RemoveNodes (not RemoveAll) so the folder and marker attributes survive.
            ce.RemoveNodes();
            string fileIndent = indent + indent;
            foreach (string name in desired)
            {
                ce.Add(new XText(newline + fileIndent));
                ce.Add(new XElement("file",
                    new XAttribute("name", name),
                    new XAttribute("type", GetFileType(name, fileTypes))));
            }

            if (desired.Count > 0)
            {
                ce.Add(new XText(newline + indent));
            }

            changed = true;
        }

        return changed;
    }

    private static bool IsManagedCe(XElement ce) =>
        string.Equals((string?)ce.Attribute(ManagerMarkerAttribute), ManagerMarkerValue, StringComparison.Ordinal);

    /// <summary>
    /// True when an unmarked block looks like an older V2 block (its folder points
    /// into the manager's data directory). The base-game <c>./db/type_files</c>
    /// block does not match, so a fresh install never warns.
    /// </summary>
    private static bool LooksLikeLegacyManagerBlock(XElement ce)
    {
        string? folder = (string?)ce.Attribute("folder");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        string normalized = folder.Trim().Replace('\\', '/');
        return normalized.EndsWith("/type_files", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("DayZ-Mod-Manager-V2", StringComparison.OrdinalIgnoreCase);
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

    private static XElement BuildCe(
        IReadOnlyList<string> fileNames, string folder, string newline, string indent,
        IReadOnlyDictionary<string, string> fileTypes)
    {
        var ce = new XElement("ce",
            new XAttribute("folder", folder),
            new XAttribute(ManagerMarkerAttribute, ManagerMarkerValue));
        string fileIndent = indent + indent;

        foreach (string name in fileNames)
        {
            ce.Add(new XText(newline + fileIndent));
            ce.Add(new XElement("file",
                new XAttribute("name", name),
                new XAttribute("type", GetFileType(name, fileTypes))));
        }

        if (fileNames.Count > 0)
        {
            ce.Add(new XText(newline + indent));
        }

        return ce;
    }

    /// <summary>Inserts a new block before <c>&lt;classes&gt;</c>, matching its indentation.</summary>
    private static void InsertCe(XDocument doc, XElement ce, XElement? classes, string newline, string indent)
    {
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
            doc.Root!.Add(new XText(newline + indent));
            doc.Root.Add(ce);
        }
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
