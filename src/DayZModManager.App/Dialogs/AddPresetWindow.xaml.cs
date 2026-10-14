using System.Windows;
using System.Linq;
using DayZModManager.App.Services;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Dialogs;

/// <summary>Name gathered by the preset prompt window.</summary>
public sealed record PresetNameInput(string Name);

public partial class AddPresetWindow : Window
{
    public AddPresetWindow(string mapName)
        : this(
            "Add Preset",
            $"Preset name for {mapName}:",
            "Create")
    {
    }

    public AddPresetWindow(
        string windowTitle, string prompt, string confirmContent,
        IReadOnlyList<string>? maps = null, string? defaultMap = null)
    {
        InitializeComponent();
        Title = windowTitle;
        PromptText.Text = prompt;
        ConfirmButton.Content = confirmContent;

        // The destination-map row is only shown when a map list is supplied (the
        // duplicate flow); the add flow reuses the same window without it.
        if (maps is not null && maps.Count > 0)
        {
            DestinationMapPanel.Visibility = Visibility.Visible;
            DestinationMapBox.ItemsSource = maps;
            DestinationMapBox.SelectedItem = defaultMap is not null && maps.Contains(defaultMap)
                ? defaultMap
                : maps[0];
        }

        NameBox.Focus();
    }

    public PresetNameInput? Result { get; private set; }

    /// <summary>The selected destination map, or null when the row is hidden or nothing is selected.</summary>
    public string? SelectedDestinationMap => DestinationMapBox.SelectedItem as string;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a preset name.", "Preset name", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!PresetService.IsValidPresetName(name))
        {
            MessageBox.Show(PresetService.PresetNameRequirement, "Preset name", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DestinationMapPanel.Visibility == Visibility.Visible && DestinationMapBox.SelectedItem is not string)
        {
            MessageBox.Show("Select a destination map.", "Destination map", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new PresetNameInput(name);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
