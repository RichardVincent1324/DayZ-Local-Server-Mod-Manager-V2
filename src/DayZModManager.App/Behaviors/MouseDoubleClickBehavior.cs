using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DayZModManager.App.Behaviors;

/// <summary>
/// Executes a command when a control is double-clicked. Unlike a
/// <c>MouseBinding</c> with <c>LeftDoubleClick</c>, this listens to the
/// <c>MouseDoubleClick</c> routed event, which fires reliably even on controls
/// (such as ListBox items) that handle the first mouse click.
/// </summary>
public static class MouseDoubleClickBehavior
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(MouseDoubleClickBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static void SetCommand(DependencyObject element, ICommand value) =>
        element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(CommandProperty);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Control control)
        {
            control.MouseDoubleClick -= OnMouseDoubleClick;
            control.MouseDoubleClick += OnMouseDoubleClick;
        }
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        ICommand? command = GetCommand(control);
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }
}
