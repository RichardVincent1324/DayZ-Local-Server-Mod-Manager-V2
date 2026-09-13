using DayZModManager.Core.Services;
using DayZModManager.Core.Tests.TestDoubles;

namespace DayZModManager.Core.Tests.Services;

public class BatchFileServiceTests
{
    private const string BatPath = @"D:\DayZServer\LocalServer.example.bat";

    private static IReadOnlyList<string> ModPaths(int count) =>
        Enumerable.Range(1, count).Select(i => $"ModList/@E{i}").ToList();

    private static string JoinEntries(int from, int count)
    {
        IEnumerable<string> entries = Enumerable.Range(from, count).Select(i => $"ModList/@E{i}");
        return string.Join(';', entries);
    }

    private static List<string> ExpectedModListLines(int count)
    {
        var expected = new List<string>();
        if (count == 0)
        {
            expected.Add("set \"modList=-mod=\"");
            return expected;
        }

        bool first = true;
        for (int offset = 0; offset < count; offset += BatchFileService.ModsPerLine)
        {
            int perLine = Math.Min(BatchFileService.ModsPerLine, count - offset);
            string body = JoinEntries(offset + 1, perLine) + ";";
            expected.Add(first
                ? $"set \"modList=-mod={body}\""
                : $"set \"modList=%modList%{body}\"");
            first = false;
        }

        return expected;
    }

    /// <summary>Returns the physical lines of the file that are modList set commands.</summary>
    private static List<string> ModListLines(string content) =>
        content
            .Split('\n')
            .Select(line => line.EndsWith('\r') ? line[..^1] : line)
            .Where(line => line.StartsWith("set \"modList=", StringComparison.Ordinal))
            .ToList();

    // --- BuildModListLines -------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(25)]
    public void BuildModListLines_GroupsTenPerLine(int count)
    {
        Assert.Equal(ExpectedModListLines(count), BatchFileService.BuildModListLines(ModPaths(count)));
    }

    // --- WriteModList ------------------------------------------------------

    [Fact]
    public void WriteModList_ReplacesOnlyModListBlock()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"serverName=LocalServer\"\nset \"modList=-mod=@CF;@Old;\"\nset \"serverPort=2302\"");
        var service = new BatchFileService(fs);

        bool result = service.WriteModList(BatPath, ModPaths(12));

        Assert.True(result);
        string content = fs.TryGetFileContents(BatPath)!;
        Assert.Contains("set \"serverName=LocalServer\"", content);
        Assert.Contains("set \"serverPort=2302\"", content);
        Assert.DoesNotContain("@Old", content);
        Assert.Equal(ExpectedModListLines(12), ModListLines(content));
    }

    [Fact]
    public void WriteModList_ReplacesExistingMultiLineBlock()
    {
        var fs = new FakeFileSystem();
        string oldBlock = string.Join('\n', ExpectedModListLines(12));
        fs.AddFile(BatPath, $"{oldBlock}\nset \"serverName=X\"");
        var service = new BatchFileService(fs);

        bool result = service.WriteModList(BatPath, ModPaths(25));

        Assert.True(result);
        string content = fs.TryGetFileContents(BatPath)!;
        Assert.Equal(ExpectedModListLines(25), ModListLines(content));
        Assert.Contains("set \"serverName=X\"", content);
    }

    [Fact]
    public void WriteModList_ReturnsFalse_WhenNoModListLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"serverName=LocalServer\"");
        var service = new BatchFileService(fs);

        Assert.False(service.WriteModList(BatPath, ModPaths(2)));
    }

    [Fact]
    public void WriteModList_PreservesCrlfLineEndings()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"modList=-mod=@Old;\"\r\nset \"serverName=X\"\r\n");
        var service = new BatchFileService(fs);

        service.WriteModList(BatPath, ModPaths(12));

        string content = fs.TryGetFileContents(BatPath)!;
        List<string> expected = ExpectedModListLines(12);
        Assert.StartsWith($"{expected[0]}\r\n{expected[1]}\r\n", content);
        Assert.Contains("\r\nset \"serverName=X\"\r\n", content);
        Assert.EndsWith("\r\n", content);
        Assert.DoesNotContain("@Old", content);
    }

    [Fact]
    public void WriteModList_WritesEmptyModList_WhenNoMods()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"modList=-mod=@Old;\"");
        var service = new BatchFileService(fs);

        service.WriteModList(BatPath, Array.Empty<string>());

        Assert.Equal("set \"modList=-mod=\"", fs.TryGetFileContents(BatPath));
    }

    // --- HasModListLine ----------------------------------------------------

    [Fact]
    public void HasModListLine_ReturnsTrue_ForMultiLineBlock()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, string.Join('\n', ExpectedModListLines(25)));
        var service = new BatchFileService(fs);

        Assert.True(service.HasModListLine(BatPath));
    }

    [Fact]
    public void HasModListLine_ReturnsFalse_WhenAbsent()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"serverName=LocalServer\"");
        var service = new BatchFileService(fs);

        Assert.False(service.HasModListLine(BatPath));
    }

    // --- WriteServerProfile -------------------------------------------------

    [Fact]
    public void WriteServerProfile_ReplacesOnlyProfileLine()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(BatPath, "set \"serverProfile=map_profiles\\chernarusplus\"\nset \"serverName=X\"");
        var service = new BatchFileService(fs);

        Assert.True(service.WriteServerProfile(BatPath, "map_profiles\\sakhal"));
        Assert.Contains("set \"serverProfile=map_profiles\\sakhal\"", fs.TryGetFileContents(BatPath)!);
        Assert.Contains("set \"serverName=X\"", fs.TryGetFileContents(BatPath)!);
    }
}
