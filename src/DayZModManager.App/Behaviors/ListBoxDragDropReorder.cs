using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    public static readonly DependencyProperty IsDragActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsDragActive",
            typeof(bool),
            typeof(ListBoxDragDropReorder),
            new PropertyMetadata(false));

    public static void SetIsDragActive(DependencyObject element, bool value) =>
        element.SetValue(IsDragActiveProperty, value);

    public static bool GetIsDragActive(DependencyObject element) =>
        (bool)element.GetValue(IsDragActiveProperty);

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
        listBox.PreviewMouseWheel -= OnPreviewMouseWheel;

        listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove += OnPreviewMouseMove;
        listBox.PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
        listBox.LostMouseCapture += OnLostMouseCapture;
        listBox.PreviewMouseWheel += OnPreviewMouseWheel;
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

            // Snapshot the selection once the click has been processed (bubbling
            // mouse-down runs MakeSingleSelection/MakeToggleSelection) but before any
            // mouse-move can trigger WPF's cursor-follow selection. This is the
            // "drag-start selection" that must be preserved during and after the drag.
            listBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (state.DraggedItem is not null)
                {
                    state.DragStartSelection = listBox.SelectedItems.Cast<ModItemViewModel>().ToList();
                }
            }));
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

            // Capture before EndDrag clears the drag state.
            List<ModItemViewModel>? dragStartSelection = state.DragStartSelection;

            EndDrag(listBox, state);

            if (insertIndex >= 0)
            {
                // Compensate for the dragged item being removed before the target position.
                int itemIndex = listBox.Items.IndexOf(dragged);
                int finalIndex = itemIndex < insertIndex ? insertIndex - 1 : insertIndex;

                ICommand command = GetReorderCommand(listBox);
                if (command is not null && itemIndex >= 0 && finalIndex >= 0)
                {
                    // The reorder mutates the bound collection, which can confuse the
                    // virtualizing ListBox's selection bookkeeping (stale container
                    // references move the highlight to other items or drop it). Restore
                    // the drag-start selection by reference once layout has settled so
                    // the dragged item stays selected.
                    command.Execute(new ReorderRequest(dragged.Name, finalIndex));
                    if (dragStartSelection is { Count: > 0 })
                    {
                        listBox.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                            RestoreSelection(listBox, dragStartSelection)));
                    }
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
        if (state.IsDragging && !state.SuppressEndDrag)
        {
            EndDrag(listBox, state);
        }
    }

    /// <summary>
    /// Element mouse capture (taken during a drag so rows can't be highlighted)
    /// routes the wheel to the ListBox instead of the template's ScrollViewer,
    /// which lives inside the ListBox and is never on the event route. Scroll the
    /// ScrollViewer manually while dragging, mirroring native wheel semantics.
    /// </summary>
    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (!GetState(listBox).IsDragging)
        {
            return;
        }

        if (FindDescendant<ScrollViewer>(listBox) is not { } scrollViewer)
        {
            return;
        }

        int lines = SystemParameters.WheelScrollLines;
        if (lines == -1)
        {
            if (e.Delta > 0)
            {
                scrollViewer.PageUp();
            }
            else
            {
                scrollViewer.PageDown();
            }
        }
        else if (lines > 0)
        {
            if (e.Delta > 0)
            {
                for (int i = 0; i < lines; i++)
                {
                    scrollViewer.LineUp();
                }
            }
            else
            {
                for (int i = 0; i < lines; i++)
                {
                    scrollViewer.LineDown();
                }
            }
        }

        e.Handled = true;
    }

    private static void BeginDrag(ListBox listBox, DragState state)
    {
        state.IsDragging = true;
        state.DragStartSelection ??= listBox.SelectedItems.Cast<ModItemViewModel>().ToList();

        listBox.SetValue(IsDragActiveProperty, true);

        // Pin the drag-start selection (corrects any cursor-follow churn that
        // happened in the brief pre-drag window).
        RestoreSelection(listBox, state.DragStartSelection);

        // WPF cannot change the capture mode on an element that already holds
        // capture (MouseDevice only updates the mode when the element changes),
        // and ListBox captured with SubTree mode on mouse-down. SubTree capture
        // keeps items under the cursor receiving MouseEnter, whose OnMouseEnter
        // handler moves selection to every row the cursor passes. Release and
        // re-capture with Element mode so items no longer receive mouse events.
        // IsDragging is already set, so the transient LostMouseCapture cannot
        // recurse into BeginDrag; SuppressEndDrag stops it from cancelling us.
        state.SuppressEndDrag = true;
        try
        {
            listBox.ReleaseMouseCapture();
            listBox.CaptureMouse();
        }
        finally
        {
            state.SuppressEndDrag = false;
        }

        state.Ghost = new DragGhost(state.DraggedItem?.Name ?? "Mod");
        state.Ghost.Position(ScreenPoint(listBox));
        state.Ghost.Show();

        // Showing a window can drop mouse capture; re-assert Element capture so
        // items cannot receive mouse events (and change selection) during the drag.
        if (!listBox.IsMouseCaptured)
        {
            listBox.CaptureMouse();
        }

        if (AdornerLayer.GetAdornerLayer(listBox) is { } layer)
        {
            state.IndicatorLayer = layer;
            state.Indicator = new InsertionIndicatorAdorner(listBox);
            layer.Add(state.Indicator);
        }
    }

    private static void UpdateDrag(ListBox listBox, DragState state)
    {
        state.Ghost?.Position(ScreenPoint(listBox));
        ReassertDragSelection(listBox, state);
        UpdateInsertionIndicator(listBox, state);
    }

    private static void ReassertDragSelection(ListBox listBox, DragState state)
    {
        if (state.DragStartSelection is not { Count: > 0 } pinned)
        {
            return;
        }

        if (listBox.SelectedItems.Count == pinned.Count &&
            listBox.SelectedItems.Cast<ModItemViewModel>().All(vm => pinned.Contains(vm)))
        {
            return;
        }

        RestoreSelection(listBox, pinned);
    }

    /// <summary>
    /// Clears the current selection and selects exactly <paramref name="selection"/>
    /// by item reference. Realized containers get IsSelected set directly (which
    /// keeps the Selector's bookkeeping in sync); virtualized items fall back to
    /// SelectedItems.Add and get highlighted when their container is realized.
    /// </summary>
    private static void RestoreSelection(ListBox listBox, IReadOnlyList<ModItemViewModel> selection)
    {
        listBox.SelectedItems.Clear();

        foreach (ModItemViewModel vm in selection)
        {
            if (listBox.ItemContainerGenerator.ContainerFromItem(vm) is ListBoxItem container)
            {
                container.IsSelected = true;
            }
            else
            {
                listBox.SelectedItems.Add(vm);
            }
        }
    }

    private static void EndDrag(ListBox listBox, DragState state)
    {
        state.Ghost?.Close();

        if (state.IndicatorLayer is { } layer && state.Indicator is { } indicator)
        {
            layer.Remove(indicator);
        }

        listBox.SetValue(IsDragActiveProperty, false);

        state.Reset();
        ReleaseCapture(listBox);
    }

    private static void UpdateInsertionIndicator(ListBox listBox, DragState state)
    {
        if (state.Indicator is not { } indicator)
        {
            return;
        }

        int index = ComputeInsertIndex(listBox, Mouse.GetPosition(listBox));
        double y = -1;

        if (index >= 0)
        {
            y = index < listBox.Items.Count
                ? GetContainerTopY(listBox, index)
                : GetListBottomY(listBox);

            if (y < 0) y = 0;
            if (y > listBox.ActualHeight) y = listBox.ActualHeight;
        }

        indicator.Y = y;
        indicator.InvalidateVisual();
    }

    private static double GetContainerTopY(ListBox listBox, int index)
    {
        if (listBox.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
        {
            return container.TranslatePoint(new Point(0, 0), listBox).Y;
        }

        // The target container may be virtualized; fall back to a realized neighbor.
        for (int i = index + 1; i < listBox.Items.Count; i++)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement next)
            {
                return next.TranslatePoint(new Point(0, 0), listBox).Y;
            }
        }

        for (int i = index - 1; i >= 0; i--)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement prev)
            {
                return prev.TranslatePoint(new Point(0, 0), listBox).Y + prev.ActualHeight;
            }
        }

        return 0;
    }

    private static double GetListBottomY(ListBox listBox)
    {
        for (int i = listBox.Items.Count - 1; i >= 0; i--)
        {
            if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement last)
            {
                return last.TranslatePoint(new Point(0, 0), listBox).Y + last.ActualHeight;
            }
        }

        return listBox.ActualHeight;
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

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } found)
            {
                return found;
            }
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
        public bool SuppressEndDrag;
        public List<ModItemViewModel>? DragStartSelection;
        public DragGhost? Ghost;
        public AdornerLayer? IndicatorLayer;
        public InsertionIndicatorAdorner? Indicator;

        public void Reset()
        {
            DraggedItem = null;
            IsDragging = false;
            SuppressEndDrag = false;
            DragStartSelection = null;
            Ghost = null;
            IndicatorLayer = null;
            Indicator = null;
        }
    }

    private sealed class InsertionIndicatorAdorner : Adorner
    {
        private static readonly Pen LinePen = CreateLinePen();

        public double Y = -1;

        public InsertionIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        private static Pen CreateLinePen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2D, 0x9C, 0xD8)), 2.0);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Y < 0)
            {
                return;
            }

            drawingContext.DrawLine(LinePen, new Point(0, Y), new Point(AdornedElement.RenderSize.Width, Y));
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
