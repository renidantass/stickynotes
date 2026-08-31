using System.Collections.ObjectModel;
using StickyNotes.Data;
using StickyNotes.Models;
using StickyNotes.Services;

namespace StickyNotes.ViewModels;

public enum ArchiveFilter { All, Active, Archived }

/// <summary>ViewModel da janela "Todas as notas": lista, busca e arquivamento.
/// As notas são carregadas uma única vez e a busca/filtro operam em memória —
/// nunca re-consulta o banco a cada tecla digitada.</summary>
public class AllNotesViewModel : ViewModelBase
{
    private readonly INoteRepository _repository;
    private readonly NotesCoordinator _coordinator;
    private readonly INavigationService _navigation;
    private readonly IConfirmationService _confirmation;

    private readonly List<Note> _all;
    private string _searchText = string.Empty;
    private ArchiveFilter _filter = ArchiveFilter.All;

    public AllNotesViewModel(INoteRepository repository, NotesCoordinator coordinator,
        INavigationService navigation, IConfirmationService confirmation)
    {
        _repository = repository;
        _coordinator = coordinator;
        _navigation = navigation;
        _confirmation = confirmation;

        _all = repository.GetAll();

        CreateNoteCommand = new RelayCommand(() => CreateNote());

        // Ações direto no post-it (menu de contexto no hover)
        OpenNoteCommand = new RelayCommand(note => _navigation.OpenNote((Note)note!));
        ArchiveNoteCommand = new RelayCommand(note => ToggleArchive((Note)note!));
        DeleteNoteCommand = new RelayCommand(note => Delete((Note)note!));

        // Criação/arquivamento/exclusão vindas de outras janelas atualizam o mural.
        _coordinator.NotesChanged += (_, _) => Reload();

        Refresh();
    }

    public ObservableCollection<Note> Notes { get; } = [];

    /// <summary>Verdadeiro quando há notas visíveis (para o estado vazio do mural).</summary>
    public bool HasNotes => Notes.Count > 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                Refresh();
            }
        }
    }

    public ArchiveFilter Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Refresh();
            }
        }
    }

    public RelayCommand CreateNoteCommand { get; }
    public RelayCommand OpenNoteCommand { get; }
    public RelayCommand ArchiveNoteCommand { get; }
    public RelayCommand DeleteNoteCommand { get; }

    /// <summary>Recarrega o cache do banco (só em mudança estrutural, não na busca).</summary>
    private void Reload()
    {
        _all.Clear();
        _all.AddRange(_repository.GetAll());
        Refresh();
    }

    private void Refresh()
    {
        Notes.Clear();
        var query = _searchText.Trim();
        foreach (var note in _all.Where(Matches))
        {
            Notes.Add(note);
        }

        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(HasNotes));

        bool Matches(Note n)
        {
            bool matchesFilter = Filter switch
            {
                ArchiveFilter.Active => !n.IsArchived,
                ArchiveFilter.Archived => n.IsArchived,
                _ => true,
            };
            if (!matchesFilter)
            {
                return false;
            }

            return string.IsNullOrEmpty(query)
                || n.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || n.Body.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void CreateNote()
    {
        var note = _coordinator.Create();
        _navigation.OpenNote(note);
    }

    private void ToggleArchive(Note note) => _coordinator.ToggleArchive(note);

    private void Delete(Note note)
    {
        if (!_confirmation.ConfirmDelete(note.Title))
        {
            return;
        }

        _coordinator.Delete(note);
    }
}
