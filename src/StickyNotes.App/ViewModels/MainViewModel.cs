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
    private bool _isDataReady;

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        var empty = new Data.EmptyRepository();
        _coordinator = new NotesCoordinator(empty);
        _navigation = new NavigationService(empty, _coordinator, new ConfirmationService(), settingsService);

        // Aplica a preferência de tema salva (antes de qualquer janela carregar).
        ThemeManager.ApplyPreference(settingsService.Load().ThemePreference);

        // Assina pelo método nomeado (não lambda): o SetServices desassina o handler
        // do coordinator vazio ao trocar pelo real — com lambda isso nunca funcionava
        // e o coordinator vazio ficaria retido pela MainViewModel (que vive para sempre).
        _coordinator.NotesChanged += OnCoordinatorChanged;

        CreateNoteCommand = new RelayCommand(() => CreateNote(), () => _isDataReady);
        OpenNoteCommand = new RelayCommand(note => OpenNote((Models.Note)note!));
        // O mural também depende do banco: abri-lo antes de carregar mostraria uma
        // lista vazia e criaria notas no repositório provisório.
        OpenAllNotesCommand = new RelayCommand(() => _navigation.OpenAllNotes(), () => _isDataReady);
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

    /// <summary>Verdadeiro depois que o banco terminou de carregar em background.
    /// Enquanto falso, criar nota é bloqueado: o repositório provisório não persiste,
    /// então a ação falharia (ou seria perdida) durante a janela de startup.</summary>
    public bool IsDataReady
    {
        get => _isDataReady;
        private set => SetProperty(ref _isDataReady, value);
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
        IsDataReady = true;
        CommandManager.InvalidateRequerySuggested();
        OnCoordinatorChanged(this, EventArgs.Empty);
    }

    public void CreateNote()
    {
        // Guarda de corrida do startup: o usuário pode clicar na pill antes de o
        // banco carregar. Sem isto a escrita ia para o repositório provisório.
        if (!_isDataReady)
        {
            AppLog.Info("Criação de nota ignorada: os dados ainda estão carregando.");
            return;
        }

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
