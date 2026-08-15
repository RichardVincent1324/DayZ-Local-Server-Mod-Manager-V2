using System.Windows;
using System.Windows.Controls;
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
public static class ListBoxDragDropReorder
{
    private const string DraggedItemFormat = "DayZModManager.DraggedModItem";

    public static readonly DependencyProperty ReorderCommandProperty =
        DependencyProperty.RegisterAttached(
            "ReorderCommand",
            typeof(ICommand),
            typeof(ListBoxDragDropReorder),
            new PropertyMetadata(null, OnReorderCommandChanged));

    private static readonly DependencyProperty DraggedItemProperty =
        DependencyProperty.RegisterAttached("DraggedItem", typeof(ModItemViewModel), typeof(ListBoxDragDropReorder));

    private static readonly DependencyProperty DragStartPointProperty =
        DependencyProperty.RegisterAttached("DragStartPoint", typeof(Point), typeof(ListBoxDragDropReorder));

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

        listBox.AllowDrop = true;
        listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove -= OnPreviewMouseMove;
        listBox.Drop -= OnDrop;

        listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseMove += OnPreviewMouseMove;
        listBox.Drop += OnDrop;
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        ListBoxItem? item = FindContainer(listBox, e.OriginalSource as DependencyObject);
        if (item?.DataContext is ModItemViewModel mod)
        {
            listBox.SetValue(DraggedItemProperty, mod);
            listBox.SetValue(DragStartPointProperty, e.GetPosition(listBox));
        }
        else
        {
            listBox.SetValue(DraggedItemProperty, null);
        }
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.GetValue(DraggedItemProperty) is not ModItemViewModel draggedItem)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            listBox.SetValue(DraggedItemProperty, null);
            return;
        }

        Point start = (Point)listBox.GetValue(DragStartPointProperty);
        Point current = e.GetPosition(listBox);

        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        listBox.SetValue(DraggedItemProperty, null);

        DragDrop.DoDragDrop(listBox, new DataObject(DraggedItemFormat, draggedItem), DragDropEffects.Move);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox ||
            e.Data.GetData(DraggedItemFormat) is not ModItemViewModel dragged)
        {
            return;
        }

        ICommand command = GetReorderCommand(listBox);
        if (command is null)
        {
            return;
        }

        int insertIndex;
        ListBoxItem? target = FindContainer(listBox, e.OriginalSource as DependencyObject);
        if (target?.DataContext is ModItemViewModel targetMod)
        {
            int targetItemIndex = listBox.Items.IndexOf(targetMod);
            bool insertBefore = e.GetPosition(target).Y < target.ActualHeight / 2.0;
            insertIndex = insertBefore ? targetItemIndex : targetItemIndex + 1;
        }
        else
        {
            // Dropped on empty space inside the list -> move to the end.
            Point point = e.GetPosition(listBox);
            if (point.Y < 0 || point.Y > listBox.ActualHeight || point.X < 0 || point.X > listBox.ActualWidth)
            {
                return;
            }

            insertIndex = listBox.Items.Count;
        }

        // Compensate for the dragged item being removed before the target position.
        int itemIndex = listBox.Items.IndexOf(dragged);
        int finalIndex = itemIndex < insertIndex ? insertIndex - 1 : insertIndex;

        command.Execute(new ReorderRequest(dragged.Name, finalIndex));
        e.Handled = true;
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
}
