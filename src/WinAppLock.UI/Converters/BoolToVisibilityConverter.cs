using System.Windows;

namespace WinAppLock.UI.Converters;

/// <summary>
/// Boolean → Visibility dönüşümü (XAML bağlama için).
/// true → Visible, false → Collapsed.
/// </summary>
public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Parameter "inverse" ise ters çevir
            if (parameter?.ToString() == "inverse")
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Visibility visibility)
            return visibility == Visibility.Visible;

        return false;
    }
}
