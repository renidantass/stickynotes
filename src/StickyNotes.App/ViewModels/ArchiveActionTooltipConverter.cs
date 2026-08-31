using System.Globalization;
using System.Windows.Data;

namespace StickyNotes.ViewModels;

/// <summary>Tooltip da ação de arquivar: "Arquivar" ou "Desarquivar".</summary>
public class ArchiveActionTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Desarquivar" : "Arquivar";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
