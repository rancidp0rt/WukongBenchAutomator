using System.Text;
using WukongBenchAutomator.Analysis;

namespace WukongBenchAutomator.Reporting;

internal static class MarkdownReport
{
    public static string Build(SessionReport report)
    {
        var passes = report.Passes;
        var sb = new StringBuilder();

        sb.AppendLine("# Black Myth: Wukong Benchmark Tool - отчёт");
        sb.AppendLine();
        sb.AppendLine(ReportTables.Subtitle(report));
        sb.AppendLine();
        var titles = passes.Select(p => p.Profile.Title).ToList();

        if (report.Verdict.Count > 0)
        {
            sb.AppendLine("## Вердикт");
            sb.AppendLine();
            foreach (var line in report.Verdict)
            {
                var mark = line.Level switch
                {
                    VerdictLevel.Good => "✅",
                    VerdictLevel.Warning => "⚠️",
                    _ => "•",
                };
                sb.AppendLine($"- {mark} {line.Text}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Характеристики ПК");
        sb.AppendLine();
        sb.AppendLine("| Компонент | Значение |");
        sb.AppendLine("|---|---|");
        foreach (var row in ReportTables.Hardware(report))
        {
            sb.AppendLine($"| {Cell(row.Label)} | {Cell(row.Value)} |");
        }

        sb.AppendLine();
        if (report.Preflight.Count > 0)
        {
            sb.AppendLine("### Предстартовые проверки");
            sb.AppendLine();
            foreach (var item in report.Preflight)
            {
                sb.AppendLine($"- {(item.IsWarning ? "⚠️" : "✅")} {item.Message}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Результаты");
        sb.AppendLine();
        foreach (var pass in passes.Where(p => !p.Succeeded))
        {
            sb.AppendLine($"> **{pass.Profile.Title} не выполнен:** {Cell(pass.Error ?? "нет результата")}");
            sb.AppendLine();
        }

        var metrics = ReportTables.Metrics(passes);
        if (metrics.Count > 0)
        {
            AppendTable(sb, "Метрика", titles, metrics);
            sb.AppendLine();
            sb.AppendLine(ReportTables.ComputedFootnote);
            sb.AppendLine();
        }

        var runs = ReportTables.Runs(passes);
        if (runs.Count > 0)
        {
            sb.AppendLine("### Повторные прогоны");
            sb.AppendLine();
            AppendTable(sb, "Показатель", titles, runs);
            sb.AppendLine();
        }

        var telemetry = ReportTables.Telemetry(passes);
        if (telemetry.Count > 0)
        {
            sb.AppendLine("### Телеметрия Windows во время теста");
            sb.AppendLine();
            AppendTable(sb, "Показатель", titles, telemetry);
            sb.AppendLine();
        }

        if (report.Comparison is { } comparison)
        {
            sb.AppendLine("### Сравнение");
            sb.AppendLine();
            AppendTable(sb, "Показатель", ["Сейчас", comparison.BaselineLabel, "Δ"], comparison.Rows);
            sb.AppendLine();
        }

        var screenshots = passes.SelectMany(p => p.Runs.Where(r => r.Screenshot is not null).Select(r => (p.Profile.Title, r))).ToList();
        if (screenshots.Count > 0)
        {
            sb.AppendLine("### Экран результатов бенчмарка");
            sb.AppendLine();
            foreach (var (title, run) in screenshots)
            {
                sb.AppendLine($"![{title}, прогон {run.Index}]({run.Screenshot})");
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Настройки");
        sb.AppendLine();
        AppendTable(sb, "Параметр", titles, ReportTables.Settings(passes));
        sb.AppendLine();

        foreach (var pass in passes.Where(p => p.Profile.Rationale.Count > 0))
        {
            sb.AppendLine($"### Почему такие настройки: {pass.Profile.Title}");
            sb.AppendLine();
            foreach (var line in pass.Profile.Rationale)
            {
                sb.AppendLine($"- {line}");
            }

            sb.AppendLine();
        }

        var verified = passes.Where(p => p.Verification.Count > 0).ToList();
        if (verified.Count > 0)
        {
            sb.AppendLine("## Проверка: что бенчмарк записал в файл результата");
            sb.AppendLine();
            sb.AppendLine("| Проход | Параметр | Ожидалось | В результате | |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var pass in verified)
            {
                foreach (var item in pass.Verification)
                {
                    sb.AppendLine($"| {pass.Profile.Title} | {item.Setting} | {Cell(item.Expected)} | {Cell(item.Actual)} | {(item.Ok ? "✅" : "⚠️")} |");
                }
            }

            sb.AppendLine();
        }

        foreach (var pass in passes.Where(p => p.Notes.Count > 0))
        {
            sb.AppendLine($"**Замечания ({pass.Profile.Title}):**");
            sb.AppendLine();
            foreach (var note in pass.Notes)
            {
                sb.AppendLine($"- {note}");
            }

            sb.AppendLine();
        }

        foreach (var pass in passes.Where(p => p.Changes.Count > 0))
        {
            sb.AppendLine($"<details><summary>Изменённые ключи GameUserSettings.ini - {pass.Profile.Title}</summary>");
            sb.AppendLine();
            sb.AppendLine("| Где | Ключ | Было | Стало |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var change in pass.Changes)
            {
                sb.AppendLine($"| {Cell(change.Location)} | {Cell(change.Key)} | {Cell(change.OldValue ?? "(нет)")} | {Cell(change.NewValue)} |");
            }

            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        foreach (var pass in passes.Where(p => p.Result is not null))
        {
            sb.AppendLine($"<details><summary>Все поля файла результата - {pass.Profile.Title} (<code>{Cell(Path.GetFileName(pass.Result!.SourcePath))}</code>)</summary>");
            sb.AppendLine();
            sb.AppendLine("| Поле | Значение |");
            sb.AppendLine("|---|---|");
            foreach (var (key, value) in pass.Result.RawFields)
            {
                sb.AppendLine($"| {Cell(key)} | {Cell(value)} |");
            }

            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendTable(StringBuilder sb, string firstHeader, IReadOnlyList<string> headers, IReadOnlyList<ComparisonRow> rows)
    {
        sb.AppendLine($"| {firstHeader} | {string.Join(" | ", headers.Select(Cell))} |");
        sb.AppendLine($"|---|{string.Concat(headers.Select(_ => "---:|"))}");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {Cell(row.Label.Trim())} | {string.Join(" | ", row.Values.Select(Cell))} |");
        }
    }

    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}
