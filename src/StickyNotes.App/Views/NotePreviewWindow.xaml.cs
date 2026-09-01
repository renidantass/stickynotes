using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.ViewModels;

namespace StickyNotes.Views;

/// <summary>Preview da nota exibido ao passar o mouse sobre uma aba no deck.
/// Um clique abre a nota para edição.</summary>
public partial class NotePreviewWindow : Window
{
    private Note _note;
    private INavigationService _navigation;

    public NotePreviewWindow(Note note, string body, INavigationService navigation, Point position, double maxHeight)
    {
        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();

        _note = note;
        _navigation = navigation;

        TitleText.Text = note.Title;
        BodyText.Text = body;
        ApplyColor(note.Color);

        Height = Math.Min(240, maxHeight);
        Width = 280;
        Left = position.X;
        Top = position.Y;

        // Entrada suave: fade + leve deslize (respeita reduced motion)
        if (MotionService.Enabled)
        {
            Opacity = 0;
            var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }

    private void ApplyColor(string color)
    {
        var brush = NoteColorBrush.Get(color);
        PreviewBorder.Background = brush;
        Background = brush; // o fundo da janela cobre a área do chrome (cantos recortados)
        TapeBorder.Background = NoteColorBrush.GetTape();
        FoldPath.Fill = NoteColorBrush.GetFold(color);
    }

    /// <summary>Atualiza o conteúdo da janela para outra nota (sem recriar a janela) —
    /// usado quando o hover muda de aba com o preview já aberto, evitando flicker.
    /// O corpo chega pronto (decriptado sob demanda pelo deck): nunca lê/escreve
    /// note.Body no objeto compartilhado.</summary>
    public void Refresh(Note note, string body, INavigationService navigation)
    {
        _note = note;
        _navigation = navigation;
        TitleText.Text = note.Title;
        BodyText.Text = body;
        ApplyColor(note.Color);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppWindowSetup.HideFromAltTab(this);
    }

    private void OnPreviewClick(object sender, MouseButtonEventArgs e)
    {
        // Clique no preview abre a nota para edição; fecha o preview para o
        // segundo clique de um duplo clique não abrir de novo (o OpenNote já
        // deduplica, mas fechar evita o flicker).
        _navigation.OpenNote(_note);
        Close();
    }
}
