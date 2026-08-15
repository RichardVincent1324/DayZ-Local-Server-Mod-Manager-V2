namespace DayZModManager.App.Services;

/// <summary>Abstraction over modal UI interactions (message boxes, pickers).</summary>
public interface IDialogService
{
    void ShowMessage(string message, string title, bool isError = false);

    bool Confirm(string message, string title);

    /// <summary>Opens a folder picker. Returns the chosen path, or null if cancelled.</summary>
    string? PickFolder(string title = "Select a folder");

    /// <summary>Opens a file picker. Returns the chosen file path, or null if cancelled.</summary>
    string? PickFile(string title, string filter, string initialDirectory);

    /// <summary>
    /// Opens the types-file picker for a mod. Returns the selected full paths,
    /// or null if the user cancelled.
    /// </summary>
    IReadOnlyList<string>? PickTypeFiles(string modName, IReadOnlyList<string> files);
}
