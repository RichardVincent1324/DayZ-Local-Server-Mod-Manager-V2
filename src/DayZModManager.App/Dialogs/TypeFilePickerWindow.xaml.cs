using System.Windows;
using DayZModManager.App.ViewModels;

namespace DayZModManager.App.Dialogs;

public partial class TypeFilePickerWindow : Window
{
    private readonly TypeFilePickerViewModel _viewModel;

    public TypeFilePickerWindow(TypeFilePickerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public IReadOnlyList<string>? SelectedFiles { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedFiles = _viewModel.GetSelectedFiles();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
