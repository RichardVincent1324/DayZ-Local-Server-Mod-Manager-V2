using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace DayZModManager.App.Behaviors;

/// <summary>
/// Attached behavior that mirrors a DataGrid's <see cref="DataGrid.SelectedItems"/>
/// (not natively bindable) into a view-model collection.
/// </summary>
public static class DataGridSelection
{
    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItems",
            typeof(IList),
            typeof(DataGridSelection),
            new PropertyMetadata(null, OnSelectedItemsChanged));

    public static void SetSelectedItems(DependencyObject element, IList value) =>
        element.SetValue(SelectedItemsProperty, value);

    public static IList GetSelectedItems(DependencyObject element) =>
        (IList)element.GetValue(SelectedItemsProperty);

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid grid)
        {
            grid.SelectionChanged -= OnSelectionChanged;
            grid.SelectionChanged += OnSelectionChanged;
        }
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        IList selectedItems = GetSelectedItems(grid);

        // Detach while mutating so clearing the target does not re-enter.
        grid.SelectionChanged -= OnSelectionChanged;
        try
        {
            selectedItems.Clear();
            foreach (object? item in grid.SelectedItems)
            {
                selectedItems.Add(item);
            }
        }
        finally
        {
            grid.SelectionChanged += OnSelectionChanged;
        }
    }
}
