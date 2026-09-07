using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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

    private void AutoCleanServerLogsCheckBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Reflect the persisted value once the element is realized (the Settings
        // tab content is only created on first selection). Double-writes are
        // harmless, and this no longer relies on the IsChecked binding.
        if (DataContext is MainViewModel viewModel
            && AutoCleanServerLogsCheckBox.IsChecked != viewModel.Settings.AutoCleanServerLogs)
        {
            AutoCleanServerLogsCheckBox.IsChecked = viewModel.Settings.AutoCleanServerLogs;
        }
    }

    private void AutoCleanServerLogs_Click(object sender, RoutedEventArgs e)
    {
        // Belt-and-braces: the two-way binding normally writes the value, but if
        // it ever fails the click still reaches the view model so Apply persists
        // the ticked state. Setting the same value again is a no-op.
        if (sender is CheckBox { DataContext: SettingsViewModel settings } box)
        {
            settings.AutoCleanServerLogs = box.IsChecked == true;
        }
    }

    private void LogListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            LogListBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (Keyboard.FocusedElement is TextBox focused && focused.SelectionLength > 0)
            {
                return;
            }

            var selected = new HashSet<object>(LogListBox.SelectedItems.Cast<object>());
            string text = string.Join(
                Environment.NewLine,
                LogListBox.Items
                    .Cast<object>()
                    .Where(selected.Contains)
                    .OfType<LogEntry>()
                    .Select(entry => entry.Message));

            if (text.Length > 0)
            {
                Clipboard.SetText(text);
            }

            e.Handled = true;
        }
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

    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // Leaving the Mod Manage tab persists any pending mod changes so the
        // Map & Types and Settings pages never operate on unsaved mods.
        if (e.RemovedItems.Count > 0 && ReferenceEquals(e.RemovedItems[0], ModsTab))
        {
            await viewModel.ApplyIfDirtyAsync();
        }

        if (e.AddedItems.Count > 0 && ReferenceEquals(e.AddedItems[0], MapTypesTab))
        {
            viewModel.OnMapTypesTabActivated();
        }

        if (e.AddedItems.Count > 0 && ReferenceEquals(e.AddedItems[0], ModsTab))
        {
            // TabControl moves keyboard focus into the tab content when a tab is
            // activated, landing on the search box. Move focus back to the tab
            // header once that has settled.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => ModsTab.Focus()));
        }
    }
}