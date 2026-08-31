using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using StickyNotes.Models;
using StickyNotes.Services;

namespace StickyNotes.Data;

/// <summary>Persistência das notas em SQLite via Dapper. O corpo é criptografado via DPAPI;
/// o título fica em texto plano para permitir exibição no deck e busca sem decriptar tudo.
/// Usa uma única conexão reutilizável (o app é single-user e single-threaded na UI):
/// abrir/fechar conexão a cada operação custa handshake e lock de arquivo.</summary>
public class NoteRepository : INoteRepository
{
    private readonly SqliteConnection _connection;
    private readonly IEncryptionService _encryption;

    public NoteRepository(string dbPath, IEncryptionService encryption)
    {
        _encryption = encryption;
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        ApplyPragmas();
        EnsureCreated();
    }

    /// <summary>Modo WAL + synchronous=NORMAL: escritas muito mais baratas (sem fsync
    /// por transação) mantendo consistência — ideal para app single-user de escrita leve.</summary>
    private void ApplyPragmas()
    {
        _connection.Execute("PRAGMA journal_mode = WAL;");
        _connection.Execute("PRAGMA synchronous = NORMAL;");
    }

    private void EnsureCreated()
    {
        _connection.Execute("""
            CREATE TABLE IF NOT EXISTS notes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL DEFAULT '',
                body_cipher TEXT NOT NULL DEFAULT '',
                color TEXT NOT NULL DEFAULT 'yellow',
                is_archived INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
    }

    public List<Note> GetAll()
    {
        var rows = _connection.Query<NoteRow>("""
            SELECT id, title, body_cipher AS BodyCipher, color, is_archived AS IsArchived,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM notes
            ORDER BY id DESC;
            """);
        return rows.Select(ToNote).ToList();
    }

    public List<Note> GetAllMetadata()
    {
        var rows = _connection.Query<NoteRow>("""
            SELECT id, title, '' AS BodyCipher, color, is_archived AS IsArchived,
                   created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM notes
            ORDER BY id DESC;
            """);
        return rows.Select(ToNote).ToList();
    }

    /// <summary>Corpo decriptado de uma nota (sob demanda — não custa no startup).</summary>
    public string GetBody(long id)
    {
        string cipher = _connection.ExecuteScalar<string>(
            "SELECT body_cipher FROM notes WHERE id = $id;", new { id }) ?? string.Empty;
        return _encryption.Decrypt(cipher);
    }

    public long Insert(Note note)
    {
        return _connection.ExecuteScalar<long>("""
            INSERT INTO notes (title, body_cipher, color, is_archived, created_at, updated_at)
            VALUES ($title, $body, $color, $archived, $created, $updated);
            SELECT last_insert_rowid();
            """, ToParams(note));
    }

    public void Update(Note note)
    {
        note.UpdatedAt = DateTime.Now;
        _connection.Execute("""
            UPDATE notes
            SET title = $title, body_cipher = $body, color = $color,
                is_archived = $archived, updated_at = $updated
            WHERE id = $id;
            """, ToParams(note));
    }

    public void Delete(long id)
    {
        _connection.Execute("DELETE FROM notes WHERE id = $id;", new { id });
    }

    private Note ToNote(NoteRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Body = _encryption.Decrypt(row.BodyCipher),
        Color = row.Color,
        IsArchived = row.IsArchived,
        CreatedAt = ParseRoundTrip(row.CreatedAt),
        UpdatedAt = ParseRoundTrip(row.UpdatedAt),
    };

    /// <summary>Datas gravadas em formato round-trip ISO 8601 ("o"): o parse deve ser
    /// invariante à localidade do usuário, senão o formato pode ser mal interpretado.</summary>
    private static DateTime ParseRoundTrip(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private object ToParams(Note note) => new
    {
        id = note.Id,
        title = note.Title ?? string.Empty,
        body = _encryption.Encrypt(note.Body ?? string.Empty),
        color = note.Color,
        archived = note.IsArchived ? 1 : 0,
        created = note.CreatedAt.ToString("o"),
        updated = note.UpdatedAt.ToString("o"),
    };
}
