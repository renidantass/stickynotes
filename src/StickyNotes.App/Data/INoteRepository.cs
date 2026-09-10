using StickyNotes.Models;

namespace StickyNotes.Data;

/// <summary>Contrato de persistência das notas.</summary>
public interface INoteRepository : IDisposable
{
    /// <summary>Todas as notas, na ordem estável do deck: criadas mais recentemente primeiro.
    /// Editar ou fechar uma nota NUNCA muda a posição dela na lista.</summary>
    List<Note> GetAll();

    /// <summary>Notas SEM o corpo (apenas metadados do deck/mural: título, cor, datas).
    /// Não decripta corpos — o startup e a busca ficam rápidos; o corpo é carregado
    /// sob demanda ao abrir a nota.</summary>
    List<Note> GetAllMetadata();

    /// <summary>Corpo decriptado de uma nota (sob demanda, não custa no startup).</summary>
    string GetBody(long id);

    /// <summary>Insere e retorna o Id gerado pelo banco.</summary>
    long Insert(Note note);

    /// <summary>Persiste o conteúdo (título/corpo/cor) e atualiza
    /// <see cref="Note.UpdatedAt"/>. Retorna false quando a nota não existe mais
    /// (excluída em outra janela) — o chamador deve tratar em vez de salvar no vazio.
    /// NÃO altera o estado de arquivamento: arquivar é operação separada, senão uma
    /// edição aberta em paralelo "ressuscitava" a nota.</summary>
    bool Update(Note note);

    /// <summary>Arquiva/desarquiva sem tocar no conteúdo da nota.</summary>
    void SetArchived(long id, bool archived);

    void Delete(long id);
}
