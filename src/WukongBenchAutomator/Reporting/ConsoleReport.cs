using WukongBenchAutomator.Analysis;

namespace WukongBenchAutomator.Reporting;

internal static class ConsoleReport
{
    public static void Print(SessionReport report)
    {
        var passes = report.Passes;
        var headers = passes.Select(p => p.Profile.Title).ToList();

        Console.WriteLine();
        Banner("Black Myth: Wukong Benchmark Tool - итоговый отчёт");

        Section("ХАРАКТЕРИСТИКИ ПК");
        var hardware = ReportTables.Hardware(report);
        var labelWidth = hardware.Count == 0 ? 0 : hardware.Max(r => r.Label.Length);
        foreach (var row in hardware)
        {
            Console.WriteLine($"  {row.Label.PadRight(labelWidth)}  {row.Value}");
        }

        if (report.Preflight.Count > 0)
        {
            Section("ПРЕДСТАРТОВЫЕ ПРОВЕРКИ");
            foreach (var item in report.Preflight)
            {
                WriteColored($"  {(item.IsWarning ? "[!!]" : "[ok]")} {item.Message}", item.IsWarning ? ConsoleColor.Yellow : ConsoleColor.Gray);
            }
        }

        Section("РЕЗУЛЬТАТЫ");
        foreach (var pass in passes.Where(p => !p.Succeeded))
        {
            WriteColored($"  {pass.Profile.Title}: не выполнен - {pass.Error}", ConsoleColor.Red);
        }

        var metrics = ReportTables.Metrics(passes);
        if (metrics.Count > 0)
        {
            PrintTable("Метрика", headers, metrics);
            Console.WriteLine($"  {ReportTables.ComputedFootnote}");
        }

        var runs = ReportTables.Runs(passes);
        if (runs.Count > 0)
        {
            Section("ПОВТОРНЫЕ ПРОГОНЫ");
            PrintTable("Показатель", headers, runs);
        }

        var telemetry = ReportTables.Telemetry(passes);
        if (telemetry.Count > 0)
        {
            Section("ТЕЛЕМЕТРИЯ WINDOWS ВО ВРЕМЯ ТЕСТА");
            PrintTable("Показатель", headers, telemetry);
        }

        if (report.Comparison is { } comparison)
        {
            Section("СРАВНЕНИЕ");
            PrintTable("Показатель", ["Сейчас", comparison.BaselineLabel, "Δ"], comparison.Rows);
        }

        Section("НАСТРОЙКИ");
        PrintTable("Параметр", headers, ReportTables.Settings(passes), numeric: false);

        var verified = passes.Where(p => p.Verification.Count > 0).ToList();
        if (verified.Count > 0)
        {
            Section("ПРОВЕРКА: ЧТО БЕНЧМАРК ЗАПИСАЛ В ФАЙЛ РЕЗУЛЬТАТА");
            foreach (var pass in verified)
            {
                Console.WriteLine($"  {pass.Profile.Title}:");
                foreach (var item in pass.Verification)
                {
                    var line = item.Ok
                        ? $"    [ok] {item.Setting}: {item.Actual}"
                        : $"    [!!] {item.Setting}: ожидалось {item.Expected}, в результате {item.Actual}";
                    WriteColored(line, item.Ok ? ConsoleColor.Green : ConsoleColor.Yellow);
                }
            }
        }

        foreach (var pass in passes.Where(p => p.Notes.Count > 0))
        {
            Section($"ЗАМЕЧАНИЯ: {pass.Profile.Title}");
            foreach (var note in pass.Notes)
            {
                Console.WriteLine($"  • {note}");
            }
        }

        if (report.Verdict.Count > 0)
        {
            Section("ВЕРДИКТ");
            foreach (var line in report.Verdict)
            {
                var (mark, color) = line.Level switch
                {
                    VerdictLevel.Good => ("[ok]", ConsoleColor.Green),
                    VerdictLevel.Warning => ("[!!]", ConsoleColor.Yellow),
                    _ => (" •  ", ConsoleColor.White),
                };
                WriteColored($"  {mark} {line.Text}", color);
            }
        }

        if (report.OutputDirectory is not null)
        {
            Section("ФАЙЛЫ ОТЧЁТА");
            Console.WriteLine($"  {report.OutputDirectory}");
            Console.WriteLine("  report.html - отчёт с графиками и снимками, report.md - то же в Markdown, report.json - для машинной обработки,");
            Console.WriteLine("  <проход>\\frames.csv и telemetry.csv - покадровые данные и телеметрия");
        }

        Console.WriteLine();
    }

    private static void PrintTable(string firstHeader, IReadOnlyList<string> headers, IReadOnlyList<ComparisonRow> rows, bool numeric = true)
    {
        var widths = new int[headers.Count + 1];
        widths[0] = Math.Max(firstHeader.Length, rows.Count == 0 ? 0 : rows.Max(r => r.Label.Length));
        for (var i = 0; i < headers.Count; i++)
        {
            widths[i + 1] = Math.Max(headers[i].Length, rows.Count == 0 ? 0 : rows.Max(r => r.Values[i].Length));
        }

        string Line(string first, IEnumerable<string> rest) =>
            "  " + first.PadRight(widths[0])
                 + string.Concat(rest.Select((v, i) => "  " + (numeric ? v.PadLeft(widths[i + 1]) : v.PadRight(widths[i + 1]))));

        WriteColored(Line(firstHeader, headers), ConsoleColor.White);
        Console.WriteLine("  " + string.Join("  ", widths.Select(w => new string('─', w))));
        foreach (var row in rows)
        {
            Console.WriteLine(Line(row.Label, row.Values));
        }
    }

    private static void Banner(string title)
    {
        var line = new string('═', title.Length + 4);
        WriteColored($"╔{line}╗", ConsoleColor.Cyan);
        WriteColored($"║  {title}  ║", ConsoleColor.Cyan);
        WriteColored($"╚{line}╝", ConsoleColor.Cyan);
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        WriteColored(title, ConsoleColor.Cyan);
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = previous;
    }
}
