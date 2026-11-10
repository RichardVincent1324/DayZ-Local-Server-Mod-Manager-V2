using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DayZModManager.App.Behaviors;

/// <summary>
/// Prevents the dropdown of a <see cref="ComboBox"/> using pixel scrolling
/// (<c>ScrollViewer.CanContentScroll="False"</c>) from micro-scrolling when the
/// mouse hovers an item that is only partially visible.
/// </summary>
/// <remarks>
/// <para>
/// Focus changes raise <see cref="FrameworkElement.RequestBringIntoViewEvent"/>,
/// which the dropdown's <see cref="ScrollViewer"/> turns into a scroll. Mouse
/// hover and click can trigger such a request even when the item is almost fully
/// visible, producing a small, unwanted pixel scroll. This guard suppresses
/// every request that is <em>not</em> caused by keyboard navigation.
/// </para>
/// <para>
/// Keyboard navigation is detected by checking the navigation keys at the moment
/// the event is raised. The request is raised synchronously from the key handler,
/// so the key state is still current when this method runs. Any other request
/// (mouse hover, focus change, opening the dropdown to scroll the selected item
/// into view) is cancelled by setting <see cref="RoutedEventArgs.Handled"/>.
/// </para>
/// <para>
/// Known trade-off: because opening the dropdown to reveal the selected item is
/// not keyboard-driven, that request is also suppressed. If the selected item
/// sits outside the initial viewport, the dropdown will not auto-scroll to it.
/// Mouse wheel and scrollbar dragging never raise this event and are unaffected.
/// </para>
/// </remarks>
public static class ComboBoxItemBringIntoViewGuard
{
    public static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not ComboBoxItem)
        {
            return;
        }

        // Let keyboard navigation scroll the dropdown normally.
        if (IsKeyboardNavigationActive())
        {
            return;
        }

        // Suppress everything else: mouse hover, focus changes, and the
        // initial "scroll selected item into view" request on open.
        e.Handled = true;
    }

    private static bool IsKeyboardNavigationActive()
    {
        return Keyboard.IsKeyDown(Key.Up)
            || Keyboard.IsKeyDown(Key.Down)
            || Keyboard.IsKeyDown(Key.PageUp)
            || Keyboard.IsKeyDown(Key.PageDown)
            || Keyboard.IsKeyDown(Key.Home)
            || Keyboard.IsKeyDown(Key.End);
    }
}