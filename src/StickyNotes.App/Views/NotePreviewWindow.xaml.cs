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

        // Entrada suave: sobe um pouco e assenta, respeitando reduced motion
        if (MotionService.Enabled)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            PreviewBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            var transform = new TransformGroup();
            var lift = new TranslateTransform(0, 8);
            var scale = new ScaleTransform(0.98, 0.98);
            transform.Children.Add(scale);
            transform.Children.Add(lift);
            PreviewBorder.RenderTransform = transform;

            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            lift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });

            Opacity = 0;
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        }
    }

    private void ApplyColor(string color)
    {
        // Mesmo material da nota: gradiente de papel + grão + aresta da cor.
        var brush = NoteColorBrush.GetGradient(color);
        PreviewBorder.Background = brush;
        Background = brush; // o fundo da janela cobre a área do chrome (cantos recortados)
        PreviewBorder.BorderBrush = NoteColorBrush.GetEdge(color);
        GrainLayer.Background = NoteColorBrush.GetGrain();
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
