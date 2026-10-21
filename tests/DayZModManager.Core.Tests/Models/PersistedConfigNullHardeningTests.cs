using System.Text.Json;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Tests.Models;

/// <summary>
/// A JSON <c>null</c> for a collection property would otherwise be assigned
/// directly by System.Text.Json, and later iteration would throw a
/// NullReferenceException (e.g. in the MainViewModel constructor).
/// </summary>
public class PersistedConfigNullHardeningTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void TypesConfig_NullMods_BecomesEmptyList()
    {
        TypesConfig? config = JsonSerializer.Deserialize<TypesConfig>("{\"mods\":null}", Options);

        Assert.NotNull(config);
        Assert.NotNull(config!.Mods);
        config.Mods.Add(new ModTypesEntry { ModName = "@CF" });
        Assert.Single(config.Mods);
    }

    [Fact]
    public void ModTypesEntry_NullLists_BecomeEmptyLists()
    {
        ModTypesEntry? entry = JsonSerializer.Deserialize<ModTypesEntry>(
            "{\"sourceFiles\":null,\"generatedFiles\":null}", Options);

        Assert.NotNull(entry!.SourceFiles);
        Assert.NotNull(entry.GeneratedFiles);
    }

    [Fact]
    public void ModTypesEntry_NullFileRoles_BecomesEmptyCaseInsensitiveDictionary()
    {
        ModTypesEntry? entry = JsonSerializer.Deserialize<ModTypesEntry>("{\"fileRoles\":null}", Options);

        Assert.NotNull(entry!.FileRoles);
        entry.FileRoles["CF_types.xml"] = "types";
        Assert.True(entry.FileRoles.ContainsKey("cf_TYPES.xml"));
    }
}
