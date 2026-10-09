using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DotAHK.Converters;

/// <summary>
/// Converts a boolean into a <see cref="Visibility"/> value. Set
/// <see cref="Invert"/> to true to show the element when the flag is false
/// (used for the "no scripts" empty state).
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility v && v == Visibility.Visible;
        return Invert ? !visible : visible;
    }
}
