using System.Text.Json;
using DayZModManager.Core.Abstractions;
using DayZModManager.Core.Models;

namespace DayZModManager.Core.Services;

/// <summary>
/// Shared JSON serialization/deserialization for configuration files.
/// Uses camelCase, case-insensitive matching, and no byte-order mark on write.
/// </summary>
internal static class ConfigJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static ConfigLoadResult<T> Read<T>(IFileSystem fileSystem, string filePath)
        where T : class
    {
        if (!fileSystem.FileExists(filePath))
        {
            return ConfigLoadResult<T>.Missing();
        }

        try
        {
            string json = fileSystem.ReadAllText(filePath);
            T? value = JsonSerializer.Deserialize<T>(json, Options);
            return value is null
                ? ConfigLoadResult<T>.Corrupt()
                : ConfigLoadResult<T>.Success(value);
        }
        catch (JsonException)
        {
            return ConfigLoadResult<T>.Corrupt();
        }
    }

    public static void Write<T>(IFileSystem fileSystem, string filePath, T value)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            fileSystem.CreateDirectory(directory);
        }

        fileSystem.WriteAllText(filePath, JsonSerializer.Serialize(value, Options));
    }
}
