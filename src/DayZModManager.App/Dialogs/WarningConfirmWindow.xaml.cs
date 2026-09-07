using System.Windows;

namespace DayZModManager.App.Dialogs;

/// <summary>
/// Confirmation window that shows a normal message plus a prominent red warning
/// banner, used when proceeding may be unsafe (e.g. loading a save created under
/// a different mod/type configuration).
/// </summary>
public partial class WarningConfirmWindow : Window
{
    public WarningConfirmWindow(string message, string title, string warning, string note = "")
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        WarningText.Text = warning;
        NoteText.Text = note;
        if (string.IsNullOrWhiteSpace(note))
        {
            NoteText.Visibility = Visibility.Collapsed;
        }
    }

    private void Yes_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void No_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
