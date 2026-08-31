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
    private readonly Note _note;
    private readonly INavigationService _navigation;

    public NotePreviewWindow(Note note, INavigationService navigation, Point position, double maxHeight)
    {
        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();

        _note = note;
        _navigation = navigation;

        TitleText.Text = note.Title;
        BodyText.Text = note.Body;
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
        TapeBorder.Background = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        FoldPath.Fill = new SolidColorBrush(NoteColorBrush.Darken(color));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppWindowSetup.HideFromAltTab(this);
    }

    private void OnPreviewClick(object sender, MouseButtonEventArgs e)
    {
        // Clique no preview abre a nota para edição
        _navigation.OpenNote(_note);
    }
}
