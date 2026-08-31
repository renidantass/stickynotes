using StickyNotes.Models;

namespace StickyNotes.Data;

/// <summary>Repositório vazio usado enquanto o banco real carrega em background
/// (o deck aparece no primeiro frame; as notas popam quando a conexão fica pronta).</summary>
public sealed class EmptyRepository : INoteRepository
{
    public List<Note> GetAll() => [];
    public List<Note> GetAllMetadata() => [];
    public string GetBody(long id) => string.Empty;
    public long Insert(Note note) => throw new NotSupportedException();
    public void Update(Note note) => throw new NotSupportedException();
    public void Delete(long id) => throw new NotSupportedException();
}
