using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.ViewModels;

namespace StickyNotes.Views;

public partial class DeckWindow : Window
{
    private const double CollapsedWidth = 24;
    private const double CollapsedHeight = 30;
    private const double ExpandedWidth = 66;
    private const double HoverDelayMs = 200;
    private const double MaxMenuHeight = 480;
    private const double PreviewDelayMs = 300;
    private const double PreviewWidth = 280;
    private const double PreviewGap = 6;
    private const int ToggleDeckHotkeyId = 0xA1;

    // Ctrl+Alt+S: alterna o deck
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _previewTimer;
    private bool _mouseOverDeck;
    private bool _mouseOverPreview;
    private NotePreviewWindow? _previewWindow;
    private Note? _previewNote;
    private FrameworkElement? _previewTab;
    private double _previewTabY;
    private double _expandedHeight = CollapsedHeight;

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>Posição do cursor em DIPs (unidades WPF), convertendo os pixels físicos
    /// de GetCursorPos pela escala DPI do monitor onde o deck está.</summary>
    private Point GetCursorPositionInDips()
    {
        if (!GetCursorPos(out var point))
        {
            return new Point(-1, -1);
        }

        double scale = GetDpiScale();
        return new Point(point.X / scale, point.Y / scale);
    }

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    public DeckWindow(MainViewModel viewModel)
    {
        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverDelayMs) };
        _hoverTimer.Tick += OnHoverTimerTick;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDelayMs) };
        _previewTimer.Tick += OnPreviewTimerTick;

        // As notas chegam em background (startup rápido): re-mede quando chegarem.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnDeckLoaded;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.NoteCount))
        {
            // Se ficou sem notas, recolhe para a pill clicável (hover não expande vazio).
            if (_viewModel.NoteCount == 0 && _viewModel.IsExpanded)
            {
                Collapse();
                return;
            }

            // Posterga para depois do binding de ItemsSource aplicar (prioridade DataBind
            // roda depois do PropertyChanged síncrono) — senão mede sem as abas.
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, Remeasure);
        }
    }

    private void Remeasure()
    {
        MenuPanel.Visibility = Visibility.Visible;
        MenuPanel.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
        double measured = MenuPanel.DesiredSize.Height + MenuPanel.Margin.Top + MenuPanel.Margin.Bottom;
        MenuPanel.Visibility = Visibility.Collapsed;

        if (measured <= 0)
        {
            measured = 24 + (_viewModel.Notes.Count * 66) + 96;
        }

        _expandedHeight = Math.Clamp(measured, CollapsedHeight, MaxMenuHeight);
        PositionOnEdge();
    }

    public DockSide DockSide { get; set; } = DockSide.Right;

    private void OnDeckLoaded(object sender, RoutedEventArgs e)
    {
        // Mede a altura real do menu DEPOIS do Loaded: bindings aplicados e layout pronto.
        Remeasure();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppWindowSetup.HideFromAltTab(this);

        // Hotkey global Ctrl+Alt+S para expandir/recolher o deck
        var handle = new WindowInteropHelper(this).Handle;
        RegisterHotKey(handle, ToggleDeckHotkeyId, ModControl | ModAlt, 0x53 /* S */);
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);

        // Posicionamento inicial mínimo (a altura real é aplicada no Loaded).
        var workArea = SystemParameters.WorkArea;
        Width = CollapsedWidth;
        Height = _expandedHeight;
        CenterVertically();
        Left = DockSide == DockSide.Right ? workArea.Right - Width : workArea.Left;
        ApplyPillState();
    }

    private void OnDeckKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsExpanded)
        {
            return;
        }

        // Esc recolhe o deck
        if (e.Key == Key.Escape)
        {
            Collapse();
            e.Handled = true;
            return;
        }

        // Setas navegam entre as abas; Enter abre a nota focada
        var tabs = GetNoteTabs();
        if (tabs.Count == 0)
        {
            return;
        }

        int current = -1;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].IsKeyboardFocused)
            {
                current = i;
                break;
            }
        }

        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            int next = current < 0
                ? 0
                : (e.Key == Key.Down ? (current + 1) % tabs.Count : (current - 1 + tabs.Count) % tabs.Count);
            tabs[next].Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && current >= 0)
        {
            tabs[current].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            e.Handled = true;
        }
    }

    private List<Button> GetNoteTabs()
    {
        var result = new List<Button>();
        foreach (var item in NotesItemsControl.Items)
        {
            if (NotesItemsControl.ItemContainerGenerator.ContainerFromItem(item) is ContentPresenter presenter
                && presenter.ContentTemplate.FindName("TabButton", presenter) is Button button)
            {
                result.Add(button);
            }
        }

        return result;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_HOTKEY = 0x0312
        if (msg == 0x0312 && wParam.ToInt32() == ToggleDeckHotkeyId)
        {
            if (_viewModel.IsExpanded)
            {
                Collapse();
            }
            else
            {
                Expand();
                // Foca a primeira aba para navegação por teclado (setas/Enter/Esc)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    var tabs = GetNoteTabs();
                    if (tabs.Count > 0)
                    {
                        tabs[0].Focus();
                    }
                });
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            UnregisterHotKey(handle, ToggleDeckHotkeyId);
        }

        base.OnClosed(e);
    }

    private void PositionOnEdge()
    {
        var workArea = SystemParameters.WorkArea;

        // A largura atual da janela (pill ou expandida) é preservada; apenas
        // re-centraliza verticalmente e gruda na borda correta.
        Height = _expandedHeight;
        CenterVertically();
        Left = DockSide == DockSide.Right ? workArea.Right - Width : workArea.Left;

        if (!_viewModel.IsExpanded)
        {
            ApplyPillState();
        }
    }

    /// <summary>Estado visual de pill: conteúdo compacto visível, menu oculto,
    /// janela estreita colada à borda da tela.</summary>
    private void ApplyPillState()
    {
        MenuPanel.Visibility = Visibility.Collapsed;
        ExpandButton.Visibility = Visibility.Visible;
        Width = CollapsedWidth;
        DeckBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
        UpdateEmptyState();
    }

    /// <summary>Alterna os indicadores de vazio: pill com ícone ✎, menu com convite
    /// para criar a primeira nota.</summary>
    private void UpdateEmptyState()
    {
        bool hasNotes = _viewModel.NoteCount > 0;
        EmptyIndicator.Visibility = hasNotes ? Visibility.Collapsed : Visibility.Visible;
        NoteCountBadge.Visibility = hasNotes ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Centraliza verticalmente a janela (pill ou menu) na área de trabalho.</summary>
    private void CenterVertically()
    {
        var workArea = SystemParameters.WorkArea;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        _mouseOverDeck = true;
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        _mouseOverDeck = false;
        _hoverTimer.Stop();

        // O deck só recolhe se o mouse realmente saiu do deck E do preview.
        // Pequena tolerância: dá tempo do MouseEnter do preview disparar.
        if (!_mouseOverPreview && !IsMouseOverDeckArea())
        {
            // Se o preview existe, dá uma chance dele capturar o mouse
            if (_previewWindow is not null)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    if (!_mouseOverDeck && !_mouseOverPreview && !IsMouseOverDeckArea())
                    {
                        Collapse();
                    }
                });
            }
            else
            {
                Collapse();
            }
        }
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        // Sem notas o hover não expande: a pill vazia é um alvo de clique único
        // ("criar primeira nota") — expandir impediria o clique.
        if (_mouseOverDeck && _viewModel.NoteCount > 0)
        {
            Expand();
        }
    }

    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        // Sem notas: clique na pill já cria a primeira nota (ação direta).
        if (_viewModel.NoteCount == 0)
        {
            _viewModel.CreateNoteCommand.Execute(null);
            return;
        }

        if (_viewModel.IsExpanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    private void OnNoteTabMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Note note } tab)
        {
            _previewNote = note;
            _previewTab = tab;

            // Garante que a aba hoverada está visível no viewport do deck
            // (com muitas notas o menu rola) antes de capturar a posição.
            ScrollTabIntoView(tab);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (_previewNote == note)
                {
                    _previewTabY = GetTabScreenTop(tab);
                }
            });

            _previewTimer.Stop();
            _previewTimer.Start();
        }
    }

    /// <summary>Topo da aba em coordenadas de tela em DIPs — calculado a partir da
    /// posição da janela + offset relativo (PointToScreen pode misturar pixels físicos
    /// com DIPs dependendo da escala de DPI, desalinhando o preview).</summary>
    private double GetTabScreenTop(FrameworkElement tab)
    {
        var offset = tab.TransformToAncestor(this).Transform(new Point(0, 0));
        return Top + offset.Y;
    }

    /// <summary>Rola o ScrollViewer do menu para que a aba hoverada fique visível.</summary>
    private static void ScrollTabIntoView(FrameworkElement tab)
    {
        var scroll = FindAncestor<ScrollViewer>(tab);
        if (scroll is null)
        {
            return;
        }

        var transform = tab.TransformToAncestor(scroll);
        var offset = transform.Transform(new Point(0, 0));
        double viewport = scroll.ViewportHeight;
        double top = offset.Y;
        double bottom = offset.Y + tab.ActualHeight;

        if (top < 0)
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + top);
        }
        else if (bottom > viewport)
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + (bottom - viewport));
        }
    }

    private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnNoteTabMouseLeave(object sender, MouseEventArgs e)
    {
        _previewTimer.Stop();
        ClosePreview();
        _previewNote = null;
        _previewTab = null;
    }

    private void OnPreviewTimerTick(object? sender, EventArgs e)
    {
        _previewTimer.Stop();
        if (_previewNote is not null)
        {
            ShowPreview(_previewNote);
        }
    }

    private void ShowPreview(Note note)
    {
        ClosePreview();

        // Re-captura a posição atual da aba no momento de abrir (o deck pode ter
        // sido re-medido/re-posicionado entre o hover e o timer de 300ms).
        if (_previewTab is FrameworkElement tab && tab.DataContext == note)
        {
            _previewTabY = GetTabScreenTop(tab);
        }

        // Posiciona ao lado do deck, com o topo alinhado ao topo da aba hoverada.
        // Se não couber na tela, desloca o mínimo necessário (preview de 240px máx.).
        var workArea = SystemParameters.WorkArea;
        double x = DockSide == DockSide.Right
            ? Left - PreviewWidth - PreviewGap
            : Left + ActualWidth + PreviewGap;

        double top = _previewTabY;
        double maxHeight = Math.Min(240, workArea.Bottom - top - 8);
        if (maxHeight < 120)
        {
            // Aba perto da borda inferior: sobe o preview para caber.
            top = Math.Max(workArea.Top, workArea.Bottom - 240 - 8);
            maxHeight = Math.Min(240, workArea.Bottom - top - 8);
        }

        _previewWindow = new NotePreviewWindow(note, _viewModel.Navigation, new Point(x, top), maxHeight);
        _previewWindow.MouseEnter += OnPreviewMouseEnter;
        _previewWindow.MouseLeave += OnPreviewMouseLeave;
        _previewWindow.Closed += OnPreviewClosed;
        _previewWindow.Show();
    }

    private void OnPreviewMouseEnter(object sender, MouseEventArgs e)
    {
        _mouseOverPreview = true;
    }

    private void OnPreviewMouseLeave(object sender, MouseEventArgs e)
    {
        _mouseOverPreview = false;
        _previewTimer.Stop();
        if (!IsMouseOverDeckArea())
        {
            Collapse();
        }
    }

    private void OnPreviewClosed(object? sender, EventArgs e)
    {
        _mouseOverPreview = false;
        if (_previewWindow == sender)
        {
            _previewWindow = null;
        }
    }

    private void ClosePreview()
    {
        if (_previewWindow is not null)
        {
            _previewWindow.MouseEnter -= OnPreviewMouseEnter;
            _previewWindow.MouseLeave -= OnPreviewMouseLeave;
            _previewWindow.Closed -= OnPreviewClosed;
            _previewWindow.Close();
            _previewWindow = null;
        }
    }

    private void Expand()
    {
        if (_viewModel.IsExpanded)
        {
            return;
        }

        _viewModel.IsExpanded = true;

        // Re-mede com o menu visível: garante que a altura comporta as abas
        // mesmo se o re-mede do NoteCount ainda não tiver rodado.
        MenuPanel.Visibility = Visibility.Visible;
        MenuPanel.Measure(new Size(ExpandedWidth, double.PositiveInfinity));
        double measured = MenuPanel.DesiredSize.Height + MenuPanel.Margin.Top + MenuPanel.Margin.Bottom;
        if (measured > 0)
        {
            _expandedHeight = Math.Clamp(measured, CollapsedHeight, MaxMenuHeight);
            Height = _expandedHeight;
            CenterVertically();
        }

        // Janela alarga de pill (24px) para menu (66px); o border estica junto.
        ExpandButton.Visibility = Visibility.Collapsed;
        NoteCountBadge.Visibility = Visibility.Collapsed;
        EmptyIndicator.Visibility = Visibility.Collapsed;

        AnimateWidth(ExpandedWidth);
    }

    private void Collapse()
    {
        if (!_viewModel.IsExpanded)
        {
            return;
        }

        _viewModel.IsExpanded = false;
        _previewTimer.Stop();
        ClosePreview();
        _previewNote = null;

        // Recolhe: menu some, pill volta, janela afina.
        MenuPanel.Visibility = Visibility.Collapsed;
        ExpandButton.Visibility = Visibility.Visible;
        UpdateEmptyState();

        AnimateWidth(CollapsedWidth);
    }

    /// <summary>Anima a largura da janela e ajusta o Left para manter a borda fixa.</summary>
    private void AnimateWidth(double targetWidth)
    {
        bool dockRight = DockSide == DockSide.Right;
        double edge = dockRight
            ? SystemParameters.WorkArea.Right - targetWidth
            : SystemParameters.WorkArea.Left;

        var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(WidthProperty, widthAnim);
        Width = targetWidth;

        var leftAnim = new DoubleAnimation(edge, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        BeginAnimation(LeftProperty, leftAnim);
        Left = edge;
    }

    private bool IsMouseOverDeckArea()
    {
        var cursor = GetCursorPositionInDips();
        if (cursor.X < 0)
        {
            return false;
        }

        // Deck expandido + área do preview contam como "sobre o deck"
        double extra = _previewWindow is not null ? PreviewWidth + PreviewGap : 0;
        var bounds = DockSide == DockSide.Right
            ? new Rect(Left - extra, Top, ActualWidth + extra, ActualHeight)
            : new Rect(Left, Top, ActualWidth + extra, ActualHeight);
        return bounds.Contains(cursor);
    }
}
