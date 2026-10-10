using System.Windows;
using DayZModManager.App.Services;

namespace DayZModManager.App.Dialogs;

/// <summary>Name and copy-profiles choice gathered by the preset prompt window.</summary>
public sealed record PresetNameInput(string Name, bool CopyProfiles);

public partial class AddPresetWindow : Window
{
    public AddPresetWindow(string mapName)
        : this(
            "Add Preset",
            $"Preset name for {mapName}:",
            "Copy profiles data from __default_preset__",
            checkboxDefault: false,
            "Create")
    {
    }

    public AddPresetWindow(
        string windowTitle, string prompt, string checkboxContent, bool checkboxDefault, string confirmContent)
    {
        InitializeComponent();
        Title = windowTitle;
        PromptText.Text = prompt;
        CopyProfilesBox.Content = checkboxContent;
        CopyProfilesBox.IsChecked = checkboxDefault;
        ConfirmButton.Content = confirmContent;
        NameBox.Focus();
    }

    public PresetNameInput? Result { get; private set; }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a preset name.", "Preset name", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new PresetNameInput(name, CopyProfilesBox.IsChecked == true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
