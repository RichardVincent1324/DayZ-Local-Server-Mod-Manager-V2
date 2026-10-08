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
    public void TypesConfig_NullMaps_BecomesEmptyCaseInsensitiveDictionary()
    {
        TypesConfig? config = JsonSerializer.Deserialize<TypesConfig>("{\"maps\":null}", Options);

        Assert.NotNull(config);
        Assert.NotNull(config!.Maps);
        config.Maps["SomeMap"] = new MapTypesConfig();
        Assert.True(config.Maps.ContainsKey("somemap"));
    }

    [Fact]
    public void MapTypesConfig_NullMods_BecomesEmptyList()
    {
        MapTypesConfig? map = JsonSerializer.Deserialize<MapTypesConfig>("{\"mods\":null}", Options);

        Assert.NotNull(map!.Mods);
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
