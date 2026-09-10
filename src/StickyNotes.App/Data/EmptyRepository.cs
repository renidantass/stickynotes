using StickyNotes.Models;

namespace StickyNotes.Data;

/// <summary>Repositório vazio usado enquanto o banco real carrega em background
/// (o deck aparece no primeiro frame; as notas popam quando a conexão fica pronta).
/// As escritas são no-op e sinalizam ausência de persistência (o app bloqueia a
/// criação enquanto os dados não chegam, então na prática não são chamadas).</summary>
public sealed class EmptyRepository : INoteRepository
{
    public List<Note> GetAll() => [];

    public List<Note> GetAllMetadata() => [];

    public string GetBody(long id) => string.Empty;

    public long Insert(Note note) => 0;

    public bool Update(Note note) => false;

    public void SetArchived(long id, bool archived)
    {
    }

    public void Delete(long id)
    {
    }

    public void Dispose()
    {
    }
}
