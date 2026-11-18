using DayZModManager.Core.Services;

namespace DayZModManager.App.Services;

/// <summary>Input gathered by the Add Preset dialog.</summary>
/// <param name="Name">The new preset's name.</param>
public sealed record AddPresetRequest(string Name);

/// <summary>Input gathered by the Duplicate Preset dialog.</summary>
/// <param name="Name">The new (duplicated) preset's name.</param>
/// <param name="DestinationMap">The map the copy is created under.</param>
public sealed record DuplicatePresetRequest(string Name, string DestinationMap);

/// <summary>Abstraction over modal UI interactions (message boxes, pickers).</summary>
public interface IDialogService
{
    void ShowMessage(string message, string title, bool isError = false);

    bool Confirm(string message, string title);

    /// <summary>
    /// Confirmation whose <paramref name="warning"/> (when non-empty) is rendered
    /// prominently in red above the Yes/No buttons so it cannot be missed. A plain
    /// <paramref name="note"/> (e.g. a path for investigation) is shown in normal
    /// text below the warning.
    /// </summary>
    bool ConfirmWithWarning(string message, string title, string warning, string note = "");

    /// <summary>
    /// Shows an informational window with the same prominent red warning banner
    /// as <see cref="ConfirmWithWarning"/>, but with a single OK button. Used to
    /// report a blocked operation.
    /// </summary>
    void ShowWarning(string message, string title, string warning, string note = "");

    /// <summary>
    /// Shows an informational window with a prominent red warning banner and plays
    /// the standard Windows warning tone. Used for one-time, serious notices that
    /// must not be missed.
    /// </summary>
    void ShowAlert(string message, string title, string warning, string note = "");

    /// <summary>Opens a folder picker. Returns the chosen path, or null if cancelled.</summary>
    string? PickFolder(string title = "Select a folder");

    /// <summary>Opens a file picker. Returns the chosen file path, or null if cancelled.</summary>
    string? PickFile(string title, string filter, string initialDirectory);

    /// <summary>
    /// Opens the type-file import picker for a mod. <paramref name="modFolderPath"/>
    /// is offered as a shortcut to inspect the mod's files. Returns the selected
    /// full paths, or null if the user cancelled.
    /// </summary>
    IReadOnlyList<string>? PickTypeFiles(
        string modName,
        IReadOnlyList<string> files,
        string? modFolderPath = null);

    /// <summary>
    /// Prompts for a single line of text. Returns the entered text, or null when
    /// the user cancelled.
    /// </summary>
    string? AskText(string title, string prompt, string defaultValue = "");

    /// <summary>
    /// Opens the Add Preset dialog for a map. Returns the requested name, or null
    /// when cancelled.
    /// </summary>
    AddPresetRequest? AskAddPreset(string mapName);

    /// <summary>
    /// Opens the Duplicate Preset dialog for an existing preset. <paramref name="maps"/>
    /// is the same map collection as the Current Map ComboBox; <paramref name="defaultMap"/>
    /// is preselected. Returns the requested name and the destination map, or null
    /// when cancelled.
    /// </summary>
    DuplicatePresetRequest? AskDuplicatePreset(string sourcePresetName, IReadOnlyList<string> maps, string defaultMap);
}
