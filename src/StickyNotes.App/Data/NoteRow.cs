namespace StickyNotes.Data;

/// <summary>Espelho da linha da tabela <c>notes</c> (nomes snake_case do banco).
/// O corpo trafega criptografado e as datas no formato round-trip ISO 8601 ("o").</summary>
internal sealed class NoteRow
{
    public long Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string BodyCipher { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public bool IsArchived { get; init; }
    public string CreatedAt { get; init; } = string.Empty;
    public string UpdatedAt { get; init; } = string.Empty;
}
