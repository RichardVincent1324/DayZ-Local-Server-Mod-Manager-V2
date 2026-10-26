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

    // Kept in a field so EndDrag can remove the exact same delegate instance.
    private static readonly RequestBringIntoViewEventHandler SuppressBringIntoViewHandler =
        OnRequestBringIntoView;

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
        finally
        {
            // While a drag is in progress, block the bubbling MouseMove event.
            // This prevents the ListBox's internal edge auto-scroll logic from 
            // triggering when the mouse is dragged beyond the top/bottom edges.
            if (state.IsDragging)
            {
                e.Handled = true;
            }
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

        // Suppress WPF's focus transfer. Without this, ListBoxItems will try to
        // grab focus as the cursor moves, firing RequestBringIntoView and causing
        // the gray focus visual to jump around or auto-scroll.
        listBox.PreviewGotKeyboardFocus -= OnPreviewGotKeyboardFocus;
        listBox.PreviewGotKeyboardFocus += OnPreviewGotKeyboardFocus;
        
        // Steal focus away from the clicked item to stop it from requesting visibility.
        listBox.Focus();

        // Block the ScrollViewer's auto-scroll (BringIntoView) for the duration of
        // the drag; see OnRequestBringIntoView. The handler must sit on the
        // ItemsPresenter because it is below the ScrollViewer on the bubbling
        // route, so it can mark the request handled before the ScrollViewer sees it.
        if (FindDescendant<ItemsPresenter>(listBox) is { } itemsPresenter)
        {
            itemsPresenter.AddHandler(
                FrameworkElement.RequestBringIntoViewEvent,
                SuppressBringIntoViewHandler,
                handledEventsToo: true);
        }

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
        
        // Handle hover detection for controlled edge scrolling.
        UpdateEdgeScroll(listBox, state);
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
        // Stop any active edge auto-scroll countdowns or timers immediately.
        StopEdgeScroll(state);

        state.Ghost?.Close();

        if (state.IndicatorLayer is { } layer && state.Indicator is { } indicator)
        {
            layer.Remove(indicator);
        }

        // Re-enable the ScrollViewer's auto-scroll (BringIntoView) that was
        // suppressed in BeginDrag.
        if (FindDescendant<ItemsPresenter>(listBox) is { } itemsPresenter)
        {
            itemsPresenter.RemoveHandler(
                FrameworkElement.RequestBringIntoViewEvent,
                SuppressBringIntoViewHandler);
        }

        // Restore normal focus management.
        listBox.PreviewGotKeyboardFocus -= OnPreviewGotKeyboardFocus;

        listBox.SetValue(IsDragActiveProperty, false);

        state.Reset();
        ReleaseCapture(listBox);
    }

    /// <summary>
    /// While a drag is in progress nothing may be brought into view: moving the
    /// cursor to the top or bottom edge would otherwise make the ScrollViewer
    /// auto-scroll to follow the item, recycle containers and make the selection
    /// highlight jump. The handler is attached to the ItemsPresenter, which sits
    /// below the ScrollViewer on the bubbling route, so it can stop the request
    /// before the ScrollViewer handles it. Wheel scrolling is unaffected because
    /// it drives the ScrollViewer directly from OnPreviewMouseWheel.
    /// </summary>
    private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// Blocks keyboard focus from transferring to ListBoxItems during a drag,
    /// which prevents the native gray focus visual from jumping to other rows.
    /// </summary>
    private static void OnPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ListBox listBox && GetState(listBox).IsDragging)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Monitors mouse position to trigger edge auto-scrolling.
    /// Only triggers when the cursor is INSIDE the ListBox bounds (preventing 
    /// uncontrolled scrolling when dragging outside), and requires a short 
    /// hover delay to avoid accidental triggers.
    /// </summary>
    private static void UpdateEdgeScroll(ListBox listBox, DragState state)
    {
        if (FindDescendant<ScrollViewer>(listBox) is not { } scrollViewer) return;

        // Skip if the list has already reached its scroll limits.
        bool canScrollUp = scrollViewer.VerticalOffset > 0;
        bool canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;

        // Absolute pixel threshold (30px) from the top/bottom edges.
        const double edgeThreshold = 30.0; 
        Point position = Mouse.GetPosition(listBox);

        int newDirection = 0;
        
        // Top edge: must be strictly inside the control (Y >= 0) and within threshold.
        if (canScrollUp && position.Y >= 0 && position.Y < edgeThreshold)
        {
            newDirection = -1;
        }
        // Bottom edge: must be strictly inside the control (Y <= ActualHeight) and within threshold.
        else if (canScrollDown && position.Y <= listBox.ActualHeight && position.Y > listBox.ActualHeight - edgeThreshold)
        {
            newDirection = 1;
        }

        // If the direction changes (entered edge, left edge, or jumped from top to bottom)
        if (newDirection != state.EdgeDirection)
        {
            StopEdgeScroll(state);
            state.EdgeDirection = newDirection;

            if (newDirection != 0)
            {
                // 0.5 seconds hover delay before initiating scroll.
                state.HoverTimer = new DispatcherTimer(DispatcherPriority.Input)
                {
                    Interval = TimeSpan.FromSeconds(0.5) 
                };
                state.HoverTimer.Tick += (s, e) =>
                {
                    state.HoverTimer?.Stop();
                    state.HoverTimer = null;
                    StartScrollTimer(listBox, state, newDirection);
                };
                state.HoverTimer.Start();
            }
        }
    }

    /// <summary>
    /// Starts the actual scrolling logic. Uses LineDown/LineUp to ensure the list 
    /// scrolls exactly one logical item at a time, avoiding pixel-based visual glitches.
    /// </summary>
    private static void StartScrollTimer(ListBox listBox, DragState state, int direction)
    {
        if (state.ScrollTimer != null) return;

        // Scroll one row every 150ms (approx. 6-7 rows per second), providing 
        // a smooth "stepped" scrolling experience.
        state.ScrollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        state.ScrollTimer.Tick += (s, e) =>
        {
            if (FindDescendant<ScrollViewer>(listBox) is { } scrollViewer)
            {
                if (direction > 0)
                {
                    scrollViewer.LineDown();
                }
                else
                {
                    scrollViewer.LineUp();
                }

                // Critical: Manually refresh the insertion indicator. Since the 
                // mouse isn't moving while scrolling, OnPreviewMouseMove won't fire,
                // which would otherwise leave the blue line stuck at its old position.
                UpdateInsertionIndicator(listBox, state);
            }
        };
        state.ScrollTimer.Start();
    }

    /// <summary>
    /// Stops and cleans up both the hover delay timer and the scroll timer.
    /// </summary>
    private static void StopEdgeScroll(DragState state)
    {
        state.HoverTimer?.Stop();
        state.HoverTimer = null;
        state.ScrollTimer?.Stop();
        state.ScrollTimer = null;
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

        // Timers for the controlled edge auto-scrolling.
        public DispatcherTimer? HoverTimer; 
        public DispatcherTimer? ScrollTimer;
        public int EdgeDirection; // -1 = top, 1 = bottom, 0 = none

        public void Reset()
        {
            DraggedItem = null;
            IsDragging = false;
            SuppressEndDrag = false;
            DragStartSelection = null;
            Ghost = null;
            IndicatorLayer = null;
            Indicator = null;

            // Ensure timers are stopped and cleaned up
            HoverTimer?.Stop();
            HoverTimer = null;
            ScrollTimer?.Stop();
            ScrollTimer = null;
            EdgeDirection = 0;
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