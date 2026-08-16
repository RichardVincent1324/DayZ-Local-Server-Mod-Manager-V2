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
    public void Initialize_ExistingInstall_PinsLegacy()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.LegacyDirectory(), provider.Current);
        Assert.True(fs.FileExists(PointerPath()));
        Assert.Equal(AppPaths.LegacyDirectory(), fs.TryGetFileContents(PointerPath()));
    }

    [Fact]
    public void Initialize_PointerFile_UsesPointedDirectory()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(PointerPath(), @"D:\elsewhere");
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(@"D:\elsewhere", provider.Current);
    }

    [Fact]
    public void Resolve_UnpinnedFreshInstall_UsesServerPathDefault()
    {
        var provider = new DataDirectoryProvider(new FakeFileSystem());
        provider.Initialize();

        Assert.Equal(
            Path.Combine(@"D:\server", AppPaths.DataDirectoryName),
            provider.Resolve(new Settings { ServerPath = @"D:\server" }));
    }

    [Fact]
    public void Resolve_PinnedExistingInstall_KeepsPinnedLocation()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        Assert.Equal(AppPaths.LegacyDirectory(), provider.Resolve(new Settings { ServerPath = @"D:\server" }));
    }

    [Fact]
    public void Resolve_ExplicitOverride_Wins()
    {
        var provider = new DataDirectoryProvider(new FakeFileSystem());
        provider.Initialize();

        Assert.Equal(@"D:\custom", provider.Resolve(new Settings { DataDirectory = @"D:\custom" }));
    }

    [Fact]
    public void MoveTo_MigratesDataFiles_UpdatesPointerAndCurrent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(SettingsPath(), "{}");
        fs.AddFile(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.ModOrder), "[]");
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        string target = @"D:\server\DayZ-Local-Server-Mod-Manager-Data";
        provider.MoveTo(target, new Settings());

        Assert.Equal(target, provider.Current);
        Assert.True(fs.FileExists(Path.Combine(target, ConfigFileNames.Settings)));
        Assert.True(fs.FileExists(Path.Combine(target, ConfigFileNames.ModOrder)));
        Assert.False(fs.FileExists(SettingsPath()));
        Assert.False(fs.FileExists(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.ModOrder)));
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
    public void MoveTo_PersistsDataDirectoryOverride_InSettingsJson()
    {
        var fs = new FakeFileSystem();
        var provider = new DataDirectoryProvider(fs);
        provider.Initialize();

        provider.MoveTo(@"D:\custom", new Settings { DataDirectory = @"D:\custom" });

        string? json = fs.TryGetFileContents(@"D:\custom\settings.json");
        Assert.NotNull(json);
        Assert.Contains("\"dataDirectory\"", json);
    }

    [Fact]
    public void Initialize_DoesNotThrow_WhenPointerWriteFails()
    {
        var fs = new FailingFileSystem { ThrowOnWriteAllText = true };
        fs.AddFile(SettingsPath(), "{}"); // existing install -> pins legacy by writing the pointer
        var provider = new DataDirectoryProvider(fs);

        provider.Initialize();

        Assert.Equal(AppPaths.LegacyDirectory(), provider.Current);
    }

    [Fact]
    public void MoveTo_DoesNotThrow_WhenMigrationCopyFails()
    {
        var fs = new FailingFileSystem { ThrowOnCopyFile = true };
        fs.AddFile(SettingsPath(), "{}");
        fs.AddFile(Path.Combine(AppPaths.LegacyDirectory(), ConfigFileNames.ModOrder), "[]");
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

        public void AddFile(string path, string contents) => _inner.AddFile(path, contents);
    }
}
