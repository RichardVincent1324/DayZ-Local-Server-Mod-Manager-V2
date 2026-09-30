using DayZModManager.App.ViewModels;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Tests.ViewModels;

public class TypeFilePickerViewModelTests
{
    private const string Base = @"D:\workshop\@InediaInfectedAI";

    private static string FileOf(string name) => $@"{Base}\{name}";

    private static HashSet<string> Set(params string[] paths) => new(paths, StringComparer.OrdinalIgnoreCase);

    private static TypeFilePickerViewModel Create(
        IReadOnlyList<string> files,
        IReadOnlySet<string>? activeFiles = null,
        IReadOnlyDictionary<string, TypesFileRole>? activeRoles = null) =>
        new("@InediaInfectedAI", files, Base, activeFiles, activeRoles);

    [Fact]
    public void RecognizedAndUnrecognized_AreSeparated()
    {
        string types = FileOf("types.xml");
        string spawn = FileOf("casual_spawnabletypes.xml");
        string weird = FileOf("weird_economy_config.xml");

        TypeFilePickerViewModel vm = Create(new[] { weird, types, spawn });

        Assert.Equal(new[] { types, spawn }, vm.RecognizedOptions.Select(o => o.FullPath));
        Assert.Equal(new[] { weird }, vm.UnrecognizedOptions.Select(o => o.FullPath));
        Assert.True(vm.HasUnrecognizedOptions);
    }

    [Fact]
    public void HasUnrecognizedOptions_False_WhenAllRecognized()
    {
        string types = FileOf("types.xml");
        string spawn = FileOf("casual_spawnabletypes.xml");

        TypeFilePickerViewModel vm = Create(new[] { types, spawn });

        Assert.False(vm.HasUnrecognizedOptions);
        Assert.Empty(vm.UnrecognizedOptions);
    }

    [Fact]
    public void Defaults_CheckOnlyActiveFiles()
    {
        string types = FileOf("types.xml");
        string spawn = FileOf("casual_spawnabletypes.xml");
        string weird = FileOf("weird_economy_config.xml");

        TypeFilePickerViewModel vm = Create(new[] { types, spawn, weird }, Set(types, weird));

        Assert.True(vm.RecognizedOptions.Single(o => o.FullPath == types).IsChecked);
        Assert.True(vm.UnrecognizedOptions.Single(o => o.FullPath == weird).IsChecked);
        Assert.False(vm.RecognizedOptions.Single(o => o.FullPath == spawn).IsChecked);
    }

    [Fact]
    public void Defaults_CheckNothing_WhenNothingActive()
    {
        string types = FileOf("types.xml");
        string weird = FileOf("weird_economy_config.xml");

        TypeFilePickerViewModel vm = Create(new[] { types, weird });

        Assert.All(vm.RecognizedOptions.Concat(vm.UnrecognizedOptions), o => Assert.False(o.IsChecked));
    }

    [Fact]
    public void RecognizedFiles_UseFilenameRole()
    {
        string types = FileOf("casual_types.xml");
        string spawn = FileOf("casual_spawnabletypes.xml");

        TypeFilePickerViewModel vm = Create(new[] { types, spawn });

        Assert.Equal(TypesFileRole.Types, vm.RecognizedOptions.Single(o => o.FullPath == types).Role);
        Assert.Equal(TypesFileRole.SpawnableTypes, vm.RecognizedOptions.Single(o => o.FullPath == spawn).Role);
    }

    [Fact]
    public void UnrecognizedFile_DefaultsToTypesRole()
    {
        string weird = FileOf("weird_economy_config.xml");

        TypeFilePickerViewModel vm = Create(new[] { weird });

        Assert.Equal(TypesFileRole.Types, vm.UnrecognizedOptions.Single().Role);
    }

    [Fact]
    public void GetSelections_ReturnsCheckedFilesWithRoles()
    {
        string types = FileOf("types.xml");
        string weird = FileOf("weird_economy_config.xml");

        TypeFilePickerViewModel vm = Create(new[] { types, weird });
        vm.RecognizedOptions.Single(o => o.FullPath == types).IsChecked = true;
        var weirdOption = vm.UnrecognizedOptions.Single(o => o.FullPath == weird);
        weirdOption.IsChecked = true;
        weirdOption.Role = TypesFileRole.SpawnableTypes;

        IReadOnlyList<TypeFileSelection> selections = vm.GetSelections();

        Assert.Equal(2, selections.Count);
        Assert.Contains(selections, s => s.SourceFile == types && s.Role == TypesFileRole.Types);
        Assert.Contains(selections, s => s.SourceFile == weird && s.Role == TypesFileRole.SpawnableTypes);
    }

    [Fact]
    public void UncheckedFiles_AreNotReturned()
    {
        string types = FileOf("types.xml");
        string weird = FileOf("weird_economy_config.xml");

        TypeFilePickerViewModel vm = Create(new[] { types, weird });

        Assert.Empty(vm.GetSelections());
    }

    [Fact]
    public void ActiveUnrecognizedSpawnable_PreSelectsItsRole()
    {
        string weird = FileOf("weird_economy_config.xml");
        var roles = new Dictionary<string, TypesFileRole>(StringComparer.OrdinalIgnoreCase)
        {
            [weird] = TypesFileRole.SpawnableTypes,
        };

        TypeFilePickerViewModel vm = Create(new[] { weird }, Set(weird), roles);

        var option = vm.UnrecognizedOptions.Single();
        Assert.True(option.IsChecked);
        Assert.Equal(TypesFileRole.SpawnableTypes, option.Role);
    }
}
