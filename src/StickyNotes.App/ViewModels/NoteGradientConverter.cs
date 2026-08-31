using System.Globalization;
using System.Windows.Data;

namespace StickyNotes.ViewModels;

/// <summary>Converte a chave de cor da nota (ex.: "yellow") para um gradiente
/// vertical (topo claro, base escura) — efeito de pilha de post-its.</summary>
public class NoteGradientConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => NoteColorBrush.GetGradient(value as string ?? string.Empty);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
