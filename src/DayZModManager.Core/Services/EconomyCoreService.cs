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
    /// (leaf names only, e.g. "CF_types.xml"). An empty list removes the block.
    /// Returns false if the file is missing or malformed.
    /// </summary>
    bool UpdateModTypes(string missionPath, IReadOnlyList<string> fileNames);
}

public sealed class EconomyCoreService : IEconomyCoreService
{
    private const string ModTypesFolder = "./db/ModTypes";

    private readonly IFileSystem _fileSystem;

    public EconomyCoreService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public bool UpdateModTypes(string missionPath, IReadOnlyList<string> fileNames)
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

        foreach (XElement ce in root.Descendants("ce").Where(IsModTypesCe).ToList())
        {
            XNode? preceding = ce.PreviousNode;
            ce.Remove();
            if (preceding is XText text && string.IsNullOrWhiteSpace(text.Value))
            {
                text.Remove();
            }
        }

        if (fileNames.Count > 0)
        {
            XElement? classes = root.Descendants("classes").FirstOrDefault();
            (string newline, string indent) = DetectFormatting(classes);
            XElement ce = BuildCe(fileNames, newline, indent);

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
        }

        _fileSystem.WriteAllText(configPath, Serialize(doc));
        return true;
    }

    private static bool IsModTypesCe(XElement ce)
    {
        string? folder = (string?)ce.Attribute("folder");
        return folder is "./db/ModTypes" or @"db\ModTypes" or "ModTypes";
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
            string type = name.Contains("spawnable", StringComparison.OrdinalIgnoreCase)
                ? "spawnabletypes"
                : "types";

            ce.Add(new XText(newline + fileIndent));
            ce.Add(new XElement("file", new XAttribute("name", name), new XAttribute("type", type)));
        }

        ce.Add(new XText(newline + indent));
        return ce;
    }

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
