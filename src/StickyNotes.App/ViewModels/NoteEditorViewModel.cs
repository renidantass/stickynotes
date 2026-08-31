using System.Windows.Threading;
using StickyNotes.Models;
using StickyNotes.Services;

namespace StickyNotes.ViewModels;

/// <summary>Estado de edição de uma nota: título, corpo, cor e salvamento com
/// debounce + flush periódico. Minimiza o I/O de escrita: o save só acontece
/// após uma pausa na digitação (250ms) ou no máximo a cada 800ms de digitação
/// contínua; no fechamento da janela o save é imediato.</summary>
public class NoteEditorViewModel : ViewModelBase
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(800);

    private readonly Note _note;
    private readonly NotesCoordinator _coordinator;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _flushTimer;

    private string _title;
    private string _body;
    private string _color;

    public NoteEditorViewModel(Note note, NotesCoordinator coordinator)
    {
        _note = note;
        _coordinator = coordinator;
        _title = note.Title;
        _body = note.Body;
        _color = note.Color;

        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => Save();

        _flushTimer = new DispatcherTimer { Interval = FlushInterval };
        _flushTimer.Tick += (_, _) => Save();
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value))
            {
                ScheduleSave();
            }
        }
    }

    public string Body
    {
        get => _body;
        set
        {
            if (SetProperty(ref _body, value))
            {
                ScheduleSave();
            }
        }
    }

    public string Color
    {
        get => _color;
        private set => SetProperty(ref _color, value);
    }

    /// <summary>Avança para a próxima cor da paleta e salva.</summary>
    public void CycleColor()
    {
        Color = NoteColorBrush.Next(Color);
        Save();
    }

    /// <summary>Salva agora (usado no fechamento da janela, no debounce e no flush).</summary>
    public void Save()
    {
        _saveTimer.Stop();
        _flushTimer.Stop();
        _note.Title = Title.Trim();
        _note.Body = Body;
        _note.Color = Color;
        _coordinator.Save(_note);
    }

    /// <summary>Arquiva a nota (torna inativa e a remove do deck).</summary>
    public void Archive()
    {
        _coordinator.SetArchived(_note, archived: true);
    }

    public void Delete()
    {
        _coordinator.Delete(_note);
    }

    private void ScheduleSave()
    {
        // Debounce: espera a pausa na digitação. O flush periódico garante o
        // save mesmo durante digitação contínua, sem salvar a cada tecla.
        _saveTimer.Stop();
        _saveTimer.Start();
        _flushTimer.Stop();
        _flushTimer.Start();
    }
}
