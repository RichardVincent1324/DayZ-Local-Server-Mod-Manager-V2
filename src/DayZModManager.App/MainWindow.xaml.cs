using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        // Keep the window title in sync with the assembly version so a release
        // bump only needs to touch DayZModManager.App.csproj.
        string version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        Title = $"DayZ Mod Manager v{version}";

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
    {        if (_activeModList is not null)
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

    private void PresetTypesRoot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        // Keep the grids' selection/focus when the click is inside either grid.
        if (IsDescendantOf(source, TypesDataGrid) || IsDescendantOf(source, SavesDataGrid))
        {
            return;
        }

        // Let buttons, combo boxes, and other focusable controls handle their own
        // focus. Only an empty-area click releases the grids.
        if (IsWithinFocusableControl(source, PresetTypesRoot))
        {
            return;
        }

        // Move focus first so an in-progress cell edit commits before selection
        // is cleared.
        PresetTypesRoot.Focus();
        TypesDataGrid.UnselectAll();
        SavesDataGrid.UnselectAll();
    }

    private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
    {
        for (DependencyObject? current = source; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinFocusableControl(DependencyObject source, DependencyObject boundary)
    {
        for (DependencyObject? current = source; current is not null; current = GetParent(current))
        {
            if (current is Control { Focusable: true })
            {
                return true;
            }

            if (ReferenceEquals(current, boundary))
            {
                break;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child) =>
        child is FrameworkContentElement contentElement
            ? contentElement.Parent
            : VisualTreeHelper.GetParent(child);

    private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        // Leaving the Mod Manage tab persists any pending mod changes so the
        // Preset & Types and Settings pages never operate on unsaved mods.
        if (e.RemovedItems.Count > 0 && ReferenceEquals(e.RemovedItems[0], ModsTab))
        {
            await viewModel.ApplyIfDirtyAsync();
        }

        if (e.AddedItems.Count > 0 && ReferenceEquals(e.AddedItems[0], PresetTypesTab))
        {
            viewModel.OnPresetTypesTabActivated();
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