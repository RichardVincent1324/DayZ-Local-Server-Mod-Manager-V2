using System.Windows;
using System.Windows.Controls;
using DayZModManager.App.ViewModels;

namespace DayZModManager.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private ListBox? _activeModList;

    public MainWindow()
    {
        InitializeComponent();

        LoadedListBox.GotKeyboardFocus += (_, _) => _activeModList = LoadedListBox;
        AvailableListBox.GotKeyboardFocus += (_, _) => _activeModList = AvailableListBox;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_activeModList is not null)
        {
            _activeModList.SelectAll();
        }
        else if (LoadedListBox.SelectedItems.Count > 0)
        {
            LoadedListBox.SelectAll();
        }
        else if (AvailableListBox.SelectedItems.Count > 0)
        {
            AvailableListBox.SelectAll();
        }
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && ReferenceEquals(e.AddedItems[0], MapTypesTab) &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.OnMapTypesTabActivated();
        }
    }
}