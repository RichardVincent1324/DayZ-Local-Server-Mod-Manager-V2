using DayZModManager.Core;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class DataDirectoryProviderTests
{
    private static string PointerPath() => Path.Combine(AppPaths.LegacyDirectory(), AppPaths.PointerFileName);

    private static string SettingsPath() => Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.Settings);

    [Fact]
    public void Initialize_FreshInstall_ReturnsLegacyAndWritesNoPointer()
    {
        var fs = new FakeFileSystem();
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.LegacyDirectory(), provider.Current);
        Assert.False(fs.FileExists(PointerPath()));
    }

    [Fact]
    public void Initialize_ExistingInstall_UsesLegacy_WithoutPointer()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.LegacyDirectory(), provider.Current);
        Assert.False(fs.FileExists(PointerPath()));
    }

    [Fact]
    public void Initialize_PointerFile_UsesPointedDirectory()
    {
        var fs = new FakeFileSystem();
        fs.AddDirectory(@"D:\elsewhere");
        fs.AddFile(PointerPath(), @"D:\elsewhere");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(@"D:\elsewhere", provider.Current);
    }

    [Fact]
    public void Initialize_StalePointerToMissingDirectory_FallsBackToLegacy()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(PointerPath(), @"D:\deleted");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.LegacyDirectory(), provider.Current);
    }

    [Fact]
    public void Initialize_EmptyPointer_FallsBackToLegacy()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(PointerPath(), "   ");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.LegacyDirectory(), provider.Current);
    }

    [Fact]
    public void Resolve_FreshInstallWithServerPath_UsesServerPathDefault()
    {
        var provider = new DataDirectoryProvider(new FakeFileSystem());
        provider.Initialize();

        Assert.Equal(
            Path.Combine(@"D:\server", AppPaths.DataDirectoryName),
            provider.Resolve(new Settings { ServerPath = @"D:\server" }));
    }

    [Fact]
    public void Resolve_ExistingInstallWithServerPath_UsesServerPathDefault()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        Assert.Equal(
            Path.Combine(@"D:\server", AppPaths.DataDirectoryName),
            provider.Resolve(new Settings { ServerPath = @"D:\server" }));
    }

    [Fact]
    public void MoveTo_MigratesTypesConfig_UpdatesPointerAndCurrent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        fs.AddFile(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.TypesConfig), "{}");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        string target = Path.Combine(@"D:\server", AppPaths.DataDirectoryName);
        provider.MoveTo(target, new Settings());

        Assert.Equal(target, provider.Current);
        Assert.True(fs.FileExists(Path.Combine(target, ConfigFileNames.Settings)));
        Assert.True(fs.FileExists(Path.Combine(target, ConfigFileNames.TypesConfig)));
        Assert.False(fs.FileExists(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.TypesConfig)));
        Assert.Equal(target, fs.TryGetFileContents(PointerPath()));
    }

    [Fact]
    public void MoveTo_DoesNotMigrate_WhenSameDirectory()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        provider.MoveTo(AppPaths.LegacyDirectory(), new Settings());

        Assert.True(fs.FileExists(SettingsPath()));
    }

    [Fact]
    public void MoveTo_DoesNotOverwriteTargetModOrder()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        fs.AddFile(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.TypesConfig), "{}");
        fs.AddFile(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.ModOrder), "[\"@legacy\"]");

        // The ApplyService has already written the authoritative mod order into
        // the target before relocation runs.
        string target = Path.Combine(@"D:\server", AppPaths.DataDirectoryName);
        fs.AddFile(Path.Combine(target, ConfigFileNames.ModOrder), "[\"@applied\"]");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        provider.MoveTo(target, new Settings());

        Assert.Equal(target, provider.Current);
        Assert.Equal("[\"@applied\"]", fs.TryGetFileContents(Path.Combine(target, ConfigFileNames.ModOrder)));
        Assert.True(fs.FileExists(Path.Combine(target, ConfigFileNames.TypesConfig)));
    }

    [Fact]
    public void MoveTo_MigratesSavesLibrary()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        string legacySaves = Path.Combine(AppPaths.LegacyDirectory(), SaveGameService.SavesRootName);
        string map = "dayzOffline.chernarusplus";
        fs.AddDirectory(legacySaves, map);
        fs.AddDirectory(Path.Combine(legacySaves, map), "Alpha");
        fs.AddFile(Path.Combine(legacySaves, map, "Alpha", "players.db"), "data");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        string target = Path.Combine(@"D:\server", AppPaths.DataDirectoryName);
        provider.MoveTo(target, new Settings());

        string targetSave = Path.Combine(target, SaveGameService.SavesRootName, map, "Alpha", "players.db");
        Assert.True(fs.FileExists(targetSave));
        Assert.False(fs.DirectoryExists(legacySaves));
    }

    [Fact]
    public void MoveTo_PersistsSettingsJson_InTarget()
    {
        var fs = new FakeFileSystem();
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        provider.MoveTo(@"D:\custom", new Settings { WorkshopPath = @"D:\ws" });

        string? json = fs.TryGetFileContents(@"D:\custom\settings.json");
        Assert.NotNull(json);
        Assert.Contains("\"workshopPath\"", json);
    }

    [Fact]
    public void Initialize_DoesNotWrite_WhenHonoringPointer()
    {
        // Honoring a pointer performs no writes, so a write failure must not surface.
        var fs = new FailingFileSystem { ThrowOnWriteAllText = true };
        fs.AddDirectory(@"D:\elsewhere");
        fs.AddFile(PointerPath(), @"D:\elsewhere");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(@"D:\elsewhere", provider.Current);
    }

    [Fact]
    public void MoveTo_DoesNotThrow_WhenMigrationCopyFails()
    {
        var fs = new FailingFileSystem { ThrowOnCopyFile = true };
        fs.AddFile(SettingsPath(), "{}");
        fs.AddFile(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.TypesConfig), "{}");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        provider.MoveTo(@"D:\new", new Settings());

        Assert.Equal(@"D:\new", provider.Current);
    }

    /// <summary>Wraps <see cref="FakeFileSystem"/> and can fail writes/copies on demand.</summary>
    private sealed class FailingFileSystem : IFileSystem
    {
        private readonly FakeFileSystem _inner = new();

        public bool ThrowOnWriteAllText { get; set; }
        public bool ThrowOnCopyFile { get; set; }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetDirectories(string path) => _inner.GetDirectories(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) =>
            _inner.GetFiles(path, searchPattern, recursive);

        public void CopyFile(string sourcePath, string destinationPath)
        {
            if (ThrowOnCopyFile)
            {
                throw new IOException("copy failed");
            }

            _inner.CopyFile(sourcePath, destinationPath);
        }

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public void WriteAllText(string path, string contents)
        {
            if (ThrowOnWriteAllText)
            {
                throw new IOException("write failed");
            }

            _inner.WriteAllText(path, contents);
        }

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void CopyDirectory(string sourcePath, string destinationPath) =>
            _inner.CopyDirectory(sourcePath, destinationPath);

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(sourcePath, destinationPath);

        public void AddFile(string path, string contents) => _inner.AddFile(path, contents);

        public void AddDirectory(string path) => _inner.CreateDirectory(path);
    }
}
