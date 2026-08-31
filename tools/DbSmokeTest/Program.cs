using System.IO;
using System.Threading;
using System.Windows;
using StickyNotes.Data;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.ViewModels;
using StickyNotes.Views;

var thread = new Thread(() =>
{
    try
    {
        var app = new StickyNotes.App();
        app.InitializeComponent();

        var settings = new SettingsService();
        var repo = new NoteRepository(settings.DatabasePath, new EncryptionService());
        var coordinator = new NotesCoordinator(repo);
        var confirmation = new ConfirmationService();
        var navigation = new NavigationService(repo, coordinator, confirmation, settings);
        var vm = new MainViewModel(settings);
        vm.SetServices(coordinator, navigation);

        // 1) Janela do mural abre com o banco real
        var all = new AllNotesWindow(repo, coordinator, navigation, confirmation);
        all.Resources.MergedDictionaries.Add(ThemeManager.Resources);
        all.Show();
        all.UpdateLayout();
        var avm = (AllNotesViewModel)all.DataContext;
        Console.WriteLine($"AllNotesWindow OK — notas visíveis: {avm.Notes.Count}, HasNotes: {avm.HasNotes}");
        all.Close();

        // 2) Ciclo do editor em banco temporário: cria, edita via VM, salva
        string tmp = Path.Combine(Path.GetTempPath(), $"sticky-smoke-{Guid.NewGuid():N}.db");
        var repo2 = new NoteRepository(tmp, new EncryptionService());
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

        // 4) ConfirmDialog carrega com os dois temas
        foreach (var theme in new[] { ThemeManager.LightResources, ThemeManager.DarkResources })
        {
            var dialog = new ConfirmDialog { Message = "teste" };
            dialog.Resources.MergedDictionaries.Clear();
            dialog.Resources.MergedDictionaries.Add(theme);
            dialog.Show();
            dialog.UpdateLayout();
            Console.WriteLine($"ConfirmDialog OK ({theme.Count} recursos) — {dialog.ActualWidth:F0}x{dialog.ActualHeight:F0}");
            dialog.Close();
        }

        bool ok = ordemOk && corpoOk && arquivou;
        Console.WriteLine(ok ? "SUCESSO" : "FALHA");

        try { File.Delete(tmp); } catch { }
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
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
