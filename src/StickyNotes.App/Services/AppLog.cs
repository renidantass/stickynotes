using System.Diagnostics;
using System.IO;
using System.Text;

namespace StickyNotes.Services;

/// <summary>Log mínimo em arquivo (%LocalAppData%\StickyNotes\logs). Sem dependências:
/// é o único rastro de diagnóstico em produção, já que o app é distribuído como zip
/// e não tem telemetria. Nunca lança — log não pode derrubar o app.</summary>
public static class AppLog
{
    private const long MaxBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StickyNotes", "logs");

    private static readonly string LogPath = Path.Combine(LogDirectory, "sticky-notes.log");

    public static string FilePath => LogPath;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        Debug.WriteLine(line);

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                RotateIfNeeded();
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Sem acesso ao arquivo de log: ignora (o Debug.WriteLine acima já ajuda em dev).
        }
    }

    /// <summary>Rotaciona para um único backup ao estourar o teto: evita o log crescer
    /// sem limite num app que fica residente por semanas.</summary>
    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        string backup = LogPath + ".1";
        File.Delete(backup);
        File.Move(LogPath, backup);
    }
}
