namespace DayZModManager.Core;

/// <summary>
/// Well-known paths used to resolve where the application's data files are stored.
/// </summary>
public static class AppPaths
{
    /// <summary>Default data subdirectory name, created under the server path.</summary>
    public const string DataDirectoryName = "DayZ-Mod-Manager-V2";

    /// <summary>
    /// Small anchor file kept in the legacy AppData directory that records the
    /// active data directory so it can be rediscovered on startup.
    /// </summary>
    public const string PointerFileName = "data_directory.txt";

    /// <summary>
    /// The bootstrap directory. Lives under %LOCALAPPDATA% (or the app base
    /// directory when unavailable) and is the default data directory until a
    /// server path is configured.
    /// </summary>
    public static string LegacyDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? Path.Combine(AppContext.BaseDirectory, DataDirectoryName)
            : Path.Combine(appData, DataDirectoryName);
    }

    /// <summary>
    /// Resolves the effective data directory: a per-server subfolder under
    /// <paramref name="serverPath"/> once one is configured, otherwise the
    /// legacy AppData directory.
    /// </summary>
    public static string Resolve(string? serverPath)
    {
        if (!string.IsNullOrWhiteSpace(serverPath))
        {
            return Path.Combine(serverPath, DataDirectoryName);
        }

        return LegacyDirectory();
    }
}
