using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ScreenTimeSync.Helpers;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => (v is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => (v is bool b && b) ? new SolidColorBrush(Color.FromRgb(0xE1,0x70,0x55)) : new SolidColorBrush(Color.FromRgb(0x00,0xE6,0x76));
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class InitialConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c) => v is string s && s.Length>0 ? s[0].ToString().ToUpperInvariant() : "?";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object v, Type t, object p, CultureInfo c)
    {
        double pct = v is double d ? d : 0;
        double max = 200;
        if (p is string ps && double.TryParse(ps, out var m)) max = m;
        return Math.Clamp(pct/100*max, 4, max);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
