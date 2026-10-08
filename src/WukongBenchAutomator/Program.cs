using System.Globalization;
using System.Text;
using WukongBenchAutomator;
using WukongBenchAutomator.Analysis;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Game;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.Reporting;
using WukongBenchAutomator.Results;
using WukongBenchAutomator.Steam;
using WukongBenchAutomator.SystemInfo;
using WukongBenchAutomator.Vision;

Console.OutputEncoding = Encoding.UTF8;

// числа в отчётах с точкой при любых региональных настройках
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

Options options;
try
{
    options = Options.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Options.Usage);
    return 2;
}

if (options.ShowHelp)
{
    Console.WriteLine(Options.Usage);
    return 0;
}

try
{
    return options.Command switch
    {
        Command.SysInfo => PrintSystemInfo(),
        Command.Ocr => PrintOcr(options.OcrImage!),
        Command.Report => BuildReportFromFiles(options),
        _ => new BenchmarkSession(options).Run(),
    };
}
catch (Exception ex)
{
    Log.Error(ex.Message);
    Log.Debug(ex.ToString());
    return 1;
}

static int PrintSystemInfo()
{
    var install = SteamLocator.FindBenchmark(out _);
    var report = new SessionReport
    {
        StartedAt = DateTime.Now,
        System = SystemInfoCollector.Collect(install?.RootDir),
        BenchmarkDirectory = install?.RootDir,
        SteamBuildId = install?.BuildId,
    };

    foreach (var row in ReportTables.Hardware(report))
    {
        Console.WriteLine($"{row.Label}: {row.Value}");
    }

    Console.WriteLine($"Benchmark Tool: {install?.RootDir ?? "не найден в библиотеках Steam"}");
    return 0;
}

// --ocr: что видит OCR на снимке меню
static int PrintOcr(string imagePath)
{
    var reader = ScreenTextReader.TryCreate();
    if (reader is null)
    {
        Console.WriteLine("OCR Windows недоступен на этом ПК.");
        return 1;
    }

    var (lines, size) = reader.ReadFile(Path.GetFullPath(imagePath));
    Console.WriteLine($"Языки OCR: {string.Join(", ", reader.Languages)}. Снимок {size.Width}x{size.Height}, строк: {lines.Count}");
    foreach (var line in lines)
    {
        Console.WriteLine($"  [{line.Bounds.X:0},{line.Bounds.Y:0} {line.Bounds.Width:0}x{line.Bounds.Height:0}] {line.Text}");
    }

    var defaults = Options.Parse([]);
    var benchmark = ScreenTextReader.FindButton(lines, MenuNavigator.BenchmarkKeywords, defaults.BenchmarkButton, size);
    var confirm = ScreenTextReader.FindButton(lines, MenuNavigator.ConfirmKeywords, defaults.ConfirmButton, size);
    Console.WriteLine($"'Тест быстродействия': {(benchmark is { } b ? $"({b.X:0}, {b.Y:0})" : "не найдена")}");
    Console.WriteLine($"'Подтвердить': {(confirm is { } c ? $"({c.X:0}, {c.Y:0})" : "не найдена")}");
    return 0;
}

static int BuildReportFromFiles(Options options)
{
    var report = new SessionReport { StartedAt = DateTime.Now };
    void AddPass(string? path, string id, string title)
    {
        if (path is null || path == "-")
        {
            return;
        }

        var result = ResultParser.Parse(path);
        var profile = SettingsVerifier.ProfileFromResult(id, title, result);
        var pass = new PassReport { Profile = profile, Result = result };
        pass.Validity = ValidityChecker.Assess(pass);
        report.Passes.Add(pass);
    }

    AddPass(options.ReportCpuFile, "cpu", "CPU-тест");
    AddPass(options.ReportGpuFile, "gpu", "GPU-тест");
    report.FinishedAt = report.StartedAt;
    report.Verdict = VerdictBuilder.Build(report);
    if (options.CompareFile is { } compare)
    {
        report.Comparison = History.Compare(report, History.LoadReport(Path.GetFullPath(compare)), Path.GetFileName(compare));
    }

    var root = options.OutputRoot ?? Path.Combine(AppContext.BaseDirectory, "results");
    var directory = Path.GetFullPath(Path.Combine(root, $"report_{report.StartedAt:yyyy-MM-dd_HH-mm-ss}"));
    Directory.CreateDirectory(directory);
    ReportWriter.WriteAll(report, directory);
    return 0;
}
