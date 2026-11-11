using System.Windows;
using DayZModManager.App.ViewModels;
using DayZModManager.Core.Services;

namespace DayZModManager.App.Dialogs;

public partial class TypeFilePickerWindow : Window
{
    private readonly TypeFilePickerViewModel _viewModel;
    private readonly Action? _openModFolder;

    public TypeFilePickerWindow(TypeFilePickerViewModel viewModel, Action? openModFolder = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _openModFolder = openModFolder;
        DataContext = viewModel;
    }

    public IReadOnlyList<string>? Selections { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Selections = _viewModel.GetSelectedFiles();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OpenModFolder_Click(object sender, RoutedEventArgs e) => _openModFolder?.Invoke();
}
