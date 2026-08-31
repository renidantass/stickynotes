using System.Windows;
using StickyNotes.Data;
using StickyNotes.Services;
using StickyNotes.ViewModels;
using StickyNotes.Views;

namespace StickyNotes;

public partial class App : Application
{
    private NotesCoordinator? _coordinator;
    private MainViewModel? _mainViewModel;
    private DeckWindow? _deckWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // O app só sai pelo botão "Sair" (RequestExit) — recriar janelas em runtime
        // (troca de lado do deck) não pode disparar shutdown ao fechar a última visível.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // O deck aparece imediatamente (estado vazio). O banco (cold-start do SQLite
        // nativo ~200ms) carrega em background — não bloqueia o primeiro frame.
        var settingsService = new SettingsService();
        _mainViewModel = new MainViewModel(settingsService);
        _mainViewModel.RequestExit += OnMainRequestExit;

        ShowDeck();
        LoadDataInBackground(settingsService);
    }

    private async void LoadDataInBackground(SettingsService settingsService)
    {
        try
        {
            var (repository, coordinator) = await Task.Run(() =>
            {
                var repo = new NoteRepository(settingsService.DatabasePath, new EncryptionService());
                return (repo, new NotesCoordinator(repo));
            });

            var confirmation = new ConfirmationService();
            var navigation = new NavigationService(repository, coordinator, confirmation, settingsService);
            navigation.DeckSideChanged += (_, _) => ShowDeck();
            _coordinator = coordinator;
            _mainViewModel!.SetServices(coordinator, navigation);
        }
        catch (Exception ex)
        {
            // Falha ao abrir o banco: mantém o deck vazio e registra o erro.
            System.Diagnostics.Debug.WriteLine($"[StickyNotes] falha ao carregar dados: {ex}");
        }
    }

    private void OnMainRequestExit(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private void ShowDeck()
    {
        if (_deckWindow is not null)
        {
            _deckWindow.Close();
        }

        _deckWindow = new DeckWindow(_mainViewModel!)
        {
            DockSide = _mainViewModel!.DockSide,
            WorkArea = ScreenHelper.Resolve(_mainViewModel.SettingsService.Load().MonitorDeviceName).WorkArea,
        };
        _deckWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _deckWindow?.Close();
        base.OnExit(e);
    }
}
