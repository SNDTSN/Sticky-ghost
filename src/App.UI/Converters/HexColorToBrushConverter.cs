using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace App.UI.Converters;

public sealed class HexColorToBrushConverter : IValueConverter
{
    private readonly Dictionary<string, IBrush> _cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            if (_cache.TryGetValue(hex, out var cached))
                return cached;

            if (Color.TryParse(hex, out var color))
            {
                var brush = new SolidColorBrush(color);
                _cache[hex] = brush;
                return brush;
            }
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
