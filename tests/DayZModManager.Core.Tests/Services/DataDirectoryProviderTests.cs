using DayZModManager.Core;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class DataDirectoryProviderTests
{
    private static string PointerPath() => Path.Combine(AppPaths.BootstrapDirectory(), AppPaths.PointerFileName);

    private static string SettingsPath() => Path.Combine(AppPaths.BootstrapDirectory(), ConfigFileNames.Settings);

    private static string PresetsPath(string root) => Path.Combine(root, PresetPaths.PresetsDirectoryName);

    [Fact]
    public void Initialize_FreshInstall_ReturnsBootstrapAndWritesNoPointer()
    {
        var fs = new FakeFileSystem();
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.BootstrapDirectory(), provider.Current);
        Assert.False(fs.FileExists(PointerPath()));
    }

    [Fact]
    public void Initialize_ExistingInstall_UsesBootstrap_WithoutPointer()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.BootstrapDirectory(), provider.Current);
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
    public void Initialize_StalePointerToMissingDirectory_FallsBackToBootstrap()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(PointerPath(), @"D:\deleted");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.BootstrapDirectory(), provider.Current);
    }

    [Fact]
    public void Initialize_EmptyPointer_FallsBackToBootstrap()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(PointerPath(), "   ");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.BootstrapDirectory(), provider.Current);
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
    public void MoveTo_MigratesPresets_UpdatesPointerAndCurrent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        string sourcePresetDir =
            Path.Combine(PresetsPath(AppPaths.BootstrapDirectory()), "map", "__default_preset__");
        fs.AddDirectory(sourcePresetDir);
        fs.AddFile(Path.Combine(sourcePresetDir, "mod_order.json"), "[\"@mod\"]");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        string target = Path.Combine(@"D:\server", AppPaths.DataDirectoryName);
        provider.MoveTo(target, new Settings());

        Assert.Equal(target, provider.Current);
        Assert.True(fs.FileExists(Path.Combine(target, ConfigFileNames.Settings)));
        Assert.True(fs.FileExists(
            Path.Combine(PresetsPath(target), "map", "__default_preset__", "mod_order.json")));
        Assert.False(fs.DirectoryExists(PresetsPath(AppPaths.BootstrapDirectory())));
        Assert.Equal(target, fs.TryGetFileContents(PointerPath()));
    }

    [Fact]
    public void MoveTo_DoesNotMigrate_WhenSameDirectory()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        provider.MoveTo(AppPaths.BootstrapDirectory(), new Settings());

        Assert.True(fs.FileExists(SettingsPath()));
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
    public void MoveTo_Throws_WhenPresetsMigrationFails_AndKeepsSource()
    {
        var fs = new FailingFileSystem { ThrowOnCopyDirectory = true };
        fs.AddFile(SettingsPath(), "{}");
        string sourcePresetDir =
            Path.Combine(PresetsPath(AppPaths.BootstrapDirectory()), "map", "__default_preset__");
        fs.AddDirectory(sourcePresetDir);
        string sourcePresetFile = Path.Combine(sourcePresetDir, "settings.json");
        fs.AddFile(sourcePresetFile, "{}");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        Assert.Throws<IOException>(() => provider.MoveTo(@"D:\new", new Settings()));

        // The pointer and current directory stay on the source so the presets are
        // not orphaned.
        Assert.Equal(AppPaths.BootstrapDirectory(), provider.Current);
        Assert.True(fs.FileExists(sourcePresetFile));
        Assert.False(fs.FileExists(PointerPath()));
    }

    /// <summary>Wraps <see cref="FakeFileSystem"/> and can fail writes/copies on demand.</summary>
    private sealed class FailingFileSystem : IFileSystem
    {
        private readonly FakeFileSystem _inner = new();

        public bool ThrowOnWriteAllText { get; set; }
        public bool ThrowOnCopyFile { get; set; }
        public bool ThrowOnCopyDirectory { get; set; }

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

        public void CopyDirectory(string sourcePath, string destinationPath)
        {
            if (ThrowOnCopyDirectory)
            {
                throw new IOException("copy directory failed");
            }

            _inner.CopyDirectory(sourcePath, destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(sourcePath, destinationPath);

        public void AddFile(string path, string contents) => _inner.AddFile(path, contents);

        public void AddDirectory(string path) => _inner.CreateDirectory(path);
    }
}
