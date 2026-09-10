using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StickyNotes.Data;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.ViewModels;

namespace StickyNotes.Views;

public partial class AllNotesWindow : Window
{
    // DWMWA_WINDOW_CORNER_PREFERENCE = 33: canto arredondado do Windows 11
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaRound = 2;

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly AllNotesViewModel _viewModel;

    public AllNotesWindow(INoteRepository repository, NotesCoordinator coordinator,
        INavigationService navigation, IConfirmationService confirmation)
    {
        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();
        _viewModel = new AllNotesViewModel(repository, coordinator, navigation, confirmation);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AllNotesViewModel.Notes))
            {
                UpdateCount();
            }
        };

        Closed += (_, _) => _viewModel.Dispose();
        StateChanged += OnStateChanged;
        Loaded += OnLoaded;
    }

    /// <summary>Adia a carga para depois do primeiro frame. Buscar todas as notas
    /// decripta os corpos de uma vez; feito antes, a janela só aparecia depois
    /// desse trabalho. Agora o mural abre na hora e as notas entram em seguida.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, _viewModel.Load);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppWindowSetup.HideFromAltTab(this);
        ApplyRoundCorners();
        UpdateCount();
        UpdateMaximizeIcon();
    }

    /// <summary>Cantos arredondados no padrão do Windows 11 (DWM), como o resto do app.</summary>
    private void ApplyRoundCorners()
    {
        if (PresentationSource.FromVisual(this) is HwndSource { Handle: var handle } && handle != IntPtr.Zero)
        {
            int preference = DwmwaRound;
            // Falha silenciosa é aceitável: o DWM apenas não arredonda os cantos.
            _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeIcon();
    }

    private void UpdateMaximizeIcon()
    {
        // Troca o glifo entre maximizar (E922) e restaurar (E923), como a titlebar nativa.
        if (MaximizeButton.Content is TextBlock glyph)
        {
            glyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void UpdateCount()
    {
        CountText.Text = _viewModel.Notes.Count == 1
            ? "1 nota"
            : $"{_viewModel.Notes.Count} notas";
    }

    private void OnStickyClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Note note })
        {
            _viewModel.OpenNoteCommand.Execute(note);
        }
    }

    /// <summary>Abre a nota destacada com Enter. O cartão em si é um Border com
    /// clique de mouse — sem isto o mural só funcionava com mouse. O ListBox já
    /// dá foco por Tab e navegação por setas; faltava o Enter ativar.</summary>
    private void OnWallKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (NotesWall.SelectedItem is Note note)
        {
            _viewModel.OpenNoteCommand.Execute(note);
            e.Handled = true;
        }
    }

    /// <summary>Conjunto de notas que já receberam a animação de entrada — re-renders
    /// (busca/filtro) não re-animam post-its existentes, só os novos. Teto alto o
    /// suficiente para nunca re-animar em uso normal (limpo quando estoura).</summary>
    private const int MaxAnimatedNoteIds = 1024;
    private readonly HashSet<long> _animatedNoteIds = [];

    /// <summary>Entrada do post-it: fade + leve subida, com stagger por índice
    /// (delay = índice × 20ms + jitter) e respeito ao reduced motion do Windows.</summary>
    private void OnStickyLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el)
        {
            return;
        }

        // Apenas a primeira renderização de cada nota anima; refiltros do mural
        // recriam os containers e não devem repetir a animação (custo de layout).
        if (el.DataContext is Note note)
        {
            if (_animatedNoteIds.Count >= MaxAnimatedNoteIds)
            {
                _animatedNoteIds.Clear();
            }

            if (!_animatedNoteIds.Add(note.Id))
            {
                el.Opacity = 1;
                return;
            }
        }

        int index = 0;
        if (el.DataContext is Note n && _viewModel.Notes.IndexOf(n) is var i && i >= 0)
        {
            index = i;
        }

        double delayMs = MotionService.Enabled
            ? (index * 20) + (index % 3) * 5
            : 0;

        // Com virtualização, containers realizam ao rolar: o stagger por índice
        // absoluto não pode crescer sem limite (a nota nº 200 esperaria 4s).
        delayMs = Math.Min(delayMs, 240);

        var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        el.BeginAnimation(OpacityProperty, fade);

        if (MotionService.Enabled)
        {
            // Transform criado em código (não-freezable) para poder animar o Y
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            var translate = new TranslateTransform(0, 12);
            el.RenderTransform = translate;
            var rise = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            translate.BeginAnimation(TranslateTransform.YProperty, rise);
        }
    }
}
