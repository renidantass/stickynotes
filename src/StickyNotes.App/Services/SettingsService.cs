using System.IO;
using System.Text.Json;

namespace StickyNotes.Services;

public enum DockSide { Left, Right }

/// <summary>Tema do app: segue o sistema ou força claro/escuro.</summary>
public enum ThemePreference { System, Light, Dark }

/// <summary>Configurações persistentes do app (JSON em %AppData%\StickyNotes).</summary>
public class Settings
{
    public DockSide DockSide { get; set; } = DockSide.Right;
    public ThemePreference ThemePreference { get; set; } = ThemePreference.System;
    public bool StartWithWindows { get; set; }

    /// <summary>DeviceName do monitor onde o deck fica encostado (ex. "\\.\DISPLAY1").
    /// Vazio = monitor primário.</summary>
    public string MonitorDeviceName { get; set; } = "";
}

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private Settings? _cached;

    public SettingsService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickyNotes");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.json");
    }

    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickyNotes");

    public string DatabasePath => Path.Combine(DataDirectory, "notes.db");

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

        _cached = settings;
        return settings;
    }

    public void Save(Settings settings)
    {
        _cached = settings;
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
