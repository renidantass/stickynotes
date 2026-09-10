using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StickyNotes.Services;

namespace StickyNotes.Views;

/// <summary>Diálogo de confirmação no visual do app (substitui o MessageBox nativo).
/// Uso: <c>var result = new ConfirmDialog { Message = "..." }.ShowDialog(owner);</c></summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Mensagem exibida no corpo do diálogo.</summary>
    public string Message
    {
        get => MessageText.Text;
        set => MessageText.Text = value;
    }

    public bool Confirmed { get; private set; }

    /// <summary>O plano de atenção entra crescendo desde o centro: chega de onde
    /// o olho já está, em vez de aparecer seco.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!MotionService.Enabled)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        DialogSurface.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(0.96, 0.96);
        DialogSurface.RenderTransform = scale;
        var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }
}
