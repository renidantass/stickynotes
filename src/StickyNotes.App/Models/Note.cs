using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StickyNotes.Models;

/// <summary>Cor da nota (chave de uma paleta fixa).</summary>
public static class NoteColors
{
    public static readonly string[] All =
    [
        "yellow", "pink", "blue", "green", "purple", "orange",
    ];

    public const string Default = "yellow";
}

/// <summary>Uma nota adesiva persistida localmente. Implementa INotifyPropertyChanged
/// para que o deck/preview reajam a mudanças de cor/título em runtime sem re-render.</summary>
public class Note : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _body = string.Empty;
    private string _color = NoteColors.Default;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Body
    {
        get => _body;
        set => Set(ref _body, value);
    }

    public string Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
