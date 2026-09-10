using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using Microsoft.Data.Sqlite;
using StickyNotes.Models;
using StickyNotes.Services;

namespace StickyNotes.Data;

/// <summary>Persistência das notas em SQLite via Dapper. O corpo é criptografado via DPAPI;
/// o título fica em texto plano para permitir exibição no deck e busca sem decriptar tudo.
/// Usa uma única conexão reutilizável (o app é single-user e single-threaded na UI):
/// abrir/fechar conexão a cada operação custa handshake e lock de arquivo.</summary>
public sealed class NoteRepository : INoteRepository
{
    /// <summary>Versão atual do schema; ver <see cref="Migrate"/>.</summary>
    private const int SchemaVersion = 1;

    /// <summary>Texto devolvido quando um blob não pode ser decriptado (outro usuário/
    /// máquina ou corrupção) — uma nota ilegível não pode derrubar o app.</summary>
    private const string UnreadableBody = "[conteúdo ilegível]";

    private readonly SqliteConnection _connection;
    private readonly IEncryptionService _encryption;

    /// <summary>Cache de corpos decriptados (id → versão + corpo). DPAPI/Unprotect é caro:
    /// sem cache, cada hover no preview re-decriptava a nota e cada reload do mural
    /// re-decriptava todas. Validade pela versão da linha (qualquer escrita incrementa);
    /// teto de entradas com descarte do menos usado recentemente.</summary>
    private const int MaxCachedBodies = 512;
    private readonly Dictionary<long, (long Version, string Body)> _bodyCache = new();
    private readonly Dictionary<long, long> _cacheAccess = new();
    private long _accessCounter;
    private bool _disposed;

    public NoteRepository(string dbPath, IEncryptionService encryption)
    {
        _encryption = encryption;
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        ApplyPragmas();
        Migrate();
    }

    /// <summary>Modo WAL + synchronous=NORMAL: escritas muito mais baratas (sem fsync
    /// por transação) mantendo consistência — ideal para app single-user de escrita leve.</summary>
    private void ApplyPragmas()
    {
        _connection.Execute("PRAGMA journal_mode = WAL;");
        _connection.Execute("PRAGMA synchronous = NORMAL;");
        // Sem timeout explícito, um lock de outra instância esperava o default do
        // provider (~30s) travando a UI. 3s falha rápido e de forma rastreável.
        _connection.Execute("PRAGMA busy_timeout = 3000;");
        // O título fica em texto plano; sem isto, uma nota excluída ainda poderia
        // remanescer legível em páginas livres do arquivo.
        _connection.Execute("PRAGMA secure_delete = ON;");
    }

    /// <summary>Migração versionada via <c>PRAGMA user_version</c>: o antigo
    /// <c>CREATE TABLE IF NOT EXISTS</c> nunca alterava um banco existente, então a
    /// primeira mudança de coluna quebraria silenciosamente o banco do usuário.
    /// Cada degrau é aplicado uma vez e a versão é gravada no fim.</summary>
    private void Migrate()
    {
        int version = _connection.ExecuteScalar<int>("PRAGMA user_version;");
        if (version >= SchemaVersion)
        {
            return;
        }

        using var transaction = _connection.BeginTransaction();

        if (version < 1)
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
                """, transaction: transaction);

            // Bancos criados antes do versionamento não têm a coluna version.
            if (!ColumnExists("notes", "version", transaction))
            {
                _connection.Execute(
                    "ALTER TABLE notes ADD COLUMN version INTEGER NOT NULL DEFAULT 0;",
                    transaction: transaction);
            }
        }

        _connection.Execute($"PRAGMA user_version = {SchemaVersion};", transaction: transaction);
        transaction.Commit();
        AppLog.Info($"Schema do banco migrado da versão {version} para {SchemaVersion}.");
    }

    private bool ColumnExists(string table, string column, SqliteTransaction transaction)
    {
        var columns = _connection.Query($"PRAGMA table_info({table});", transaction: transaction);
        foreach (IDictionary<string, object> row in columns)
        {
            if (row.TryGetValue("name", out var name)
                && string.Equals(name as string, column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public List<Note> GetAll()
    {
        var rows = _connection.Query<NoteRow>("""
            SELECT id, title, body_cipher AS BodyCipher, color, is_archived AS IsArchived,
                   created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
            FROM notes
            ORDER BY id DESC;
            """);
        return rows.Select(ToNote).ToList();
    }

    public List<Note> GetAllMetadata()
    {
        var rows = _connection.Query<NoteRow>("""
            SELECT id, title, '' AS BodyCipher, color, is_archived AS IsArchived,
                   created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
            FROM notes
            ORDER BY id DESC;
            """);
        return rows.Select(ToNote).ToList();
    }

    /// <summary>Corpo decriptado de uma nota (sob demanda — não custa no startup).
    /// Usa o cache de decriptação: o hover repetido no preview não repete DPAPI.</summary>
    public string GetBody(long id)
    {
        var row = _connection.QuerySingleOrDefault<(long Version, string BodyCipher)>(
            "SELECT version, body_cipher FROM notes WHERE id = $id;", new { id });
        if (row.BodyCipher is null)
        {
            return string.Empty;
        }

        if (_bodyCache.TryGetValue(id, out var cached) && cached.Version == row.Version)
        {
            Touch(id);
            return cached.Body;
        }

        string body = SafeDecrypt(row.BodyCipher, id);
        CacheBody(id, row.Version, body);
        return body;
    }

    public long Insert(Note note)
    {
        long id = _connection.ExecuteScalar<long>("""
            INSERT INTO notes (title, body_cipher, color, is_archived, created_at, updated_at, version)
            VALUES ($title, $body, $color, $archived, $created, $updated, 0);
            SELECT last_insert_rowid();
            """, InsertParams(note));
        note.Version = 0;
        CacheBody(id, 0, note.Body ?? string.Empty);
        return id;
    }

    public bool Update(Note note)
    {
        note.UpdatedAt = DateTime.Now;
        int rows = _connection.Execute("""
            UPDATE notes
            SET title = $title, body_cipher = $body, color = $color,
                updated_at = $updated, version = version + 1
            WHERE id = $id;
            """, UpdateParams(note));
        if (rows == 0)
        {
            return false;
        }

        note.Version++;
        // Pré-aquece o cache com o corpo recém-salvo: o preview seguinte não decripta.
        CacheBody(note.Id, note.Version, note.Body ?? string.Empty);
        return true;
    }

    public void SetArchived(long id, bool archived)
    {
        _connection.Execute("""
            UPDATE notes
            SET is_archived = $archived, updated_at = $updated, version = version + 1
            WHERE id = $id;
            """, new
        {
            id,
            archived = archived ? 1 : 0,
            updated = ToRoundTrip(DateTime.Now),
        });
    }

    public void Delete(long id)
    {
        _connection.Execute("DELETE FROM notes WHERE id = $id;", new { id });
        _bodyCache.Remove(id);
        _cacheAccess.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // Consolida o WAL no arquivo principal antes de fechar: evita deixar
            // -wal/-shm acumulados (e mais superfície com dados em texto plano).
            _connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch (Exception ex)
        {
            AppLog.Warn("Falha no checkpoint do WAL ao fechar o banco.", ex);
        }

        _connection.Close();
        _connection.Dispose();
        _bodyCache.Clear();
        _cacheAccess.Clear();
        GC.SuppressFinalize(this);
    }

    private void CacheBody(long id, long version, string body)
    {
        if (_bodyCache.Count >= MaxCachedBodies && !_bodyCache.ContainsKey(id))
        {
            EvictLeastRecentlyUsed();
        }

        _bodyCache[id] = (version, body);
        Touch(id);
    }

    private void Touch(long id) => _cacheAccess[id] = ++_accessCounter;

    private void EvictLeastRecentlyUsed()
    {
        long oldestId = -1;
        long oldestTick = long.MaxValue;
        foreach (var entry in _cacheAccess)
        {
            if (entry.Value < oldestTick)
            {
                oldestTick = entry.Value;
                oldestId = entry.Key;
            }
        }

        if (oldestId >= 0)
        {
            _cacheAccess.Remove(oldestId);
            _bodyCache.Remove(oldestId);
        }
    }

    private Note ToNote(NoteRow row)
    {
        string body;
        if (string.IsNullOrEmpty(row.BodyCipher))
        {
            // Modo metadados (GetAllMetadata): corpo vazio, sem tocar em DPAPI.
            body = string.Empty;
        }
        else if (_bodyCache.TryGetValue(row.Id, out var cached) && cached.Version == row.Version)
        {
            Touch(row.Id);
            body = cached.Body;
        }
        else
        {
            body = SafeDecrypt(row.BodyCipher, row.Id);
            CacheBody(row.Id, row.Version, body);
        }

        return new Note()
        {
            Id = row.Id,
            Title = row.Title,
            Body = body,
            Color = row.Color,
            IsArchived = row.IsArchived,
            CreatedAt = ParseRoundTrip(row.CreatedAt),
            UpdatedAt = ParseRoundTrip(row.UpdatedAt),
            Version = row.Version,
        };
    }

    /// <summary>Isola a falha de decriptação por nota: um blob de outro usuário/máquina
    /// ou corrompido não pode propagar exceção para o hover/abertura nem impedir a
    /// leitura das demais linhas.</summary>
    private string SafeDecrypt(string cipherText, long id)
    {
        try
        {
            return _encryption.Decrypt(cipherText);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            AppLog.Warn($"Não foi possível decriptar o corpo da nota {id}.", ex);
            return UnreadableBody;
        }
    }

    /// <summary>Datas gravadas em formato round-trip ISO 8601 ("o"): o parse deve ser
    /// invariante à localidade do usuário, senão o formato pode ser mal interpretado.</summary>
    private static DateTime ParseRoundTrip(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string ToRoundTrip(DateTime value) =>
        value.ToString("o", CultureInfo.InvariantCulture);

    private object InsertParams(Note note) => new
    {
        title = note.Title ?? string.Empty,
        body = _encryption.Encrypt(note.Body ?? string.Empty),
        color = note.Color,
        archived = note.IsArchived ? 1 : 0,
        created = ToRoundTrip(note.CreatedAt),
        updated = ToRoundTrip(note.UpdatedAt),
    };

    /// <summary>Sem <c>archived</c>: editar o conteúdo nunca sobrescreve o estado de
    /// arquivamento gravado por outra superfície.</summary>
    private object UpdateParams(Note note) => new
    {
        id = note.Id,
        title = note.Title ?? string.Empty,
        body = _encryption.Encrypt(note.Body ?? string.Empty),
        color = note.Color,
        updated = ToRoundTrip(note.UpdatedAt),
    };
}
