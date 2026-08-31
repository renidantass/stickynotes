using System.Globalization;
using System.Windows.Data;

namespace StickyNotes.ViewModels;

/// <summary>Converte um valor enum para bool (IsChecked de RadioButton) e vice-versa,
/// comparando com o parâmetro (nome do membro do enum).</summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is string name
            ? Enum.Parse(targetType, name)
            : Binding.DoNothing;
}
