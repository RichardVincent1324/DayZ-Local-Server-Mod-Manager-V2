using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DayZModManager.App.ViewModels;

namespace DayZModManager.App.Behaviors;

/// <summary>
/// Enables drag-and-drop reordering of a ListBox bound to a collection of
/// <see cref="ModItemViewModel"/> items. On drop, the target index is computed
/// and the attached <see cref="ReorderCommand"/> is invoked with a
/// <see cref="ReorderRequest"/>.
/// </summary>
/// <remarks>
/// Implemented with mouse capture rather than OLE <c>DragDrop.DoDragDrop</c> so
/// mouse events keep flowing during the drag: the list can still be scrolled
/// with the mouse wheel while dragging, and a small drag ghost follows the
/// cursor to indicate the item being dragged.
/// </remarks>
public static class ListBoxDragDropReorder
{
    private static readonly ConditionalWeakTable<ListBox, DragState> States = new();

    public static readonly DependencyProperty ReorderCommandProperty =
        DependencyProperty.RegisterAttached(
            "ReorderCommand",
            typeof(ICommand),
            typeof(ListBoxDragDropReorder),
            new PropertyMetadata(null, OnReorderCommandChanged));

    public static void SetReorderCommand(DependencyObject element, ICommand value) =>
        element.SetValue(ReorderCommandProperty, value);

    public static ICommand GetReorderCommand(DependencyObject element) =>
        (ICommand)element.GetValue(ReorderCommandProperty);

    private static void OnReorderCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove -= OnPreviewMouseMove;
        listBox.PreviewMouseLeftButtonUp -= OnPreviewMouseLeftButtonUp;
        listBox.LostMouseCapture -= OnLostMouseCapture;

        listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove += OnPreviewMouseMove;
        listBox.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        listBox.LostMouseCapture += OnLostMouseCapture;
    }

    private static DragState GetState(ListBox listBox) => States.GetValue(listBox, _ => new DragState());

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        DragState state = GetState(listBox);
        state.Reset();

        ListBoxItem? item = FindContainer(listBox, e.OriginalSource as DependencyObject);
        if (item?.DataContext is ModItemViewModel mod)
        {
            state.DraggedItem = mod;
            state.StartPoint = e.GetPosition(listBox);
        }
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        DragState state = GetState(listBox);

        try
        {
            if (state.DraggedItem is null)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag(listBox, state);
                return;
            }

            if (!state.IsDragging)
            {
                Point current = e.GetPosition(listBox);
                if (Math.Abs(current.X - state.StartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(current.Y - state.StartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                {
                    return;
                }

                BeginDrag(listBox, state);
            }

            UpdateDrag(listBox, state);
        }
        catch
        {
            EndDrag(listBox, state);
            throw;
        }
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        DragState state = GetState(listBox);

        try
        {
            if (!state.IsDragging || state.DraggedItem is not ModItemViewModel dragged)
            {
                return;
            }

            Point position = e.GetPosition(listBox);
            int insertIndex = ComputeInsertIndex(listBox, position);

            EndDrag(listBox, state);

            if (insertIndex >= 0)
            {
                // Compensate for the dragged item being removed before the target position.
                int itemIndex = listBox.Items.IndexOf(dragged);
                int finalIndex = itemIndex < insertIndex ? insertIndex - 1 : insertIndex;

                ICommand command = GetReorderCommand(listBox);
                if (command is not null && itemIndex >= 0 && finalIndex >= 0)
                {
                    command.Execute(new ReorderRequest(dragged.Name, finalIndex));
                }
            }

            e.Handled = true;
        }
        catch
        {
            EndDrag(listBox, state);
            throw;
        }
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        DragState state = GetState(listBox);
        if (state.IsDragging)
        {
            EndDrag(listBox, state);
        }
    }

    private static void BeginDrag(ListBox listBox, DragState state)
    {
        state.IsDragging = true;
        listBox.CaptureMouse();

        state.Ghost = new DragGhost(state.DraggedItem?.Name ?? "Mod");
        state.Ghost.Position(ScreenPoint(listBox));
        state.Ghost.Show();
    }

    private static void UpdateDrag(ListBox listBox, DragState state)
    {
        state.Ghost?.Position(ScreenPoint(listBox));
    }

    private static void EndDrag(ListBox listBox, DragState state)
    {
        state.Ghost?.Close();
        state.Reset();
        ReleaseCapture(listBox);
    }

    private static int ComputeInsertIndex(ListBox listBox, Point position)
    {
        if (IsOverScrollBar(listBox, position))
        {
            return -1;
        }

        ListBoxItem? target = FindContainer(listBox, listBox.InputHitTest(position) as DependencyObject);
        if (target?.DataContext is ModItemViewModel targetMod)
        {
            int targetIndex = listBox.Items.IndexOf(targetMod);
            Point itemTop = target.TranslatePoint(new Point(0, 0), listBox);
            bool insertBefore = position.Y - itemTop.Y < target.ActualHeight / 2.0;
            return insertBefore ? targetIndex : targetIndex + 1;
        }

        if (position.Y < 0 || position.Y > listBox.ActualHeight ||
            position.X < 0 || position.X > listBox.ActualWidth)
        {
            return -1;
        }

        return position.Y < listBox.ActualHeight / 2.0 ? 0 : listBox.Items.Count;
    }

    private static Point ScreenPoint(UIElement element)
    {
        Point device = element.PointToScreen(Mouse.GetPosition(element));
        return PresentationSource.FromVisual(element) is { CompositionTarget: { } target }
            ? target.TransformFromDevice.Transform(device)
            : device;
    }

    private static void ReleaseCapture(ListBox listBox)
    {
        if (listBox.IsMouseCaptured)
        {
            listBox.ReleaseMouseCapture();
        }
    }

    private static ListBoxItem? FindContainer(ListBox listBox, DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, listBox))
        {
            if (source is ListBoxItem item)
            {
                return item;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool IsOverScrollBar(ListBox listBox, Point position)
    {
        if (listBox.InputHitTest(position) is not DependencyObject source)
        {
            return false;
        }

        while (source is not null)
        {
            if (source is ScrollBar)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private sealed class DragState
    {
        public ModItemViewModel? DraggedItem;
        public Point StartPoint;
        public bool IsDragging;
        public DragGhost? Ghost;

        public void Reset()
        {
            DraggedItem = null;
            IsDragging = false;
            Ghost = null;
        }
    }

    private sealed class DragGhost : Window
    {
        public DragGhost(string text)
        {
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x2D, 0x4A, 0x6B)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x9C, 0xD8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 12,
                },
            };
        }

        public void Position(Point screen)
        {
            Left = screen.X + 4;
            Top = screen.Y + 6;
        }
    }
}
