using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.ViewModels;

namespace StickyNotes.Views;

public partial class NoteWindow : Window
{
    private readonly NoteEditorViewModel _viewModel;
    private readonly IConfirmationService _confirmation;
    private readonly DockSide _dockSide;
    private readonly Rect _workArea;
    private bool _closing;

    /// <summary>Id da nota aberta nesta janela (para deduplicar aberturas).</summary>
    public long NoteId { get; }

    public NoteWindow(Note note, NotesCoordinator coordinator,
        IConfirmationService confirmation, DockSide dockSide, Rect workArea)
    {
        _confirmation = confirmation;
        _dockSide = dockSide;
        _workArea = workArea;
        NoteId = note.Id;

        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();

        _viewModel = new NoteEditorViewModel(note, coordinator);
        DataContext = _viewModel;
        ApplyColor(_viewModel.Color);

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionNearDeck();
        BodyBox.Focus();
        BodyBox.CaretIndex = BodyBox.Text.Length;
        PlayEntrance();
    }

    /// <summary>A nota "pousa" na tela em vez de aparecer seca: assenta de um
    /// leve encolhimento com fade. A escala é pequena de propósito — a nota é
    /// papel, não um balão.</summary>
    private void PlayEntrance()
    {
        if (!MotionService.Enabled)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        NoteBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.985, 0.985);
        NoteBorder.RenderTransform = scale;
        var settle = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppWindowSetup.HideFromAltTab(this);
    }

    private void PositionNearDeck()
    {
        var workArea = _workArea;
        const double deckWidth = 16;

        double top = workArea.Top + (workArea.Height - Height) / 2;
        double left = _dockSide == DockSide.Left
            ? workArea.Left + deckWidth + 8
            : workArea.Right - deckWidth - Width - 8;

        Left = Math.Max(workArea.Left, left);
        Top = Math.Max(workArea.Top, top);
    }

    private void ApplyColor(string color)
    {
        // Gradiente de papel (luz de cima) + aresta derivada da cor: o volume
        // vem da luz, não de uma cor chapada.
        var brush = NoteColorBrush.GetGradient(color);
        NoteBorder.Background = brush;
        Background = brush; // o fundo da janela cobre a área do chrome (cantos recortados)
        NoteBorder.BorderBrush = NoteColorBrush.GetEdge(color);
        GrainLayer.Background = NoteColorBrush.GetGrain();
    }

    private void OnContentChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        // O binding (UpdateSourceTrigger=PropertyChanged) já atualizou Title/Body na VM,
        // que agenda o debounce. Aqui só escondemos o ✓ até salvar de novo.
        SavedIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowSavedIndicator()
    {
        SavedIndicator.Visibility = Visibility.Visible;
        SavedIndicator.Opacity = 0;
        if (MotionService.Enabled)
        {
            var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            SavedIndicator.BeginAnimation(OpacityProperty, fade);
        }
        else
        {
            SavedIndicator.Opacity = 1;
        }
    }

    // --- Arrastar a janela pela barra de título ---
    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        _viewModel.CycleColor();
        ApplyColor(_viewModel.Color);
        ShowSavedIndicator();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // Fecha a nota e "devolve ao deck" (o OnClosed salva automaticamente).
        Close();
    }

    private void OnArchiveClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Archive();
        Close();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_confirmation.ConfirmDelete(_viewModel.Title))
        {
            _viewModel.Delete();
            Close();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            BodyBox.Focus();
        }
        else if (e.Key == Key.OemPeriod && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            OnColorClick(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            OnDeleteClick(this, new RoutedEventArgs());
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            _viewModel.Save();
        }
    }
}
