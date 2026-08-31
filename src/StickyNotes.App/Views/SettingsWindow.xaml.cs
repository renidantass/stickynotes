using System.Windows;
using System.Windows.Input;
using StickyNotes.Services;
using StickyNotes.ViewModels;

namespace StickyNotes.Views;

/// <summary>Tela de configurações do app. Salva as preferências e as aplica em
/// runtime (tema imediato, deck re-posicionado, topmost).</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsService settingsService)
    {
        AppWindowSetup.ApplyTheme(this);
        InitializeComponent();

        _viewModel = new SettingsViewModel(settingsService);
        _viewModel.DeckSideChanged += (_, _) => DeckSideChanged?.Invoke(this, EventArgs.Empty);
        DataContext = _viewModel;
    }

    /// <summary>Dispara quando o lado do deck mudou (o App recria o deck).</summary>
    public event EventHandler? DeckSideChanged;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        AppWindowSetup.HideFromAltTab(this);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
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
