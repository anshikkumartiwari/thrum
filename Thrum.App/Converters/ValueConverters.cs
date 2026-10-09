using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Thrum.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch { }
        }
        return Brushes.DodgerBlue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public sealed class RelativePositionConverter : IValueConverter, IMultiValueConverter
{
    // Single binding implementation: dimension - offset
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double dimension)
        {
            double offset = 0;
            if (parameter is string paramStr && double.TryParse(paramStr, out double parsedOffset))
            {
                offset = parsedOffset;
            }
            return Math.Max(0, dimension - offset);
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();

    // Multi-binding implementation: (relative * dimension) - offset
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is double relative && values[1] is double totalDimension)
        {
            double offset = 0;
            if (parameter is string paramStr && double.TryParse(paramStr, out double parsedOffset))
            {
                offset = parsedOffset;
            }
            return Math.Max(0, (relative * totalDimension) - offset);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public sealed class LevelToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is float level && values[1] is double totalWidth)
        {
            return Math.Clamp(level * totalWidth, 0.0, totalWidth);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
