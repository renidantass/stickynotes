using System.IO;
using Microsoft.Win32;
using StickyNotes.Services;

namespace StickyNotes.ViewModels;

/// <summary>ViewModel da tela de configurações. Edita uma cópia e salva tudo
/// de uma vez; as mudanças são aplicadas em runtime (tema, lado do deck, iniciar com Windows).</summary>
public class SettingsViewModel : ViewModelBase
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "StickyNotes";

    private readonly SettingsService _settingsService;
    private readonly Settings _settings;

    private DockSide _dockSide;
    private ThemePreference _themePreference;
    private bool _startWithWindows;

    /// <summary>Notifica o app que o lado do deck mudou (recriar/posicionar).</summary>
    public event EventHandler? DeckSideChanged;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Load();
        _dockSide = _settings.DockSide;
        _themePreference = _settings.ThemePreference;
        _startWithWindows = _settings.StartWithWindows;

        SaveCommand = new RelayCommand(() => Save());
    }

    public DockSide DockSide
    {
        get => _dockSide;
        set
        {
            if (SetProperty(ref _dockSide, value))
            {
                OnPropertyChanged(nameof(DockSideLeft));
                OnPropertyChanged(nameof(DockSideRight));
            }
        }
    }

    public bool DockSideLeft
    {
        get => _dockSide == DockSide.Left;
        set
        {
            if (value)
            {
                DockSide = DockSide.Left;
            }
        }
    }

    public bool DockSideRight
    {
        get => _dockSide == DockSide.Right;
        set
        {
            if (value)
            {
                DockSide = DockSide.Right;
            }
        }
    }

    public ThemePreference ThemePreference
    {
        get => _themePreference;
        set
        {
            if (SetProperty(ref _themePreference, value))
            {
                OnPropertyChanged(nameof(ThemeSystem));
                OnPropertyChanged(nameof(ThemeLight));
                OnPropertyChanged(nameof(ThemeDark));
            }
        }
    }

    public bool ThemeSystem
    {
        get => _themePreference == ThemePreference.System;
        set
        {
            if (value)
            {
                ThemePreference = ThemePreference.System;
            }
        }
    }

    public bool ThemeLight
    {
        get => _themePreference == ThemePreference.Light;
        set
        {
            if (value)
            {
                ThemePreference = ThemePreference.Light;
            }
        }
    }

    public bool ThemeDark
    {
        get => _themePreference == ThemePreference.Dark;
        set
        {
            if (value)
            {
                ThemePreference = ThemePreference.Dark;
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public RelayCommand SaveCommand { get; }

    private void Save()
    {
        // Lado antigo ANTES de salvar (o Save atualiza o cache do service).
        var oldDockSide = _settingsService.Load().DockSide;

        _settings.DockSide = _dockSide;
        _settings.ThemePreference = _themePreference;
        _settings.StartWithWindows = _startWithWindows;
        _settingsService.Save(_settings);

        // Aplica em runtime: tema imediato; lado do deck notifica o App para recriar.
        ThemeManager.ApplyPreference(_themePreference);
        ApplyStartWithWindows();
        if (_dockSide != oldDockSide)
        {
            DeckSideChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Registra/remove o app no Run key do registro (iniciar com o Windows).
    /// Usa o caminho do executável atual; em Debug aponta para o exe da build.</summary>
    private void ApplyStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (_startWithWindows)
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrEmpty(exe))
                {
                    key.SetValue(RunValueName, $"\"{exe}\"");
                }
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (IOException)
        {
            // Sem acesso ao registro: ignora (a preferência fica salva no JSON).
        }
        catch (UnauthorizedAccessException)
        {
            // Sem permissão: ignora.
        }
    }
}
