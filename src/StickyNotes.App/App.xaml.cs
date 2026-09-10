using System.Windows;
using System.Windows.Threading;
using StickyNotes.Data;
using StickyNotes.Services;
using StickyNotes.ViewModels;
using StickyNotes.Views;

namespace StickyNotes;

public partial class App : Application
{
    private NoteRepository? _repository;
    private NotesCoordinator? _coordinator;
    private MainViewModel? _mainViewModel;
    private DeckWindow? _deckWindow;
    private bool _errorDialogShown;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Rede de segurança global: sem isto, qualquer exceção em handler de UI
        // fechava o app via WER, sem log e sem chance de recuperação.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppLog.Info($"StickyNotes {GetType().Assembly.GetName().Version} iniciando " +
                    $"(dados em {AppPaths.DataDirectory}).");

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
            _repository = repository;
            _coordinator = coordinator;
            _mainViewModel!.SetServices(coordinator, navigation);
            AppLog.Info($"Banco carregado: {coordinator.NoteCount} nota(s) ativa(s).");
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao carregar os dados do usuário.", ex);
            ShowStartupFailure(ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Exceção não tratada na thread de UI.", e.Exception);
        // Continua em execução em vez de fechar o app por um erro isolado.
        e.Handled = true;
        ShowErrorOnce(e.Exception);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("Exceção não tratada no domínio do app (encerrando).", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("Exceção não observada em uma Task.", e.Exception);
        e.SetObserved();
    }

    private void ShowErrorOnce(Exception exception)
    {
        if (_errorDialogShown)
        {
            return;
        }

        _errorDialogShown = true;
        ShowError("O StickyNotes encontrou um erro inesperado e continuará em execução.",
            exception);
    }

    private void ShowStartupFailure(Exception exception)
    {
        ShowError(
            "Não foi possível carregar as suas notas. O banco pode estar em uso por " +
            "outra instância ou corrompido. O app seguirá aberto, mas sem carregar dados.",
            exception);
    }

    private void ShowError(string message, Exception exception)
    {
        try
        {
            MessageBox.Show(
                $"{message}\n\nDetalhes registrados em:\n{AppLog.FilePath}\n\n{exception.Message}",
                "StickyNotes",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Falha ao mostrar o diálogo (ex.: durante shutdown): já foi para o log.
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

        // Checkpoint do WAL + liberação da conexão: encerramento ordenado do banco.
        _repository?.Dispose();
        _coordinator = null;
        AppLog.Info("StickyNotes encerrando.");

        base.OnExit(e);
    }
}
