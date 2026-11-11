using DayZModManager.App.ViewModels;

namespace DayZModManager.App.Tests.ViewModels;

public class TypeFilePickerViewModelTests
{
    private const string Base = @"D:\workshop\@InediaInfectedAI";

    private static string FileOf(string name) => $@"{Base}\{name}";

    private static TypeFilePickerViewModel Create(params string[] files) =>
        new("@InediaInfectedAI", files, Base);

    [Fact]
    public void Files_AreListedWithRelativeDisplayPaths()
    {
        string types = FileOf("types.xml");
        string economy = FileOf("config\\economy.xml");

        TypeFilePickerViewModel vm = Create(types, economy);

        Assert.Equal(2, vm.Files.Count);
        Assert.Equal("types.xml", vm.Files[0].DisplayPath);
        Assert.Equal(@"config\economy.xml", vm.Files[1].DisplayPath);
    }

    [Fact]
    public void RecognizedFiles_AreGroupedFirst_AndOthersSecond()
    {
        string types = FileOf("Types.xml");
        string spawn = FileOf("casual_Spawnabletypes.xml");
        string economy = FileOf("economy.xml");
        string config = FileOf("config.xml");

        TypeFilePickerViewModel vm = Create(economy, types, config, spawn);

        Assert.Equal(new[] { types, spawn }, vm.RecognizedOptions.Select(option => option.FullPath));
        Assert.Equal(new[] { economy, config }, vm.UnrecognizedOptions.Select(option => option.FullPath));
        Assert.True(vm.ShowGroupSeparator);
    }

    [Fact]
    public void ShowGroupSeparator_IsFalse_WhenEitherGroupIsEmpty()
    {
        TypeFilePickerViewModel onlyRecognized = Create(FileOf("types.xml"));
        TypeFilePickerViewModel onlyOther = Create(FileOf("economy.xml"));

        Assert.False(onlyRecognized.ShowGroupSeparator);
        Assert.False(onlyOther.ShowGroupSeparator);
    }

    [Fact]
    public void GetSelectedFiles_ReturnsOnlyCheckedFiles()
    {
        string types = FileOf("types.xml");
        string economy = FileOf("economy.xml");

        TypeFilePickerViewModel vm = Create(types, economy);
        vm.Files.Single(option => option.FullPath == types).IsChecked = true;

        Assert.Equal(new[] { types }, vm.GetSelectedFiles());
    }

    [Fact]
    public void GetSelectedFiles_ReturnsEmpty_WhenNothingChecked()
    {
        TypeFilePickerViewModel vm = Create(FileOf("types.xml"));

        Assert.Empty(vm.GetSelectedFiles());
    }
}
