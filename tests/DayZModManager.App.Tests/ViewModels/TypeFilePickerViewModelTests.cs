using DayZModManager.App.ViewModels;

namespace DayZModManager.App.Tests.ViewModels;

public class TypeFilePickerViewModelTests
{
    private const string Base = @"D:\workshop\@InediaInfectedAI";

    private static string FileOf(string name) => $@"{Base}\{name}";

    private static HashSet<string> Set(params string[] paths) => new(paths, StringComparer.OrdinalIgnoreCase);

    private static TypeFilePickerViewModel Create(
        IReadOnlyList<string> files,
        IReadOnlySet<string>? activeFiles = null) =>
        new("@InediaInfectedAI", files, Base, activeFiles);

    [Fact]
    public void Defaults_CheckOnlyActiveFiles()
    {
        string casual = FileOf("casual_types.xml");
        string hardcore = FileOf("hardcore_types.xml");
        string casualSpawn = FileOf("casual_spawnabletypes.xml");

        TypeFilePickerViewModel vm = Create(new[] { casual, hardcore, casualSpawn }, Set(casual, casualSpawn));

        Assert.True(vm.Options.Single(o => o.FullPath == casual).IsChecked);
        Assert.False(vm.Options.Single(o => o.FullPath == hardcore).IsChecked);
        Assert.True(vm.Options.Single(o => o.FullPath == casualSpawn).IsChecked);
    }

    [Fact]
    public void Defaults_CheckNothing_WhenNothingActive()
    {
        string casual = FileOf("casual_types.xml");
        string hardcore = FileOf("hardcore_types.xml");

        TypeFilePickerViewModel vm = Create(new[] { casual, hardcore });

        Assert.All(vm.Options, o => Assert.False(o.IsChecked));
    }

    [Fact]
    public void CheckingAFile_UnchecksAlternativesOfSameRole()
    {
        string casual = FileOf("casual_types.xml");
        string hardcore = FileOf("hardcore_types.xml");
        string spawn = FileOf("casual_spawnabletypes.xml");
        TypeFilePickerViewModel vm = Create(new[] { casual, hardcore, spawn });

        vm.Options.Single(o => o.FullPath == casual).IsChecked = true;
        Assert.True(vm.Options.Single(o => o.FullPath == casual).IsChecked);

        // Selecting the other "types" alternative must drop the first one.
        vm.Options.Single(o => o.FullPath == hardcore).IsChecked = true;

        Assert.True(vm.Options.Single(o => o.FullPath == hardcore).IsChecked);
        Assert.False(vm.Options.Single(o => o.FullPath == casual).IsChecked);
        Assert.False(vm.Options.Single(o => o.FullPath == spawn).IsChecked);

        // A different role is unaffected.
        vm.Options.Single(o => o.FullPath == spawn).IsChecked = true;
        Assert.True(vm.Options.Single(o => o.FullPath == spawn).IsChecked);
        Assert.True(vm.Options.Single(o => o.FullPath == hardcore).IsChecked);
    }

    [Fact]
    public void DuplicateDefaultsForSameRole_KeepTheFirst()
    {
        string casual = FileOf("casual_types.xml");
        string hardcore = FileOf("hardcore_types.xml");
        TypeFilePickerViewModel vm = Create(new[] { casual, hardcore }, Set(casual, hardcore));

        Assert.True(vm.Options.Single(o => o.FullPath == casual).IsChecked);
        Assert.False(vm.Options.Single(o => o.FullPath == hardcore).IsChecked);
    }

    [Fact]
    public void Roles_AreClassifiedFromFileName()
    {
        string types = FileOf("casual_types.xml");
        string spawn = FileOf("casual_spawnabletypes.xml");

        TypeFilePickerViewModel vm = Create(new[] { types, spawn });

        Assert.Equal("types", vm.Options.Single(o => o.FullPath == types).Role);
        Assert.Equal("spawnabletypes", vm.Options.Single(o => o.FullPath == spawn).Role);
    }

    [Fact]
    public void GetSelectedFiles_ReturnsCheckedPaths()
    {
        string casual = FileOf("casual_types.xml");
        string spawn = FileOf("casual_spawnabletypes.xml");
        TypeFilePickerViewModel vm = Create(new[] { casual, spawn });

        vm.Options.Single(o => o.FullPath == casual).IsChecked = true;
        vm.Options.Single(o => o.FullPath == spawn).IsChecked = true;

        Assert.Equal(new[] { casual, spawn }, vm.GetSelectedFiles());
    }
}
