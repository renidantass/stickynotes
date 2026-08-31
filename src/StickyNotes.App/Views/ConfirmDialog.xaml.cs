using System.Windows;
using System.Windows.Input;

namespace StickyNotes.Views;

/// <summary>Diálogo de confirmação no visual do app (substitui o MessageBox nativo).
/// Uso: <c>var result = new ConfirmDialog { Message = "..." }.ShowDialog(owner);</c></summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();
    }

    /// <summary>Mensagem exibida no corpo do diálogo.</summary>
    public string Message
    {
        get => MessageText.Text;
        set => MessageText.Text = value;
    }

    public bool Confirmed { get; private set; }

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
