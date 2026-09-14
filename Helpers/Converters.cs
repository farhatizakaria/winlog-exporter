using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace LogCollector.Helpers;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

public sealed class BoolToVisibilityInvertedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not Visibility.Visible;
}

public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : value;
}

public sealed class EmptyToVisibilityConverter : IValueConverter
{
    // Expects int count -> Visible when 0, else Collapsed (for empty state)
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int c) return c == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value is string s) return string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class StringToSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var s = value as string;
        return s switch
        {
            "Success" => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
            "Warning" => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
            "Error" => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
            _ => Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
