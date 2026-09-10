using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StickyNotes.Services;

public enum DockSide { Left, Right }

/// <summary>Tema do app: segue o sistema ou força claro/escuro.</summary>
public enum ThemePreference { System, Light, Dark }

/// <summary>Configurações persistentes do app (JSON em %LocalAppData%\StickyNotes).</summary>
public class Settings
{
    /// <summary>Versão do schema do arquivo — permite migrar/descartar config antiga
    /// em vez de reinterpretar campos silenciosamente.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DockSide DockSide { get; set; } = DockSide.Right;
    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;
    public bool StartWithWindows { get; set; }

    /// <summary>DeviceName do monitor onde o deck fica encostado (ex. "\\.\DISPLAY1").
    /// Vazio = monitor primário.</summary>
    public string MonitorDeviceName { get; set; } = "";
}

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Enum como nome: reordenar/inserir valores no enum não reinterpreta a
        // config do usuário (antes, números crus eram aceitos sem validação).
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private Settings? _cached;

    public SettingsService()
    {
        _filePath = AppPaths.SettingsPath;
    }

    public string DatabasePath => AppPaths.DatabasePath;

    /// <summary>Lê as configurações com cache em memória: o JSON do disco é lido
    /// apenas uma vez por processo (evita I/O a cada abertura de nota).</summary>
    public Settings Load()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        Settings settings = new();
        try
        {
            if (File.Exists(_filePath))
            {
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(_filePath))
                           ?? new Settings();
            }
        }
        catch (IOException)
        {
            // Arquivo em uso/lock momentâneo: recomeça com padrões.
        }
        catch (JsonException)
        {
            // Config corrompida: recomeça com padrões.
        }
        catch (UnauthorizedAccessException)
        {
            // Sem permissão de leitura: recomeça com padrões.
        }

        _cached = Sanitize(settings);
        return _cached;
    }

    /// <summary>Escrita atômica (temp + move): uma queda no meio não deixa o
    /// settings.json truncado.</summary>
    public bool Save(Settings settings)
    {
        _cached = settings;
        try
        {
            string temp = _filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temp, _filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Falha ao salvar as configurações.", ex);
            return false;
        }
    }

    /// <summary>Descarta valores fora do domínio (enum inválido vindo de JSON editado
    /// à mão) em vez de propagar lixo para a UI.</summary>
    private static Settings Sanitize(Settings settings)
    {
        if (!Enum.IsDefined(settings.DockSide))
        {
            settings.DockSide = DockSide.Right;
        }

        if (!Enum.IsDefined(settings.ThemePreference))
        {
            settings.ThemePreference = ThemePreference.System;
        }

        settings.MonitorDeviceName ??= string.Empty;
        settings.Version = Settings.CurrentVersion;
        return settings;
    }
}
