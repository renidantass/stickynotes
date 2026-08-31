using System.Windows.Input;
using StickyNotes.Services;

namespace StickyNotes.ViewModels;

/// <summary>ViewModel do deck lateral: notas ativas, expansão no hover e
/// abertura/criação de notas via serviços (navigation + coordinator).</summary>
public class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;

    private NotesCoordinator _coordinator;
    private INavigationService _navigation;
    private bool _isExpanded;

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var empty = new Data.EmptyRepository();
        _coordinator = new NotesCoordinator(empty);
        _navigation = new NavigationService(empty, _coordinator, new ConfirmationService(), settingsService);

        // Aplica a preferência de tema salva (antes de qualquer janela carregar).
        ThemeManager.ApplyPreference(settingsService.Load().ThemePreference);

        _coordinator.NotesChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Notes));
            OnPropertyChanged(nameof(NoteCount));
        };

        CreateNoteCommand = new RelayCommand(() => CreateNote());
        OpenNoteCommand = new RelayCommand(note => OpenNote((Models.Note)note!));
        OpenAllNotesCommand = new RelayCommand(() => _navigation.OpenAllNotes());
        OpenSettingsCommand = new RelayCommand(() => _navigation.OpenSettings());
        ExitCommand = new RelayCommand(() => RequestExit?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? RequestExit;

    public INavigationService Navigation => _navigation;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public IReadOnlyList<Models.Note> Notes => _coordinator.Notes;

    /// <summary>Quantidade de notas ativas (para o indicador visual da pill).</summary>
    public int NoteCount => _coordinator.NoteCount;

    public DockSide DockSide => _settingsService.Load().DockSide;

    public SettingsService SettingsService => _settingsService;

    public RelayCommand CreateNoteCommand { get; }
    public RelayCommand OpenNoteCommand { get; }
    public RelayCommand OpenAllNotesCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand ExitCommand { get; }

    /// <summary>Troca o coordinator/navigation reais após o carregamento em background
    /// do banco (o deck nasce vazio e popa as notas quando os dados chegam).</summary>
    public void SetServices(NotesCoordinator coordinator, INavigationService navigation)
    {
        _coordinator.NotesChanged -= OnCoordinatorChanged;
        _coordinator = coordinator;
        _navigation = navigation;
        _coordinator.NotesChanged += OnCoordinatorChanged;
        OnCoordinatorChanged(this, EventArgs.Empty);
    }

    public void CreateNote()
    {
        var note = _coordinator.Create();
        OpenNote(note);
    }

    public void OpenNote(Models.Note note) => _navigation.OpenNote(note);

    private void OnCoordinatorChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Notes));
        OnPropertyChanged(nameof(NoteCount));
    }
}
