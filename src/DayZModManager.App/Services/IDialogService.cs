namespace DayZModManager.App.Services;

/// <summary>
/// Outcome of the Load Save confirmation. <see cref="RestoreTypes"/> is true when
/// the user opted in to restoring the save's types files.
/// </summary>
public readonly record struct LoadSaveConfirmation(bool Confirmed, bool RestoreTypes);

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
    /// Load Save confirmation that also offers an "also restore the saved types
    /// files" checkbox when <paramref name="offerTypesRestore"/> is true. The
    /// warning (when non-empty) is rendered prominently as in
    /// <see cref="ConfirmWithWarning"/>.
    /// </summary>
    LoadSaveConfirmation ConfirmLoadSave(
        string message, string title, string warning, string note, bool offerTypesRestore);

    /// <summary>Opens a folder picker. Returns the chosen path, or null if cancelled.</summary>
    string? PickFolder(string title = "Select a folder");

    /// <summary>Opens a file picker. Returns the chosen file path, or null if cancelled.</summary>
    string? PickFile(string title, string filter, string initialDirectory);

    /// <summary>
    /// Opens the types-file picker for a mod. <paramref name="activeFiles"/> is the
    /// subset of <paramref name="files"/> that are currently configured for the
    /// mod; those are pre-selected. Returns the selected full paths, or null if
    /// the user cancelled.
    /// </summary>
    IReadOnlyList<string>? PickTypeFiles(string modName, IReadOnlyList<string> files, IReadOnlySet<string>? activeFiles = null);

    /// <summary>
    /// Prompts for a single line of text. Returns the entered text, or null when
    /// the user cancelled.
    /// </summary>
    string? AskText(string title, string prompt, string defaultValue = "");
}
