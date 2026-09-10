using System.Collections.ObjectModel;
using System.Windows.Threading;
using StickyNotes.Data;
using StickyNotes.Models;
using StickyNotes.Services;

namespace StickyNotes.ViewModels;

public enum ArchiveFilter { All, Active, Archived }

/// <summary>ViewModel da janela "Todas as notas": lista, busca e arquivamento.
/// As notas são carregadas uma única vez e a busca/filtro operam em memória —
/// nunca re-consulta o banco a cada tecla digitada.</summary>
public class AllNotesViewModel : ViewModelBase, IDisposable
{
    private readonly INoteRepository _repository;
    private readonly NotesCoordinator _coordinator;
    private readonly INavigationService _navigation;
    private readonly IConfirmationService _confirmation;

    private readonly List<Note> _all = [];
    private string _searchText = string.Empty;
    private ArchiveFilter _filter = ArchiveFilter.All;
    private bool _isLoading = true;
    private readonly DispatcherTimer _searchDebounce;

    public AllNotesViewModel(INoteRepository repository, NotesCoordinator coordinator,
        INavigationService navigation, IConfirmationService confirmation)
    {
        _repository = repository;
        _coordinator = coordinator;
        _navigation = navigation;
        _confirmation = confirmation;

        // Busca com debounce (200ms): o refresh recria os containers visíveis —
        // agrupar a digitação evita refazer o mural a cada tecla.
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            Refresh();
        };

        CreateNoteCommand = new RelayCommand(() => CreateNote());

        // Ações direto no post-it (menu de contexto no hover)
        OpenNoteCommand = new RelayCommand(note => _navigation.OpenNote((Note)note!));
        ArchiveNoteCommand = new RelayCommand(note => ToggleArchive((Note)note!));
        DeleteNoteCommand = new RelayCommand(note => Delete((Note)note!));

        // Criação/arquivamento/exclusão vindas de outras janelas atualizam o mural.
        _coordinator.NotesChanged += OnCoordinatorNotesChanged;
        // Edição de conteúdo (título/corpo/cor) chega por um evento leve: atualiza
        // a instância sem recarregar o banco inteiro a cada flush de digitação.
        _coordinator.NoteSaved += OnNoteSaved;

        Refresh();
    }

    /// <summary>Carrega as notas do banco. É chamado só quando a janela já está
    /// visível: buscar todas as notas decripta os corpos de uma vez, então fazer
    /// isso no construtor adiava o aparecimento do mural.</summary>
    public void Load()
    {
        _isLoading = false;
        Reload();
    }

    /// <summary>Desassina o coordinator (o mural é aberto/fechado sob demanda; sem
    /// isso a VM ficaria retida pelo NotesChanged do coordinator, vazando memória
    /// a cada abertura do mural) e para o timer de busca.</summary>
    public void Dispose()
    {
        _searchDebounce.Stop();
        _coordinator.NotesChanged -= OnCoordinatorNotesChanged;
        _coordinator.NoteSaved -= OnNoteSaved;
        GC.SuppressFinalize(this);
    }

    private void OnCoordinatorNotesChanged(object? sender, EventArgs e) => Reload();

    /// <summary>Atualiza a instância do mural correspondente à nota salva no editor
    /// (o mural guarda instâncias próprias). Refaz o filtro apenas quando a busca
    /// ativa pode mudar de resultado — caso contrário o binding cuida do resto.</summary>
    private void OnNoteSaved(Note note)
    {
        var target = _all.Find(n => n.Id == note.Id);
        if (target is null)
        {
            return;
        }

        target.Title = note.Title;
        target.Body = note.Body;
        target.Color = note.Color;
        target.UpdatedAt = note.UpdatedAt;

        if (!string.IsNullOrEmpty(_searchText))
        {
            Refresh();
        }
    }

    public ObservableCollection<Note> Notes { get; } = [];

    /// <summary>Estado vazio só depois que a carga terminou: sem isto, o mural
    /// pisca "nenhuma nota" no frame em que abre, antes de os dados chegarem.</summary>
    public bool ShowEmptyState => !_isLoading && Notes.Count == 0;

    /// <summary>Uma parede sem notas e um filtro sem resultado são situações
    /// diferentes; o estado vazio precisa dizer o que fazer em cada uma.</summary>
    public string EmptyTitle => IsFiltered ? "Nada encontrado" : "Nenhuma nota aqui";

    public string EmptyHint => IsFiltered
        ? "Tente outro termo ou mude o filtro."
        : "Suas notas aparecem nesta parede. Crie a primeira para começar.";

    private bool IsFiltered => !string.IsNullOrEmpty(_searchText.Trim()) || _filter != ArchiveFilter.All;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                // Debounce: a propriedade atualiza na hora (o placeholder do campo
                // reage), mas o refresh do mural só roda após a pausa na digitação.
                _searchDebounce.Stop();
                _searchDebounce.Start();
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
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));

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
