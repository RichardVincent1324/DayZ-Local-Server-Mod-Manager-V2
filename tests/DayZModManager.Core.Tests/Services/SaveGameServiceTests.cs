using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class SaveGameServiceTests
{
    private const string ServerPath = @"D:\DayZServer";
    private const string MissionPath = @"D:\DayZServer\mpmissions\dayzOffline.chernarusplus";
    private const string MapName = "dayzOffline.chernarusplus";
    private const string DataDirectory = @"D:\app\data";

    private static string ConfigWithInstance(int id) =>
        $"template=\"{MapName}\"\ninstanceId = {id};\n";

    private static SaveGameService CreateService(FakeFileSystem fs) => new(fs);

    private static void SeedLiveStorage(FakeFileSystem fs, string folderName = "storage_1")
    {
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));
        fs.AddDirectory($@"{MissionPath}", folderName);
        fs.AddFile($@"{MissionPath}\{folderName}\players.db", "live-data");
    }

    private static void SeedStoredSave(FakeFileSystem fs, string saveName, string contents = "saved-data")
    {
        string library = $@"{DataDirectory}\Saves\{MapName}";
        fs.AddDirectory(library, saveName);
        fs.AddFile($@"{library}\{saveName}\players.db", contents);
    }

    private static string LibraryPath(string saveName) =>
        $@"{DataDirectory}\Saves\{MapName}\{saveName}";

    [Fact]
    public void ReadInstanceId_ParsesValueFromServerDz()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(4));

        int id = CreateService(fs).ReadInstanceId(ServerPath);

        Assert.Equal(4, id);
    }

    [Fact]
    public void ReadInstanceId_DefaultsToOne_WhenAbsent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", "template=\"x\"\n");

        Assert.Equal(1, CreateService(fs).ReadInstanceId(ServerPath));
    }

    [Fact]
    public void ReadInstanceId_DefaultsToOne_WhenFileMissing()
    {
        Assert.Equal(1, CreateService(new FakeFileSystem()).ReadInstanceId(ServerPath));
    }

    [Fact]
    public void GetStorageFolderPath_IncludesInstanceId()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(3));

        string path = CreateService(fs).GetStorageFolderPath(ServerPath, MapName);

        Assert.Equal($@"{MissionPath}\storage_3", path);
    }

    [Fact]
    public void AddSave_CopiesLiveStorageIntoLibrary()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SaveGameService service = CreateService(fs);

        SaveGameResult result = service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false);

        Assert.True(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{LibraryPath("Alpha")}\players.db"));
    }

    [Fact]
    public void AddSave_ReturnsFailure_WhenLiveStorageMissing()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false);

        Assert.False(result.Success);
    }

    [Fact]
    public void AddSave_RejectsDuplicate_UnlessOverwrite()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedStoredSave(fs, "Alpha");
        SaveGameService service = CreateService(fs);

        Assert.False(service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: false).Success);
        Assert.True(service.AddSave(ServerPath, MapName, DataDirectory, "Alpha", overwrite: true).Success);
    }

    [Fact]
    public void AddSave_RejectsInvalidName()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, "bad\\name", overwrite: false);

        Assert.False(result.Success);
    }

    [Fact]
    public void ListSaves_ReturnsStoredFolders()
    {
        var fs = new FakeFileSystem();
        SeedStoredSave(fs, "Alpha");
        SeedStoredSave(fs, "Beta");

        IReadOnlyList<string> saves = CreateService(fs).ListSaves(DataDirectory, MapName);

        Assert.Equal(new[] { "Alpha", "Beta" }, saves);
    }

    [Fact]
    public void LoadSave_ReplacesLiveStorage_AndRemovesStaging()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        SeedStoredSave(fs, "Alpha", "new-world");
        SaveGameService service = CreateService(fs);

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.old"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.restore"));
    }

    [Fact]
    public void LoadSave_CreatesLiveStorage_WhenAbsent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));
        fs.AddDirectory(MissionPath);
        SeedStoredSave(fs, "Alpha", "new-world");

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_ReturnsFailure_WhenSaveMissing()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).LoadSave(ServerPath, MapName, DataDirectory, "Ghost");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void NewGame_DeletesLiveStorage()
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);

        SaveGameResult result = CreateService(fs).NewGame(ServerPath, MapName);

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1"));
    }

    [Fact]
    public void NewGame_NoOp_WhenNoLiveStorage()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));

        SaveGameResult result = CreateService(fs).NewGame(ServerPath, MapName);

        Assert.True(result.Success);
    }

    [Fact]
    public void DeleteSave_RemovesStoredSave()
    {
        var fs = new FakeFileSystem();
        SeedStoredSave(fs, "Alpha");
        SaveGameService service = CreateService(fs);

        SaveGameResult result = service.DeleteSave(DataDirectory, MapName, "Alpha");

        Assert.True(result.Success);
        Assert.False(fs.DirectoryExists(LibraryPath("Alpha")));
    }

    [Fact]
    public void DeleteSave_ReturnsFailure_WhenSaveMissing()
    {
        var fs = new FakeFileSystem();

        SaveGameResult result = CreateService(fs).DeleteSave(DataDirectory, MapName, "Ghost");

        Assert.False(result.Success);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("trailing.")]
    [InlineData("bad\\name")]
    public void AddSave_RejectsUnsafeNames(string saveName)
    {
        var fs = new FakeFileSystem();
        SeedLiveStorage(fs);
        fs.AddDirectory($@"{DataDirectory}\Saves\{MapName}");

        SaveGameResult result = CreateService(fs).AddSave(ServerPath, MapName, DataDirectory, saveName, overwrite: true);

        Assert.False(result.Success);
    }

    [Fact]
    public void DeleteSave_RejectsDotDot_AndDoesNotTouchLibrary()
    {
        var fs = new FakeFileSystem();
        SeedStoredSave(fs, "Alpha");

        SaveGameResult result = CreateService(fs).DeleteSave(DataDirectory, MapName, "..");

        Assert.False(result.Success);
        Assert.True(fs.DirectoryExists($@"{DataDirectory}\Saves\{MapName}"));
        Assert.True(fs.DirectoryExists(LibraryPath("Alpha")));
    }

    [Fact]
    public void LoadSave_ReportsSuccess_WhenBackupCleanupFails()
    {
        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedStoredSave(inner, "Alpha", "new-world");
        var fs = new FaultyFileSystem(inner) { DeleteThrowsWhen = path => path.EndsWith(".old", StringComparison.OrdinalIgnoreCase) };
        SaveGameService service = new(fs);

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
    }

    [Fact]
    public void LoadSave_CleansStagingFolder_AndPreservesLive_WhenPromotionFails()
    {        var inner = new FakeFileSystem();
        SeedLiveStorage(inner);
        SeedStoredSave(inner, "Alpha", "new-world");
        var fs = new FaultyFileSystem(inner) { MoveThrowsWhen = path => path.Contains(".restore", StringComparison.OrdinalIgnoreCase) };
        SaveGameService service = new(fs);

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.False(result.Success);
        Assert.Equal("live-data", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.restore"));
    }

    [Fact]
    public void LoadSave_Recovers_WhenInterruptedRunLeftOnlyBackup()
    {
        var fs = new FakeFileSystem();
        fs.AddFile($@"{ServerPath}\serverDZ.cfg", ConfigWithInstance(1));
        fs.AddDirectory($@"{MissionPath}", "storage_1.old"); // interrupted run: live missing, .old holds the world
        fs.AddFile($@"{MissionPath}\storage_1.old\players.db", "old-world");
        SeedStoredSave(fs, "Alpha", "new-world");
        SaveGameService service = new(fs);

        SaveGameResult result = service.LoadSave(ServerPath, MapName, DataDirectory, "Alpha");

        Assert.True(result.Success);
        Assert.Equal("new-world", fs.TryGetFileContents($@"{MissionPath}\storage_1\players.db"));
        Assert.False(fs.DirectoryExists($@"{MissionPath}\storage_1.old"));
    }

    /// <summary>Wraps <see cref="FakeFileSystem"/> and can fail folder operations on demand.</summary>
    private sealed class FaultyFileSystem : IFileSystem
    {
        private readonly FakeFileSystem _inner;

        public FaultyFileSystem(FakeFileSystem inner) => _inner = inner;

        public Func<string, bool>? MoveThrowsWhen { get; set; }

        public Func<string, bool>? DeleteThrowsWhen { get; set; }

        public void AddFile(string path, string contents) => _inner.AddFile(path, contents);

        public void AddDirectory(string path, params string[] childDirectoryNames) => _inner.AddDirectory(path, childDirectoryNames);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public IReadOnlyList<string> GetDirectories(string path) => _inner.GetDirectories(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public IReadOnlyList<string> GetFiles(string path, string searchPattern, bool recursive) =>
            _inner.GetFiles(path, searchPattern, recursive);

        public string? TryGetFileContents(string path) => _inner.TryGetFileContents(path);

        public void CopyFile(string sourcePath, string destinationPath) => _inner.CopyFile(sourcePath, destinationPath);

        public void DeleteFile(string path) => _inner.DeleteFile(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void CopyDirectory(string sourcePath, string destinationPath) => _inner.CopyDirectory(sourcePath, destinationPath);

        public void DeleteDirectory(string path, bool recursive)
        {
            if (DeleteThrowsWhen?.Invoke(path) == true)
            {
                throw new IOException("delete failed");
            }

            _inner.DeleteDirectory(path, recursive);
        }

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (MoveThrowsWhen?.Invoke(sourcePath) == true)
            {
                throw new IOException("move failed");
            }

            _inner.MoveDirectory(sourcePath, destinationPath);
        }
    }
}
