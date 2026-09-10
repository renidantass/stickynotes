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
    private const string ShortcutName = "StickyNotes.lnk";

    private readonly SettingsService _settingsService;
    private readonly Settings _settings;

    private DockSide _dockSide;
    private ThemePreference _themePreference;
    private bool _startWithWindows;
    private MonitorInfo? _selectedMonitor;

    /// <summary>Notifica o app que o lado do deck mudou (recriar/posicionar).</summary>
    public event EventHandler? DeckSideChanged;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _settings = settingsService.Load();
        _dockSide = _settings.DockSide;
        _themePreference = _settings.ThemePreference;
        _startWithWindows = _settings.StartWithWindows;

        Monitors = ScreenHelper.GetMonitors();
        _selectedMonitor = ScreenHelper.Resolve(_settings.MonitorDeviceName);

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

    /// <summary>Monitores disponíveis (o deck pode ser encostado em qualquer um).</summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; }

    /// <summary>Versão do binário exibida nas configurações: um bug report sem versão
    /// não pode ser correlacionado com o release instalado.</summary>
    public string AppVersion { get; } =
        $"v{typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "?"}";

    /// <summary>Verdadeiro quando há mais de um monitor (a seleção só faz sentido aí).</summary>
    public bool HasMultipleMonitors => Monitors.Count > 1;

    /// <summary>Monitor selecionado para o deck.</summary>
    public MonitorInfo? SelectedMonitor
    {
        get => _selectedMonitor;
        set => SetProperty(ref _selectedMonitor, value);
    }

    public RelayCommand SaveCommand { get; }

    private void Save()
    {
        // Valores antigos ANTES de salvar (o Save atualiza o cache do service).
        var oldSettings = _settingsService.Load();
        var oldDockSide = oldSettings.DockSide;
        var oldMonitor = oldSettings.MonitorDeviceName;

        _settings.DockSide = _dockSide;
        _settings.ThemePreference = _themePreference;
        _settings.StartWithWindows = _startWithWindows;
        _settings.MonitorDeviceName = _selectedMonitor?.DeviceName ?? "";
        _settingsService.Save(_settings);

        // Aplica em runtime: tema imediato; deck re-posicionado se lado/monitor mudou.
        ThemeManager.ApplyPreference(_themePreference);
        ApplyStartWithWindows();
        if (_dockSide != oldDockSide || _settings.MonitorDeviceName != oldMonitor)
        {
            DeckSideChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Cria/remove um atalho na pasta Inicializar (shell:startup) do usuário
    /// (iniciar com o Windows). Usa o caminho do executável atual; em Debug aponta para o exe da build.</summary>
    private void ApplyStartWithWindows()
    {
        try
        {
            // Migração: instalações antigas usavam a Run key do registro.
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                key?.DeleteValue(RunValueName, throwOnMissingValue: false);
            }

            string linkPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);
            if (_startWithWindows)
            {
                string exe = Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrEmpty(exe))
                {
                    return;
                }

                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null)
                {
                    return;
                }

                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(linkPath);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
                shortcut.Save();
            }
            else
            {
                File.Delete(linkPath);
            }
        }
        catch (IOException)
        {
            // Sem acesso à pasta/atalho: ignora (a preferência fica salva no JSON).
        }
        catch (UnauthorizedAccessException)
        {
            // Sem permissão: ignora.
        }
    }
}
