using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace DayZModManager.App.Behaviors;

/// <summary>
/// Attached behavior that mirrors a ListBox's <see cref="ListBox.SelectedItems"/>
/// (not natively bindable) into a view-model collection.
/// </summary>
public static class ListBoxSelection
{
    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItems",
            typeof(IList),
            typeof(ListBoxSelection),
            new PropertyMetadata(null, OnSelectedItemsChanged));

    public static void SetSelectedItems(DependencyObject element, IList value) =>
        element.SetValue(SelectedItemsProperty, value);

    public static IList GetSelectedItems(DependencyObject element) =>
        (IList)element.GetValue(SelectedItemsProperty);

    private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBox listBox)
        {
            listBox.SelectionChanged -= OnSelectionChanged;
            listBox.SelectionChanged += OnSelectionChanged;
        }
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        IList selectedItems = GetSelectedItems(listBox);

        // Detach while mutating so clearing the target does not re-enter.
        listBox.SelectionChanged -= OnSelectionChanged;
        try
        {
            selectedItems.Clear();
            foreach (object? item in listBox.SelectedItems)
            {
                selectedItems.Add(item);
            }
        }
        finally
        {
            listBox.SelectionChanged += OnSelectionChanged;
        }
    }
}
