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
            int insertIndex = ComputeInsertIndex(listBox, position, GetRealizedBounds(listBox));

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
    /// Makes the current selection match <paramref name="selection"/> by item
    /// reference using only the deltas: items no longer selected are removed and
    /// missing ones are added. Clearing and re-adding the whole selection flips
    /// every realized container's <c>IsSelected</c> and transiently mis-paints
    /// recycled containers (the grey highlight jump). The Selector's
    /// <c>SelectedItems</c> API is used directly so no container has to be
    /// generated.
    /// </summary>
    private static void RestoreSelection(ListBox listBox, IReadOnlyList<ModItemViewModel> selection)
    {
        var target = new HashSet<ModItemViewModel>(selection);

        for (int i = listBox.SelectedItems.Count - 1; i >= 0; i--)
        {
            if (listBox.SelectedItems[i] is ModItemViewModel vm && !target.Contains(vm))
            {
                listBox.SelectedItems.RemoveAt(i);
            }
        }

        var selected = new HashSet<ModItemViewModel>(listBox.SelectedItems.OfType<ModItemViewModel>());
        foreach (ModItemViewModel vm in selection)
        {
            if (selected.Add(vm))
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

        List<RealizedContainer> realized = GetRealizedBounds(listBox);
        int index = ComputeInsertIndex(listBox, Mouse.GetPosition(listBox), realized);
        indicator.InsertPositionKind = InsertKind.None;

        if (index < 0)
        {
            indicator.Y = -1;
            indicator.InvalidateVisual();
            return;
        }

        double y = GetInsertionY(listBox, index, realized);

        // When no position can be computed, hide the indicator instead of falling
        // back to 0.
        if (y < 0)
        {
            indicator.Y = -1;
            indicator.InvalidateVisual();
            return;
        }

        // Clamp the bottom indicator inside the viewport so the AdornerLayer does
        // not clip it.
        if (y > listBox.ActualHeight)
        {
            y = listBox.ActualHeight;
        }

        indicator.InsertPositionKind = index == 0
            ? InsertKind.TopOfList
            : index == listBox.Items.Count
                ? InsertKind.BottomOfList
                : InsertKind.BetweenItems;

        indicator.Y = y;
        indicator.InvalidateVisual();
    }

    /// <summary>
    /// The Y at which the insertion line for <paramref name="index"/> is drawn: the
    /// top of that realized row, the bottom of the last row when appending, or the
    /// nearest realized boundary when the exact row is virtualized.
    /// </summary>
    private static double GetInsertionY(ListBox listBox, int index, IReadOnlyList<RealizedContainer> realized)
    {
        if (realized.Count == 0)
        {
            return -1;
        }

        foreach (RealizedContainer container in realized)
        {
            if (container.Index == index)
            {
                return container.Top;
            }
        }

        if (index >= listBox.Items.Count)
        {
            return realized[^1].Bottom;
        }

        foreach (RealizedContainer container in realized)
        {
            if (container.Index > index)
            {
                return container.Top;
            }
        }

        return realized[^1].Bottom;
    }

    /// <summary>
    /// The realized rows in item order, read straight from the ItemsPanel's
    /// children. This avoids calling ItemContainerGenerator per index on every drag
    /// move (which can force realization and recycle other containers) and gives
    /// the insert calculation a single consistent snapshot.
    /// </summary>
    private static List<RealizedContainer> GetRealizedBounds(ListBox listBox)
    {
        var bounds = new List<RealizedContainer>();
        if (GetItemsHostPanel(listBox) is not { } panel || listBox.ItemContainerGenerator is not { } generator)
        {
            return bounds;
        }

        foreach (UIElement child in panel.Children)
        {
            if (child is not FrameworkElement element || !element.IsVisible)
            {
                continue;
            }

            int index = generator.IndexFromContainer(element);
            if (index < 0)
            {
                continue;
            }

            double top = element.TranslatePoint(new Point(0, 0), listBox).Y;
            bounds.Add(new RealizedContainer(index, top, top + element.ActualHeight));
        }

        bounds.Sort(static (a, b) => a.Index.CompareTo(b.Index));
        return bounds;
    }

    /// <summary>
    /// The panel that hosts the ListBox's item containers (the ItemsPresenter's
    /// child). Read without going through the (protected) ItemsPanelRoot.
    /// </summary>
    private static Panel? GetItemsHostPanel(ListBox listBox)
    {
        if (FindDescendant<ItemsPresenter>(listBox) is not { } presenter)
        {
            return null;
        }

        int count = VisualTreeHelper.GetChildrenCount(presenter);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(presenter, i) is Panel panel)
            {
                return panel;
            }
        }

        return null;
    }

    private readonly record struct RealizedContainer(int Index, double Top, double Bottom);

    private static int ComputeInsertIndex(ListBox listBox, Point position, IReadOnlyList<RealizedContainer> realized)
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

        // The cursor is not over a row. Anchor the decision to the realized content
        // bounds rather than the control's midpoint: with only a few items the
        // content ends well above ActualHeight / 2, so a position just below the
        // last item would otherwise fall in the "upper half" and wrongly report 0.
        if (realized.Count == 0)
        {
            return listBox.Items.Count;
        }

        if (position.Y <= realized[0].Top)
        {
            return realized[0].Index;
        }

        RealizedContainer last = realized[^1];
        if (position.Y >= last.Bottom)
        {
            return last.Index + 1;
        }

        // A gap between two realized rows: insert at the nearer edge.
        for (int i = 0; i < realized.Count - 1; i++)
        {
            if (position.Y < realized[i + 1].Top)
            {
                double middle = (realized[i].Bottom + realized[i + 1].Top) / 2.0;
                return position.Y < middle ? realized[i].Index + 1 : realized[i + 1].Index;
            }
        }

        return listBox.Items.Count;
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

    private enum InsertKind
    {
        None,
        BetweenItems,
        TopOfList,
        BottomOfList
    }

    private sealed class InsertionIndicatorAdorner : Adorner
    {
        private static readonly Pen LinePen = CreateLinePen();
        public double Y = -1;
        public InsertKind InsertPositionKind { get; set; } = InsertKind.None;

        public InsertionIndicatorAdorner(UIElement adornedElement) : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        private static Pen CreateLinePen()
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x2D, 0x9C, 0xD8)), 2.0);
            pen.StartLineCap = PenLineCap.Round;
            pen.EndLineCap = PenLineCap.Round;
            pen.Freeze();
            return pen;
        }

		protected override void OnRender(DrawingContext drawingContext)
		{
			if (Y < 0 || InsertPositionKind == InsertKind.None)
			{
				return;
			}
		
			var totalWidth = AdornedElement.RenderSize.Width;
			double leftPadding = AdornedElement is Control control ? control.Padding.Left : 0;
			double scrollBarWidth = SystemParameters.VerticalScrollBarWidth;
			double contentRight = totalWidth - scrollBarWidth;
		
			Point p1, p2;
			if (InsertPositionKind == InsertKind.BetweenItems)
			{
				const double rightGap = 20;
				p1 = new Point(leftPadding, Y);
				p2 = new Point(contentRight - rightGap, Y);
			}
			else
			{
				double sideMargin = 80;
				p1 = new Point(leftPadding + sideMargin, Y);
				p2 = new Point(contentRight - sideMargin, Y);
			}
		
			drawingContext.DrawLine(LinePen, p1, p2);
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