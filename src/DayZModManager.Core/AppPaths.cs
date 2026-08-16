namespace DayZModManager.Core;

/// <summary>
/// Well-known paths used to resolve where the application's data files are stored.
/// </summary>
public static class AppPaths
{
    /// <summary>Default data subdirectory name, created under the server path.</summary>
    public const string DataDirectoryName = "DayZ-Local-Server-Mod-Manager-Data";

    /// <summary>
    /// Small anchor file kept in the legacy AppData directory that records the
    /// active data directory so it can be rediscovered on startup.
    /// </summary>
    public const string PointerFileName = "data_directory.txt";

    /// <summary>
    /// The bootstrap directory. Lives under %LOCALAPPDATA% (or the app base
    /// directory when unavailable) and is the default data directory until the
    /// user chooses another one.
    /// </summary>
    public static string LegacyDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(AppContext.BaseDirectory, "data")
            : Path.Combine(appData, "DayZModManager");
    }

    /// <summary>
    /// Resolves the effective data directory: an explicit override wins, otherwise
    /// a per-server subfolder under <paramref name="serverPath"/>, otherwise the
    /// legacy AppData directory.
    /// </summary>
    public static string Resolve(string? dataDirectoryOverride, string? serverPath)
    {
        if (!string.IsNullOrWhiteSpace(dataDirectoryOverride))
        {
            return dataDirectoryOverride;
        }

        if (!string.IsNullOrWhiteSpace(serverPath))
        {
            return Path.Combine(serverPath, DataDirectoryName);
        }

        return LegacyDirectory();
    }
}
