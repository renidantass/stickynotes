using System.Globalization;
using System.Windows.Data;

namespace StickyNotes.ViewModels;

/// <summary>Mostra "Arquivada"/"Ativa" conforme o estado da nota.</summary>
public class ArchiveStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Arquivada" : "Ativa";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
