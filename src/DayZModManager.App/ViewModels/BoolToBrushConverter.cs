using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DayZModManager.App.ViewModels;

/// <summary>Converts a dirty flag into a status colour (orange = dirty, green = applied).</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    private static readonly Brush Dirty = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x2C));
    private static readonly Brush Applied = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Dirty : Applied;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
