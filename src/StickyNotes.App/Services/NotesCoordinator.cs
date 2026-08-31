using StickyNotes.Data;
using StickyNotes.Models;

namespace StickyNotes.Services;

/// <summary>Fonte única da lista de notas do deck e orquestração das operações
/// (criar/salvar/arquivar/excluir). Centraliza o reload e notifica as janelas —
/// nenhuma ViewModel recarrega o banco por conta própria.</summary>
public class NotesCoordinator
{
    private readonly INoteRepository _repository;
    private List<Note> _notes = [];

    public NotesCoordinator(INoteRepository repository)
    {
        _repository = repository;
        Reload();
    }

    /// <summary>Notas ativas do deck, na ordem estável (criação, mais recente primeiro).</summary>
    public IReadOnlyList<Note> Notes => _notes;

    public int NoteCount => _notes.Count;

    /// <summary>Mudança estrutural: criar, arquivar, desarquivar ou excluir.</summary>
    public event EventHandler? NotesChanged;

    public void Reload()
    {
        ReloadCore();
        NotesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cria uma nota nova, recarrega e retorna a instância para abrir.</summary>
    public Note Create()
    {
        var note = new Note { Title = "Nova nota", Color = NoteColors.Default };
        note.Id = _repository.Insert(note);
        Reload();
        return note;
    }

    public void Save(Note note)
    {
        _repository.Update(note);
        // Atualiza o título/cor em memória sem re-consultar o banco (zero I/O).
        // A posição na lista NUNCA muda ao salvar — ordem estável preservada.
        int index = _notes.FindIndex(n => n.Id == note.Id);
        if (index >= 0)
        {
            _notes[index].Title = note.Title;
            _notes[index].Body = note.Body;
            _notes[index].Color = note.Color;
        }
    }

    public void ToggleArchive(Note note)
    {
        SetArchived(note, !note.IsArchived);
    }

    /// <summary>Arquiva/desarquiva forçando o estado (não alterna) — usado por
    /// ações que sabem o estado desejado, como "Arquivar" na janela da nota.</summary>
    public void SetArchived(Note note, bool archived)
    {
        note.IsArchived = archived;
        _repository.Update(note);
        Reload();
    }

    public void Delete(Note note)
    {
        _repository.Delete(note.Id);
        Reload();
    }

    private void ReloadCore()
    {
        _notes = _repository.GetAllMetadata()
            .Where(n => !n.IsArchived)
            .ToList();
    }
}
