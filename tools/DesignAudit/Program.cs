using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StickyNotes.Data;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.ViewModels;
using StickyNotes.Views;

// Auditoria de consistência visual: renderiza TODAS as janelas nos DOIS temas,
// salva os PNGs e aponta incoerências automaticamente.
//
// O que ela procura:
//  - No tema ESCURO, uma faixa larga e CLARA no chrome = fundo padrão do WPF
//    vazando ou controle nativo sem tema (as "manchas brancas").
//  - No tema CLARO, a incoerência oposta: faixa larga e ESCURA no chrome.
//  - Tokens que pintam SOBRE o papel devem ser iguais nos dois temas, porque o
//    papel é sempre claro.
//  - Todo recurso declarado nos templates do Styles.xaml tem que existir no tema
//    (um DynamicResource ausente falha silenciosamente em runtime).
//
// Uso: dotnet run --project tools/DesignAudit <pasta-de-saida>

string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "stickynotes-audit");
Directory.CreateDirectory(outDir);

var failures = new List<string>();
int exitCode = 1;

var thread = new Thread(() =>
{
    try
    {
        var app = new StickyNotes.App();
        app.InitializeComponent();

        string dbDir = Path.Combine(Path.GetTempPath(), $"sticky-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dbDir);
        using var repo = new NoteRepository(Path.Combine(dbDir, "notes.db"), new EncryptionService());

        string[] colors = ["yellow", "pink", "blue", "green", "purple", "orange"];
        string[] titles = ["Lista de compras do fim de semana", "Ideia de produto", "Senha do wifi", "Reunião", "Rascunho", "Lembrete"];
        for (int i = 0; i < colors.Length; i++)
        {
            var n = new Note
            {
                Title = titles[i],
                Body = "Comprar café, leite e pão. Verificar se o filtro precisa ser trocado.",
                Color = colors[i],
            };
            n.Id = repo.Insert(n);
        }

        var coordinator = new NotesCoordinator(repo);
        var confirmation = new ConfirmationService();
        var settings = new SettingsService();
        var navigation = new NavigationService(repo, coordinator, confirmation, settings);
        var vm = new MainViewModel(settings);
        vm.SetServices(coordinator, navigation);
        var rect = new Rect(0, 0, 1920, 1032);

        // expectPaper: a janela mostra o papel das notas, que é claro de
        // propósito nos dois temas — não serve como sinal de vazamento.
        Check("mural", () => new AllNotesWindow(repo, coordinator, navigation, confirmation), expectPaper: true);
        Check("nota", () => new NoteWindow(coordinator.Notes.First(n => n.Color == "yellow"), coordinator, confirmation, DockSide.Left, rect), expectPaper: true);
        Check("preview", () =>
        {
            var note = coordinator.Notes.First(n => n.Color == "blue");
            return new NotePreviewWindow(note, repo.GetBody(note.Id), navigation, new Point(0, 0), 240);
        }, expectPaper: true);
        Check("config", () => new SettingsWindow(settings), expectPaper: false);
        Check("dialogo", () => new ConfirmDialog { Message = "A nota \"Lista de compras\" será excluída permanentemente." }, expectPaper: false);
        CheckDeck(vm);

        Console.WriteLine();
        CheckTokensPaintOverPaper();
        CheckResourcesExistInBothThemes();

        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("AUDITORIA LIMPA");
            exitCode = 0;
        }
        else
        {
            Console.WriteLine($"AUDITORIA ENCONTROU {failures.Count} PROBLEMA(S):");
            foreach (var f in failures)
            {
                Console.WriteLine($"  - {f}");
            }

            exitCode = 1;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FALHA: {ex}");
        exitCode = 1;
    }
});
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
Environment.ExitCode = exitCode;

void CheckDeck(MainViewModel vm)
{
    foreach (var theme in Themes())
    {
        ThemeManager.ApplyPreference(Preference(theme));
        vm.IsExpanded = false;
        var w = new DeckWindow(vm) { DockSide = DockSide.Left, WorkArea = new Rect(0, 0, 1920, 1032) };
        typeof(DeckWindow).GetMethod("Expand", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
        Render(w, $"deck-{Suffix(theme)}", expectPaper: true);
    }
}

void Check(string name, Func<Window> factory, bool expectPaper)
{
    foreach (var theme in Themes())
    {
        ThemeManager.ApplyPreference(Preference(theme));
        Render(factory(), $"{name}-{Suffix(theme)}", expectPaper);
    }
}

static AppTheme[] Themes() => [AppTheme.Light, AppTheme.Dark];

static ThemePreference Preference(AppTheme t) => t == AppTheme.Light ? ThemePreference.Light : ThemePreference.Dark;

static string Suffix(AppTheme t) => t == AppTheme.Light ? "light" : "dark";

void Render(Window window, string name, bool expectPaper)
{
    window.WindowStartupLocation = WindowStartupLocation.Manual;
    window.Left = -4000;
    window.Top = 0;
    window.Show();

    // Espera as animações de entrada assentarem de verdade: uma captura no meio
    // do fade mede o alpha da animação, não a cor final da interface.
    Settle(window);

    int w = (int)Math.Ceiling(window.ActualWidth);
    int h = (int)Math.Ceiling(window.ActualHeight);
    if (w <= 0 || h <= 0)
    {
        failures.Add($"{name}: janela sem tamanho ({w}x{h})");
        window.Close();
        return;
    }

    var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
    rtb.Render(window);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(rtb));
    using (var fs = File.Create(Path.Combine(outDir, name + ".png")))
    {
        encoder.Save(fs);
    }

    int stride = w * 4;
    var pixels = new byte[h * stride];
    rtb.CopyPixels(pixels, stride, 0);

    bool darkTheme = ThemeManager.Current == AppTheme.Dark;
    var suspicious = new List<string>();
    int runStart = -1;

    for (int y = 0; y <= h; y++)
    {
        bool dense = false;
        if (y < h)
        {
            int hits = 0;
            for (int x = 0; x < w; x++)
            {
                int i = y * stride + x * 4;
                // Pbgra32: desfaz o pré-multiplicado para medir a cor real.
                byte a = pixels[i + 3];
                if (a == 0)
                {
                    continue;
                }

                double scale = 255.0 / a;
                double rr = Math.Min(255, pixels[i + 2] * scale);
                double gg = Math.Min(255, pixels[i + 1] * scale);
                double bb = Math.Min(255, pixels[i] * scale);
                double lum = 0.299 * rr + 0.587 * gg + 0.114 * bb;

                // No escuro, procura faixa CLARA; no claro, faixa ESCURA.
                if (darkTheme ? lum >= 185 : lum <= 70)
                {
                    hits++;
                }
            }

            dense = hits > w * 0.40;
        }

        if (dense && runStart < 0)
        {
            runStart = y;
        }
        else if (!dense && runStart >= 0)
        {
            if (y - runStart >= 6)
            {
                int midY = (runStart + y - 1) / 2;
                int midI = midY * stride + (w / 2) * 4;
                byte a = pixels[midI + 3];
                double scale = a == 0 ? 1 : 255.0 / a;
                int cr = (int)Math.Min(255, pixels[midI + 2] * scale);
                int cg = (int)Math.Min(255, pixels[midI + 1] * scale);
                int cb = (int)Math.Min(255, pixels[midI] * scale);
                var color = $"#{cr:X2}{cg:X2}{cb:X2}";
                suspicious.Add($"y={runStart}..{y - 1}({color})");
                if (!expectPaper)
                {
                    failures.Add($"{name}: faixa {(darkTheme ? "CLARA" : "ESCURA")} de {y - runStart}px " +
                                 $"em y={runStart}..{y - 1} ({color}) no chrome — {w}px de largura");
                }
            }

            runStart = -1;
        }
    }

    string bands = suspicious.Count == 0 ? "limpo" : string.Join(" ", suspicious);
    Console.WriteLine($"  {name,-16} {w,4}x{h,-4} {bands}");
    window.Close();
}

static void Settle(Window window)
{
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 650)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        Thread.Sleep(15);
    }

    window.UpdateLayout();
}

void CheckTokensPaintOverPaper()
{
    string[] paperTokens =
    [
        "NoteTextPrimaryBrush", "NoteTextSecondaryBrush", "NoteOverlayBrush",
        "NoteOverlayForegroundBrush", "NoteOverlayDangerBrush",
    ];

    Console.WriteLine("  tokens sobre o papel (devem ser iguais nos dois temas):");
    foreach (string token in paperTokens)
    {
        string light = Resolve(AppTheme.Light, token);
        string dark = Resolve(AppTheme.Dark, token);
        bool same = light == dark;
        Console.WriteLine($"    {token,-30} claro={light} escuro={dark} {(same ? "OK" : "DIVERGE")}");
        if (!same)
        {
            failures.Add($"{token} diverge entre os temas ({light} vs {dark}) e pinta sobre o papel");
        }
    }
}

void CheckResourcesExistInBothThemes()
{
    // Percorre os DynamicResource usados nos templates e confere que o tema
    // define todos: um DynamicResource ausente falha em silêncio (o binding
    // simplesmente não pinta nada), que é como um bug desses passa despercebido.
    string? stylesPath = FindUpward(Path.Combine("src", "StickyNotes.App", "Resources", "Styles.xaml"));
    if (stylesPath is null)
    {
        Console.WriteLine("  (Styles.xaml não encontrado para conferir os recursos)");
        return;
    }

    var text = File.ReadAllText(stylesPath);
    var used = System.Text.RegularExpressions.Regex
        .Matches(text, @"DynamicResource\s+([A-Za-z0-9_]+)")
        .Select(m => m.Groups[1].Value)
        .Distinct()
        .OrderBy(x => x)
        .ToList();

    Console.WriteLine($"  recursos exigidos pelos templates: {used.Count}");
    foreach (var theme in Themes())
    {
        ThemeManager.ApplyPreference(Preference(theme));
        var missing = used.Where(r => ThemeManager.Resources[r] is null).ToList();
        if (missing.Count == 0)
        {
            Console.WriteLine($"    tema {Suffix(theme),-5} OK");
        }
        else
        {
            foreach (var m in missing)
            {
                failures.Add($"recurso '{m}' ausente no tema {Suffix(theme)} (template exigiria e nada pintaria)");
            }

            Console.WriteLine($"    tema {Suffix(theme),-5} FALTANDO: {string.Join(", ", missing)}");
        }
    }
}

/// <summary>Sobe a partir do diretório do executável até achar o arquivo: o
/// caminho até a raiz do repositório não é fixo (muda com a configuração/runtime
/// do build), então procurar é mais confiável do que contar níveis com "..".</summary>
static string? FindUpward(string relativePath)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        string candidate = Path.Combine(dir.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        dir = dir.Parent;
    }

    return null;
}

string Resolve(AppTheme theme, string token)
{
    ThemeManager.ApplyPreference(Preference(theme));
    return ThemeManager.Resources[token] switch
    {
        SolidColorBrush b => b.Color.ToString(),
        LinearGradientBrush g => $"gradiente({g.GradientStops[0].Color}→{g.GradientStops[^1].Color})",
        null => "(ausente)",
        var v => v.ToString() ?? "(nulo)",
    };
}
