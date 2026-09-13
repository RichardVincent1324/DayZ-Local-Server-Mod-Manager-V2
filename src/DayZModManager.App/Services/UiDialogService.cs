using System.IO;
using System.Windows;
using DayZModManager.App.Dialogs;
using DayZModManager.App.ViewModels;
using Microsoft.Win32;

namespace DayZModManager.App.Services;

/// <summary>Default <see cref="IDialogService"/> backed by WPF dialogs.</summary>
public sealed class UiDialogService : IDialogService
{
    public void ShowMessage(string message, string title, bool isError = false) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool ConfirmWithWarning(string message, string title, string warning, string note = "")
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return Confirm(message, title);
        }

        var window = new WarningConfirmWindow(message, title, warning, note);
        return window.ShowDialog() == true;
    }

    public LoadSaveConfirmation ConfirmLoadSave(
        string message, string title, string warning, string note, bool offerTypesRestore)
    {
        var window = new WarningConfirmWindow(message, title, warning, note, offerTypesRestore);
        return new LoadSaveConfirmation(window.ShowDialog() == true, window.RestoreTypes);
    }

    public string? AskText(string title, string prompt, string defaultValue = "")
    {
        var window = new TextPromptWindow(title, prompt, defaultValue);
        return window.ShowDialog() == true ? window.Result : null;
    }

    public string? PickFolder(string title = "Select a folder")
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public string? PickFile(string title, string filter, string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            InitialDirectory = initialDirectory,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string>? PickTypeFiles(string modName, IReadOnlyList<string> files, IReadOnlySet<string>? activeFiles = null)
    {
        if (files.Count == 0)
        {
            ShowMessage($"No XML files with 'type' keyword found in mod {modName}.", "Info");
            return null;
        }

        string basePath = GetCommonBasePath(files);
        var viewModel = new TypeFilePickerViewModel(modName, files, basePath, activeFiles);
        var window = new TypeFilePickerWindow(viewModel);
        return window.ShowDialog() == true ? window.SelectedFiles : null;
    }

    private static string GetCommonBasePath(IReadOnlyList<string> files)
    {
        string common = Path.GetDirectoryName(files[0]) ?? string.Empty;

        foreach (string file in files.Skip(1))
        {
            string directory = Path.GetDirectoryName(file) ?? string.Empty;
            while (common.Length > 0 && !directory.StartsWith(common, StringComparison.OrdinalIgnoreCase))
            {
                common = Path.GetDirectoryName(common) ?? string.Empty;
            }
        }

        return common;
    }
}
