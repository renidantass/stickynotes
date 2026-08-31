using StickyNotes.Models;

namespace StickyNotes.Services;

/// <summary>Abre janelas de nota a partir do deck (desacopla a VM das Views).</summary>
public interface INavigationService
{
    /// <summary>Abre uma nota para edição, posicionada ao lado do deck.</summary>
    void OpenNote(Note note);

    /// <summary>Abre o mural "Todas as notas"; se já estiver aberto, apenas ativa.</summary>
    void OpenAllNotes();

    /// <summary>Abre as configurações do app.</summary>
    void OpenSettings();

    /// <summary>Corpo decriptado de uma nota (o deck só carrega metadados; o preview
    /// busca o corpo sob demanda, igual ao editor).</summary>
    string GetNoteBody(long noteId);
}
