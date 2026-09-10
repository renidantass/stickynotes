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
    // Latência de hover curta: 200ms fazia o deck parecer travado antes de reagir.
    // 90ms ainda evita disparo acidental ao passar o mouse de raspão.
    private const double HoverDelayMs = 90;
    private const double MaxMenuHeight = 480;
    private const double PreviewDelayMs = 200;
    private const double PreviewWidth = 280;
    private const double PreviewGap = 6;
    // Vigia mais frequente = recolhimento mais responsivo (2 ticks ≈ 160ms).
    private const double IdleCheckIntervalMs = 80;
    private const int ToggleDeckHotkeyId = 0xA1;

    // Ctrl+Alt+S: alterna o deck
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _hoverTimer;
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _idleCheckTimer;
    private int _idleCheckMisses;
    private bool _mouseOverDeck;
    private bool _mouseOverPreview;
    private NotePreviewWindow? _previewWindow;
    private Note? _previewNote;
    private FrameworkElement? _previewTab;
    private HwndSource? _hwndSource;
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

        // Fallback: monitor primário (o App sobrescreve com o monitor configurado).
        WorkArea = SystemParameters.WorkArea;

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HoverDelayMs) };
        _hoverTimer.Tick += OnHoverTimerTick;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PreviewDelayMs) };
        _previewTimer.Tick += OnPreviewTimerTick;

        // Vigia o cursor enquanto o deck está expandido: o MouseLeave do WPF pode
        // ser engolido pela animação de resize (bater no canto e sair rápido), então
        // um check periódico da posição real do cursor garante o recolhimento.
        _idleCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IdleCheckIntervalMs) };
        _idleCheckTimer.Tick += OnIdleCheckTimerTick;

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
        // Mede o menu com a altura máxima como constraint: as abas rolam (row *)
        // e os botões fixos (row Auto) nunca são cortados quando há muitas notas.
        // Preserva a visibilidade atual — nunca esconde o menu se já expandido.
        bool wasVisible = MenuPanel.Visibility == Visibility.Visible;
        MenuPanel.Visibility = Visibility.Visible;
        MenuPanel.Measure(new Size(ExpandedWidth, MaxMenuHeight));
        double measured = MenuPanel.DesiredSize.Height + MenuPanel.Margin.Top + MenuPanel.Margin.Bottom;
        if (!wasVisible)
        {
            MenuPanel.Visibility = Visibility.Collapsed;
        }

        if (measured <= 0)
        {
            measured = 24 + (_viewModel.Notes.Count * 66) + 96;
        }

        _expandedHeight = Math.Clamp(measured, CollapsedHeight, MaxMenuHeight);
        PositionOnEdge();
        UpdateScrollIndicators();
    }

    public DockSide DockSide { get; set; } = DockSide.Right;

    /// <summary>Área de trabalho (DIPs) do monitor onde o deck fica encostado.
    /// Preenchida pelo App com base na preferência de monitor das settings.</summary>
    public Rect WorkArea { get; set; }

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
        if (!RegisterHotKey(handle, ToggleDeckHotkeyId, ModControl | ModAlt, 0x53 /* S */))
        {
            // Falha silenciosa deixava o atalho "morto" sem nenhum sinal (outro app
            // pode já ter registrado a combinação).
            AppLog.Warn("Não foi possível registrar o atalho global Ctrl+Alt+S (já em uso?).");
        }

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        // Posicionamento inicial mínimo (a altura real é aplicada no Loaded).
        var workArea = WorkArea;
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
        // Cleanup obrigatório: o App recria o deck a cada troca de lado/monitor e a
        // MainViewModel vive para sempre. Sem isso, a janela antiga ficava retida
        // pelo PropertyChanged (leak da árvore visual inteira) e os timers seguiam
        // vivos — o idle check rodava GetCursorPos 8x/s com o deck já fechado.
        _hoverTimer.Stop();
        _previewTimer.Stop();
        _idleCheckTimer.Stop();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ClosePreview();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            UnregisterHotKey(handle, ToggleDeckHotkeyId);
        }

        // Remove o hook explicitamente: o deck é recriado a cada troca de lado/monitor,
        // e o delegate captura esta janela — não depender do teardown do HwndSource.
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;

        base.OnClosed(e);
    }

    private void PositionOnEdge()
    {
        var workArea = WorkArea;

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
        // Ressincroniza o flag da ViewModel: o deck é recriado a cada troca de
        // lado/monitor e a MainViewModel é a mesma. Sem isto, um deck novo nascia
        // em pill mas com IsExpanded=true, e o Expand() (que sai cedo nesse caso)
        // nunca mais abria o menu.
        _viewModel.IsExpanded = false;
        MenuPanel.BeginAnimation(OpacityProperty, null);
        MenuPanel.Opacity = 0;
        MenuPanel.RenderTransform = Transform.Identity;
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
        var workArea = WorkArea;
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

    /// <summary>Re-sincroniza o estado com a posição real do cursor enquanto o deck
    /// está expandido. Cobre MouseLeave perdido durante a animação de resize:
    /// exige 2 ticks consecutivos com o cursor fora para recolher (evita flutuação
    /// na borda enquanto a janela anima a largura). Usa as bounds exatas do deck e
    /// do preview (que pode ser reposicionado para caber na tela).</summary>
    private void OnIdleCheckTimerTick(object? sender, EventArgs e)
    {
        // Defesa extra contra timer zumbi: se a janela fechou expandida (o OnClosed
        // já para o timer, mas por garantia), o tick se encerra sozinho.
        if (!IsLoaded)
        {
            _idleCheckTimer.Stop();
            return;
        }

        bool overDeck = IsMouseOverDeckOnlyArea() || IsMouseOverPreviewArea();
        _mouseOverDeck = overDeck;

        if (!overDeck)
        {
            if (++_idleCheckMisses >= 2)
            {
                Collapse(); // para o próprio timer no Collapse
            }
        }
        else
        {
            _idleCheckMisses = 0;
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

            // O preview abre após o delay; a ancoragem real é calculada no
            // ShowPreview (posição mais confiável) e atualizada no scroll.
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

    private void OnNoteTabMouseLeave(object sender, MouseEventArgs e)
    {
        _previewTimer.Stop();
        _previewNote = null;
        _previewTab = null;

        // O mouse pode estar migrando para o preview (atravessando o gap entre
        // o deck e ele): só fecha o preview se ele realmente saiu da área
        // (deck + preview) — senão o preview some antes de o mouse alcançá-lo.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!_mouseOverDeck && !_mouseOverPreview && !IsMouseOverDeckArea())
            {
                ClosePreview();
            }
        });
    }

    /// <summary>Re-ancora o preview na aba hoverada quando o usuário rola as abas:
    /// o preview acompanha a aba em tempo real (em vez de ficar numa posição velha).
    /// Se a aba sair do viewport, o preview é fechado. Também atualiza as setas ↑/↓.</summary>
    private void OnTabsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateScrollIndicators();
        if (_previewWindow is null || _previewTab is not FrameworkElement tab || tab.DataContext != _previewNote)
        {
            return;
        }

        if (IsTabFullyScrolledOut(tab))
        {
            // A aba hoverada não está mais visível: preview não faz sentido.
            ClosePreview();
            return;
        }

        UpdatePreviewPosition(tab);
    }

    /// <summary>True se a aba está totalmente fora do viewport do deck (rolada
    /// para fora) — o preview deve fechar em vez de flutuar desconectado.</summary>
    private bool IsTabFullyScrolledOut(FrameworkElement tab)
    {
        var tabTop = GetTabScreenTop(tab);
        var tabBottom = tabTop + tab.ActualHeight;
        var viewportTop = Top + (TabsScrollViewer.TransformToAncestor(this).Transform(new Point(0, 0)).Y);
        var viewportBottom = viewportTop + TabsScrollViewer.ViewportHeight;
        return tabBottom <= viewportTop || tabTop >= viewportBottom;
    }

    /// <summary>Mostra as setas ↑/↓ quando há abas fora do viewport do deck
    /// (muitas notas): sinaliza visualmente que a lista rola.</summary>
    private void UpdateScrollIndicators()
    {
        if (TabsScrollViewer is null)
        {
            return;
        }

        bool canScrollUp = TabsScrollViewer.VerticalOffset > 0.5;
        bool canScrollDown = TabsScrollViewer.VerticalOffset < TabsScrollViewer.ScrollableHeight - 0.5;
        ScrollUpIndicator.Visibility = canScrollUp ? Visibility.Visible : Visibility.Collapsed;
        ScrollDownIndicator.Visibility = canScrollDown ? Visibility.Visible : Visibility.Collapsed;
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
        // O deck carrega só metadados: busca o corpo decriptado sob demanda (cacheado
        // no repositório) e passa direto ao preview — nunca escreve no objeto Note
        // compartilhado do deck, senão todo hover reteria o corpo decriptado nele.
        string body = _viewModel.Navigation.GetNoteBody(note.Id);

        // Reutiliza a janela já aberta (troca de conteúdo sem fechar/reabrir —
        // evita flicker ao passar rapidamente por várias abas).
        if (_previewWindow is not null && _previewNote == note)
        {
            _previewWindow.Refresh(note, body, _viewModel.Navigation);
            if (_previewTab is FrameworkElement currentTab)
            {
                UpdatePreviewPosition(currentTab);
            }
            return;
        }

        ClosePreview();

        if (_previewTab is FrameworkElement tab && tab.DataContext == note)
        {
            var (x, top, height) = ComputePreviewPosition(tab);
            _previewWindow = new NotePreviewWindow(note, body, _viewModel.Navigation, new Point(x, top), height);
            _previewWindow.MouseEnter += OnPreviewMouseEnter;
            _previewWindow.MouseLeave += OnPreviewMouseLeave;
            _previewWindow.Closed += OnPreviewClosed;
            _previewWindow.Show();
        }
    }

    /// <summary>Calcula a posição do preview ancorado à parte visível da aba
    /// hoverada: o topo acompanha o topo visível da aba, e se a aba está cortada
    /// pelo viewport do deck, alinha à borda visível (não some nem fica desalinhado).
    /// O clamp garante que o preview nunca saia da área de trabalho.</summary>
    private (double X, double Top, double Height) ComputePreviewPosition(FrameworkElement tab)
    {
        double tabTop = GetTabScreenTop(tab);
        double tabBottom = tabTop + tab.ActualHeight;

        // Parte da aba realmente visível no viewport (em coordenadas de tela).
        double viewportTop = Top + (TabsScrollViewer.TransformToAncestor(this).Transform(new Point(0, 0)).Y);
        double viewportBottom = viewportTop + TabsScrollViewer.ViewportHeight;
        double visibleTop = Math.Max(tabTop, viewportTop);
        double visibleBottom = Math.Min(tabBottom, viewportBottom);
        double anchor = visibleTop < visibleBottom ? visibleTop : tabTop;

        var workArea = WorkArea;
        double maxHeight = Math.Min(240, workArea.Bottom - anchor - 8);
        if (maxHeight < 120)
        {
            // Aba perto da borda inferior: sobe o preview para caber.
            anchor = Math.Max(workArea.Top, workArea.Bottom - 240 - 8);
            maxHeight = Math.Min(240, workArea.Bottom - anchor - 8);
        }

        double x = DockSide == DockSide.Right
            ? Left - PreviewWidth - PreviewGap
            : Left + ActualWidth + PreviewGap;

        return (x, Math.Max(workArea.Top, anchor), Math.Min(240, maxHeight));
    }

    /// <summary>Re-ancora o preview à aba hoverada (scroll, reposicionamento).</summary>
    private void UpdatePreviewPosition(FrameworkElement tab)
    {
        if (_previewWindow is null || TabsScrollViewer is null)
        {
            return;
        }

        var (x, top, height) = ComputePreviewPosition(tab);
        _previewWindow.Top = top;
        _previewWindow.Height = height;
        _previewWindow.Left = x;
    }

    private void OnPreviewMouseEnter(object sender, MouseEventArgs e)
    {
        _mouseOverPreview = true;
    }

    private void OnPreviewMouseLeave(object sender, MouseEventArgs e)
    {
        _mouseOverPreview = false;
        _previewTimer.Stop();

        // O MouseLeave do Window dispara com o cursor ainda na borda (a bounds do
        // deck inclui a área do preview), então o check único nunca vê o mouse
        // "fora" e o preview não fecha. Posterga o check para o cursor já ter
        // saído de verdade — mesmo padrão do OnMouseLeave do deck.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_mouseOverPreview || IsMouseOverPreviewArea())
            {
                return; // voltou para dentro do preview
            }

            ClosePreview();

            // Depois do ClosePreview, IsMouseOverDeckArea() mede só o deck
            // (o preview já foi removido da área extra).
            if (!_mouseOverDeck && !IsMouseOverDeckArea())
            {
                Collapse();
            }
        });
    }

    /// <summary>True se o cursor está sobre a janela do preview (não conta o deck).</summary>
    private bool IsMouseOverPreviewArea()
    {
        if (_previewWindow is null)
        {
            return false;
        }

        var cursor = GetCursorPositionInDips();
        if (cursor.X < 0)
        {
            return false;
        }

        return new Rect(_previewWindow.Left, _previewWindow.Top,
            _previewWindow.ActualWidth, _previewWindow.ActualHeight).Contains(cursor);
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
        // mesmo se o re-mede do NoteCount ainda não tiver rodado. A medição usa
        // MaxMenuHeight como constraint — abas rolam, botões fixos sempre visíveis.
        MenuPanel.Visibility = Visibility.Visible;
        MenuPanel.Measure(new Size(ExpandedWidth, MaxMenuHeight));
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

        // O menu não aparece seco enquanto a janela alarga: ele entra deslizando
        // da borda com fade, então a expansão lê como um movimento só.
        FadeMenuIn();

        _idleCheckTimer.Start();
        _idleCheckMisses = 0;
        AnimateWidth(ExpandedWidth, expanding: true);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateScrollIndicators);
    }

    private void Collapse()
    {
        if (!_viewModel.IsExpanded)
        {
            return;
        }

        _viewModel.IsExpanded = false;
        _previewTimer.Stop();
        _idleCheckTimer.Stop();
        ClosePreview();
        _previewNote = null;

        // Recolhe: o menu sai com fade (a janela afina junto) e a pill volta.
        FadeMenuOut();
        ExpandButton.Visibility = Visibility.Visible;
        ScrollUpIndicator.Visibility = Visibility.Collapsed;
        ScrollDownIndicator.Visibility = Visibility.Collapsed;
        UpdateEmptyState();

        AnimateWidth(CollapsedWidth, expanding: false);
    }

    /// <summary>Entrada do menu: fade + deslize curto a partir da borda onde o
    /// deck está encostado. Começa depois do primeiro frame da animação de
    /// largura, para o conteúdo entrar junto com o espaço que o recebe.</summary>
    private void FadeMenuIn()
    {
        if (!MotionService.Enabled)
        {
            MenuPanel.BeginAnimation(OpacityProperty, null);
            MenuPanel.Opacity = 1;
            MenuPanel.RenderTransform = Transform.Identity;
            return;
        }

        double from = DockSide == DockSide.Right ? 12 : -12;
        var slide = new TranslateTransform(from, 0);
        MenuPanel.RenderTransform = slide;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var begin = TimeSpan.FromMilliseconds(50);

        MenuPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170))
        {
            BeginTime = begin,
            EasingFunction = ease,
        });
        slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220))
        {
            BeginTime = begin,
            EasingFunction = ease,
        });
    }

    /// <summary>Saída do menu: fade rápido e só então colapsa. O colapso é adiado
    /// até o fade terminar, senão o menu some num frame (o que era metade da
    /// sensação de interface "agarrada"). Se o deck reabrir no meio, o guard
    /// impede que o colapso atrase a reabertura.</summary>
    private void FadeMenuOut()
    {
        if (!MotionService.Enabled)
        {
            MenuPanel.BeginAnimation(OpacityProperty, null);
            MenuPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            if (!_viewModel.IsExpanded)
            {
                MenuPanel.Visibility = Visibility.Collapsed;
            }
        };
        MenuPanel.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Anima a largura da janela e ajusta o Left para manter a borda fixa.
    /// Expandir é mais lento que recolher (a saída roda a ~70% da entrada): a
    /// chegada pede peso, a saída pede pressa.</summary>
    private void AnimateWidth(double targetWidth, bool expanding)
    {
        bool dockRight = DockSide == DockSide.Right;
        double edge = dockRight
            ? WorkArea.Right - targetWidth
            : WorkArea.Left;

        // Reduced motion: sem animação, o estado final é aplicado direto.
        if (!MotionService.Enabled)
        {
            BeginAnimation(WidthProperty, null);
            BeginAnimation(LeftProperty, null);
            Width = targetWidth;
            Left = edge;
            return;
        }

        // Expandir leva mais que recolher: chegar pede peso, sair pede pressa.
        var duration = TimeSpan.FromMilliseconds(expanding ? 190 : 120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var widthAnim = new DoubleAnimation(targetWidth, duration)
        {
            EasingFunction = ease,
        };
        BeginAnimation(WidthProperty, widthAnim);
        Width = targetWidth;

        var leftAnim = new DoubleAnimation(edge, duration)
        {
            EasingFunction = ease,
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

    /// <summary>True se o cursor está sobre a janela do deck (sem a área do preview).</summary>
    private bool IsMouseOverDeckOnlyArea()
    {
        var cursor = GetCursorPositionInDips();
        if (cursor.X < 0)
        {
            return false;
        }

        return new Rect(Left, Top, ActualWidth, ActualHeight).Contains(cursor);
    }
}
