using System.IO;

namespace StickyNotes.Services;

/// <summary>Resolve o diretório de dados do app em %LocalAppData%\StickyNotes.
/// O banco guarda o corpo cifrado com DPAPI de escopo CurrentUser: colocá-lo em
/// %AppData% (Roaming) fazia o perfil corporativo replicar o arquivo entre máquinas,
/// onde a decriptação quebra — e vazava os títulos em texto plano. Instalações
/// antigas são migradas uma única vez.</summary>
public static class AppPaths
{
    private static readonly string LocalDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyNotes");

    private static readonly string LegacyRoamingDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickyNotes");

    private static readonly string ResolvedDirectory = Resolve();

    public static string DataDirectory => ResolvedDirectory;

    public static string DatabasePath => Path.Combine(ResolvedDirectory, "notes.db");

    public static string SettingsPath => Path.Combine(ResolvedDirectory, "settings.json");

    private static string Resolve()
    {
        try
        {
            Directory.CreateDirectory(LocalDirectory);
            MigrateLegacyData();

            // Migração incompleta (ex.: disco cheio no meio do copy): mantém o
            // usuário no diretório antigo em vez de abrir um banco vazio novo.
            bool localHasDatabase = File.Exists(Path.Combine(LocalDirectory, "notes.db"));
            bool legacyHasDatabase = File.Exists(Path.Combine(LegacyRoamingDirectory, "notes.db"));
            if (!localHasDatabase && legacyHasDatabase)
            {
                AppLog.Warn("Migração de dados incompleta; continuando no diretório antigo.");
                return LegacyRoamingDirectory;
            }

            return LocalDirectory;
        }
        catch (Exception ex)
        {
            AppLog.Error("Falha ao preparar o diretório de dados.", ex);
            return LocalDirectory;
        }
    }

    /// <summary>Copia (nunca move de primeira) os arquivos do local antigo, confere o
    /// tamanho e só então remove o original — uma falha no meio nunca deixa o usuário
    /// sem o dado. Inclui -wal/-shm para não perder escritas ainda não consolidadas.</summary>
    private static void MigrateLegacyData()
    {
        if (!Directory.Exists(LegacyRoamingDirectory)
            || PathsReferToSameLocation())
        {
            return;
        }

        bool migrated = false;
        foreach (string name in new[] { "notes.db", "notes.db-wal", "notes.db-shm", "settings.json" })
        {
            try
            {
                string source = Path.Combine(LegacyRoamingDirectory, name);
                string target = Path.Combine(LocalDirectory, name);
                if (!File.Exists(source) || File.Exists(target))
                {
                    continue;
                }

                File.Copy(source, target, overwrite: false);
                if (new FileInfo(source).Length == new FileInfo(target).Length)
                {
                    File.Delete(source);
                    migrated = true;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Falha ao migrar '{name}' do diretório antigo.", ex);
            }
        }

        if (migrated)
        {
            AppLog.Info("Dados migrados de %AppData% (Roaming) para %LocalAppData%.");
        }
    }

    private static bool PathsReferToSameLocation() =>
        string.Equals(
            Path.GetFullPath(LocalDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(LegacyRoamingDirectory).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
