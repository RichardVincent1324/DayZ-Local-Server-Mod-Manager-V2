using System.Windows;

namespace DayZModManager.App.Dialogs;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string defaultValue)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.Focus();
    }

    public string? Result { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
