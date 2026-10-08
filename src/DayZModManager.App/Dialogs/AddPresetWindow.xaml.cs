using System.Windows;
using DayZModManager.App.Services;

namespace DayZModManager.App.Dialogs;

public partial class AddPresetWindow : Window
{
    public AddPresetWindow(string mapName)
    {
        InitializeComponent();
        PromptText.Text = $"Preset name for {mapName}:";
        NameBox.Focus();
    }

    public AddPresetRequest? Result { get; private set; }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a preset name.", "Add Preset", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new AddPresetRequest(name, CopyProfilesBox.IsChecked == true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
