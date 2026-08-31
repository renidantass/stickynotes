using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StickyNotes.ViewModels;

/// <summary>Inverte um bool e converte para Visibility (true → Collapsed).</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
