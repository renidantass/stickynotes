using System.Windows;
using StickyNotes.Data;
using StickyNotes.Models;
using StickyNotes.Views;

namespace StickyNotes.Services;

/// <summary>Criação e abertura das janelas do app (a composição das Views fica aqui,
/// fora das ViewModels).</summary>
public class NavigationService : INavigationService
{
    private readonly INoteRepository _repository;
    private readonly NotesCoordinator _coordinator;
    private readonly IConfirmationService _confirmation;
    private readonly SettingsService _settingsService;

    public NavigationService(INoteRepository repository, NotesCoordinator coordinator,
        IConfirmationService confirmation, SettingsService settingsService)
    {
        _repository = repository;
        _coordinator = coordinator;
        _confirmation = confirmation;
        _settingsService = settingsService;
    }

    public void OpenNote(Note note)
    {
        // O deck carrega apenas metadados; o corpo é decriptado sob demanda aqui
        // (cacheado no repositório — reabrir a nota não repete o DPAPI).
        note.Body = _repository.GetBody(note.Id);

        // Evita múltiplas janelas da mesma nota (cliques repetidos no preview/aba):
        // ativa a janela existente em vez de abrir outra.
        var existing = Application.Current.Windows.OfType<NoteWindow>()
            .FirstOrDefault(w => w.NoteId == note.Id);
        if (existing is not null)
        {
            existing.Activate();
            return;
        }

        var settings = _settingsService.Load();
        var workArea = ScreenHelper.Resolve(settings.MonitorDeviceName).WorkArea;
        var window = new NoteWindow(note, _coordinator, _confirmation, settings.DockSide, workArea);
        window.Show();
    }

    public void OpenAllNotes()
    {
        // Fecha uma instância anterior antes de abrir de novo
        var existing = Application.Current.Windows.OfType<AllNotesWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            return;
        }

        var window = new AllNotesWindow(_repository, _coordinator, this, _confirmation);
        window.Show();
        window.Activate();
    }

    public void OpenSettings()
    {
        var existing = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (existing is not null)
        {
            existing.Activate();
            return;
        }

        var window = new SettingsWindow(_settingsService);
        window.DeckSideChanged += (_, _) => DeckSideChanged?.Invoke(this, EventArgs.Empty);
        window.Show();
        window.Activate();
    }

    public string GetNoteBody(long noteId) => _repository.GetBody(noteId);

    /// <summary>Dispara quando o lado do deck mudou (o App recria o deck na borda).</summary>
    public event EventHandler? DeckSideChanged;
}
