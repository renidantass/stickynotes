using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Data.Sqlite;
using StickyNotes.Data;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.ViewModels;
using StickyNotes.Views;

int exitCode = 1;
var thread = new Thread(() =>
{
    try
    {
        var app = new StickyNotes.App();
        app.InitializeComponent();

        var settings = new SettingsService();
        using var repo = new NoteRepository(settings.DatabasePath, new EncryptionService());
        var coordinator = new NotesCoordinator(repo);
        var confirmation = new ConfirmationService();
        var navigation = new NavigationService(repo, coordinator, confirmation, settings);
        var vm = new MainViewModel(settings);
        vm.SetServices(coordinator, navigation);

        // 0) Enumeração de monitores (ScreenHelper) funciona
        var monitors = ScreenHelper.GetMonitors();
        var resolved = ScreenHelper.Resolve(settings.Load().MonitorDeviceName);
        Console.WriteLine($"Monitores OK — {monitors.Count} monitor(es): " +
            string.Join(", ", monitors.Select(m => $"{m.DisplayName} [{m.DeviceName}]")));
        Console.WriteLine($"Resolve OK — {resolved.DisplayName}, workarea {resolved.WorkArea.Width:F0}x{resolved.WorkArea.Height:F0}");

        // 1) Janela do mural abre com o banco real
        var all = new AllNotesWindow(repo, coordinator, navigation, confirmation);
        all.Resources.MergedDictionaries.Add(ThemeManager.Resources);
        all.Show();
        all.UpdateLayout();
        // A carga do mural é adiada para depois do primeiro frame (para a janela
        // aparecer antes de decriptar tudo): bombeia a fila para o teste enxergar
        // o estado já carregado.
        all.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        all.UpdateLayout();
        var avm = (AllNotesViewModel)all.DataContext;
        Console.WriteLine($"AllNotesWindow OK — notas visíveis: {avm.Notes.Count}, estado vazio: {avm.ShowEmptyState}");
        all.Close();

        // 2) Ciclo do editor em banco temporário: cria, edita via VM, salva
        string tmp = Path.Combine(Path.GetTempPath(), $"sticky-smoke-{Guid.NewGuid():N}.db");
        using var repo2 = new NoteRepository(tmp, new EncryptionService());
        var coord2 = new NotesCoordinator(repo2);

        var n1 = new Note { Title = "A" };
        n1.Id = repo2.Insert(n1);
        Thread.Sleep(5);
        var n2 = new Note { Title = "B" };
        n2.Id = repo2.Insert(n2);

        coord2.Reload();
        var editor = new NoteEditorViewModel(n2, coord2);
        editor.Title = "B editada";
        editor.Body = "corpo novo";
        editor.Save();

        var reloaded = coord2.Notes;
        bool ordemOk = reloaded[0].Id == n2.Id && reloaded[1].Id == n1.Id; // B (recente) primeiro
        bool corpoOk = repo2.GetAll().First(n => n.Id == n2.Id).Body == "corpo novo";
        Console.WriteLine($"Editor OK — ordem estável: {ordemOk}, corpo salvo: {corpoOk}");

        // 3) Arquivar via coordinator some do deck
        coord2.ToggleArchive(n2);
        bool arquivou = coord2.Notes.Count == 1 && coord2.Notes[0].Id == n1.Id;
        Console.WriteLine($"Arquivar OK — deck com {coord2.Notes.Count} nota(s): {arquivou}");

        // 3b) Archive() do editor força arquivamento (bug: antes negava e não arquivava)
        var n3 = new Note { Title = "C" };
        n3.Id = repo2.Insert(n3);
        coord2.Reload();
        var editor3 = new NoteEditorViewModel(coord2.Notes.First(x => x.Id == n3.Id), coord2);
        editor3.Archive();
        bool archiveForcado = repo2.GetAll().First(x => x.Id == n3.Id).IsArchived
            && coord2.Notes.All(x => x.Id != n3.Id);
        Console.WriteLine($"Archive() do editor OK — arquivou: {archiveForcado}");

        // 3c) Salvar uma nota já excluída não "grava no vazio" silenciosamente
        var n4 = new Note { Title = "D" };
        n4.Id = repo2.Insert(n4);
        coord2.Reload();
        var editor4 = new NoteEditorViewModel(coord2.Notes.First(x => x.Id == n4.Id), coord2);
        coord2.Delete(n4);
        bool saveDetectouExclusao = !editor4.Save();
        Console.WriteLine($"Salvar nota excluída OK — retornou falha: {saveDetectouExclusao}");

        // 3d) Editar o conteúdo NÃO desarquiva (nota arquivada em outra superfície)
        var n5 = new Note { Title = "E" };
        n5.Id = repo2.Insert(n5);
        coord2.Reload();
        var editor5 = new NoteEditorViewModel(coord2.Notes.First(x => x.Id == n5.Id), coord2);
        coord2.SetArchived(n5, true);
        editor5.Save();
        bool naoRessuscitou = repo2.GetAll().First(x => x.Id == n5.Id).IsArchived;
        Console.WriteLine($"Arquivamento preservado ao editar OK — arquivada: {naoRessuscitou}");

        // 4) Migração de schema: banco no formato antigo (sem user_version/version)
        string legacyPath = Path.Combine(Path.GetTempPath(), $"sticky-legacy-{Guid.NewGuid():N}.db");
        using (var legacy = new SqliteConnection($"Data Source={legacyPath}"))
        {
            legacy.Open();
            using var cmd = legacy.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE notes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL DEFAULT '',
                    body_cipher TEXT NOT NULL DEFAULT '',
                    color TEXT NOT NULL DEFAULT 'yellow',
                    is_archived INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                INSERT INTO notes (title, body_cipher, color, is_archived, created_at, updated_at)
                VALUES ('legada', '', 'blue', 0, '2024-01-01T00:00:00.0000000', '2024-01-01T00:00:00.0000000');
                """;
            cmd.ExecuteNonQuery();
        }

        bool migrouLegado;
        using (var migrado = new NoteRepository(legacyPath, new EncryptionService()))
        {
            var notas = migrado.GetAll();
            bool preservou = notas.Count == 1 && notas[0].Title == "legada";
            var editada = notas[0];
            editada.Title = "editada";
            bool salvou = migrado.Update(editada) && editada.Version == 1;
            migrouLegado = preservou && salvou;
        }
        Console.WriteLine($"Migração de schema OK — {migrouLegado}");

        // 5) ConfirmDialog carrega com os três temas (claro, escuro, alto contraste)
        foreach (var theme in new[] { ThemeManager.LightResources, ThemeManager.DarkResources, ThemeManager.HighContrastResources })
        {
            var dialog = new ConfirmDialog { Message = "teste" };
            dialog.Resources.MergedDictionaries.Clear();
            dialog.Resources.MergedDictionaries.Add(theme);
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        }
        Console.WriteLine("ConfirmDialog OK — 3 temas carregados");

        bool ok = ordemOk && corpoOk && arquivou && archiveForcado && saveDetectouExclusao
            && naoRessuscitou && migrouLegado;
        Console.WriteLine(ok ? "SUCESSO" : "FALHA");

        try { File.Delete(tmp); } catch { }
        try { File.Delete(legacyPath); } catch { }

        // Propaga o resultado para o CI: antes o processo saía sempre com 0 e o
        // step "Run smoke test" ficava verde mesmo com falha real.
        exitCode = ok ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FALHA: {ex.Message}");
        var inner = ex.InnerException;
        while (inner is not null)
        {
            Console.WriteLine($"  -> {inner.Message}");
            inner = inner.InnerException;
        }

        exitCode = 1;
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

// Define o exit code sem Environment.Exit: assim os `using` do bloco acima
// (dispose do banco + checkpoint do WAL) executam normalmente.
Environment.ExitCode = exitCode;
