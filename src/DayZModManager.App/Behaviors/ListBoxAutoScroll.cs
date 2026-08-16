using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace DayZModManager.App.Behaviors;

/// <summary>
/// Keeps a ListBox scrolled to the newest entry: whenever its ItemsSource
/// (an <see cref="INotifyCollectionChanged"/>) changes, the last item is
/// scrolled into view. Useful for a log that appends entries.
/// </summary>
public static class ListBoxAutoScroll
{
    private static readonly ConditionalWeakTable<ListBox, SourceSink> Sinks = new();

    public static readonly DependencyProperty AutoScrollProperty =
        DependencyProperty.RegisterAttached(
            "AutoScroll",
            typeof(bool),
            typeof(ListBoxAutoScroll),
            new PropertyMetadata(false, OnAutoScrollChanged));

    public static void SetAutoScroll(DependencyObject element, bool value) =>
        element.SetValue(AutoScrollProperty, value);

    public static bool GetAutoScroll(DependencyObject element) =>
        (bool)element.GetValue(AutoScrollProperty);

    private static void OnAutoScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox)
        {
            return;
        }

        SourceSink sink = Sinks.GetValue(listBox, _ => new SourceSink(listBox));

        if (Equals(e.OldValue, true))
        {
            sink.Detach();
        }

        if (Equals(e.NewValue, true))
        {
            sink.Attach();
        }
    }

    private sealed class SourceSink
    {
        private readonly ListBox _listBox;
        private INotifyCollectionChanged? _source;

        public SourceSink(ListBox listBox)
        {
            _listBox = listBox;
        }

        public void Attach()
        {
            _listBox.DataContextChanged += OnDataContextChanged;
            _listBox.Loaded += OnLoaded;
            Subscribe(_listBox.ItemsSource);
        }

        public void Detach()
        {
            _listBox.DataContextChanged -= OnDataContextChanged;
            _listBox.Loaded -= OnLoaded;
            Unsubscribe();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
            Subscribe(_listBox.ItemsSource);

        private void OnLoaded(object sender, RoutedEventArgs e) =>
            Subscribe(_listBox.ItemsSource);

        private void Subscribe(object? source)
        {
            Unsubscribe();

            if (source is INotifyCollectionChanged incc)
            {
                _source = incc;
                _source.CollectionChanged += OnCollectionChanged;
            }
        }

        private void Unsubscribe()
        {
            if (_source is not null)
            {
                _source.CollectionChanged -= OnCollectionChanged;
                _source = null;
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_listBox.Items.Count == 0)
            {
                return;
            }

            _listBox.ScrollIntoView(_listBox.Items[^1]);
        }
    }
}
