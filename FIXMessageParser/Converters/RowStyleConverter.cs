using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FIXMessageParser.Converters;

public class MessageIndexToBackgroundConverter : IValueConverter
{
    private static readonly Brush[] MessageBrushes =
    [
        new SolidColorBrush(Color.FromRgb(255, 255, 255)),       // white
        new SolidColorBrush(Color.FromRgb(240, 248, 255))        // alice blue
    ];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int idx)
            return MessageBrushes[(idx - 1) % MessageBrushes.Length];
        return MessageBrushes[0];
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class SectionToForegroundConverter : IValueConverter
{
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(0, 90, 160));
    private static readonly Brush TrailerBrush = new SolidColorBrush(Color.FromRgb(140, 80, 0));
    private static readonly Brush BodyBrush = new SolidColorBrush(Colors.Black);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Header" => HeaderBrush,
            "Trailer" => TrailerBrush,
            _ => BodyBrush
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class GroupCountTagToWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isGroup && isGroup)
            return System.Windows.FontWeights.SemiBold;
        return System.Windows.FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
