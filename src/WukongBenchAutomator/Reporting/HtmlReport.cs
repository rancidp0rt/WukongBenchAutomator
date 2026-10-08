using System.Globalization;
using System.Net;
using System.Text;
using WukongBenchAutomator.Analysis;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Results;
using WukongBenchAutomator.Telemetry;

namespace WukongBenchAutomator.Reporting;

internal static class HtmlReport
{
    private const int ChartWidth = 760;
    private const int ChartHeight = 220;
    private const int Buckets = 300;

    public static string Build(SessionReport report)
    {
        var passes = report.Passes;
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>Wukong Benchmark Report</title>");
        sb.AppendLine($"<style>{Css}</style></head><body><main>");

        sb.AppendLine("<h1>Black Myth: Wukong Benchmark Tool - отчёт</h1>");
        sb.AppendLine($"<p class=\"muted\">{E(ReportTables.Subtitle(report))}</p>");

        var titles = passes.Select(p => p.Profile.Title).ToList();

        sb.AppendLine("<div class=\"cards\">");
        foreach (var pass in passes)
        {
            var kind = pass.Profile.Kind.ToString().ToLowerInvariant();
            sb.AppendLine($"<section class=\"card {kind}\"><h2>{E(pass.Profile.Title)}</h2>");
            if (pass.Result is { } r)
            {
                sb.AppendLine($"<div class=\"big\">{ReportTables.Format(r.FpsAvg ?? r.Stats?.AvgFps, "0.0")}<span> FPS</span></div>");
                sb.AppendLine("<div class=\"sub\">");
                sb.AppendLine($"<span>1% low <b>{ReportTables.Format(r.Stats?.Low1Fps, "0.0")}</b></span>");
                sb.AppendLine($"<span>мин. <b>{ReportTables.Format(r.FpsMin, "0.0")}</b></span>");
                var isCpu = pass.Profile.Kind == ProfileKind.Cpu;
                if ((isCpu ? r.Stats?.CpuBoundShare : r.Stats?.GpuBoundShare) is { } share)
                {
                    sb.AppendLine($"<span>упор в {(isCpu ? "CPU" : "GPU")} <b>{ReportTables.Format(share * 100, "0")}%</b> кадров</span>");
                }

                if (pass.Telemetry?.GpuUtilAvg is { } gpuUtil)
                {
                    sb.AppendLine($"<span>загрузка GPU <b>{gpuUtil:0}%</b></span>");
                }

                sb.AppendLine("</div>");
                if (pass.Validity is { Status: not ValidityStatus.Unknown } validity)
                {
                    var ok = validity.Status == ValidityStatus.Valid;
                    sb.AppendLine($"<div class=\"badge {(ok ? "ok" : "warn")}\">{(ok ? "✓ тест корректен" : "⚠ под вопросом")}: {E(validity.Text)}</div>");
                }
            }
            else
            {
                sb.AppendLine($"<div class=\"error\">Не выполнен: {E(pass.Error ?? "нет результата")}</div>");
            }

            sb.AppendLine("</section>");
        }

        sb.AppendLine("</div>");

        if (report.Verdict.Count > 0)
        {
            sb.AppendLine("<section class=\"verdict\"><h2>Вердикт</h2><ul>");
            foreach (var line in report.Verdict)
            {
                var (css, mark) = line.Level switch
                {
                    VerdictLevel.Good => ("ok", "✓"),
                    VerdictLevel.Warning => ("warn", "⚠"),
                    _ => ("info", "•"),
                };
                sb.AppendLine($"<li class=\"{css}\"><span class=\"mark\">{mark}</span>{E(line.Text)}</li>");
            }

            sb.AppendLine("</ul></section>");
        }

        sb.AppendLine("<h2>Характеристики ПК</h2><table class=\"kv\">");
        foreach (var row in ReportTables.Hardware(report))
        {
            sb.AppendLine($"<tr><th>{E(row.Label)}</th><td>{E(row.Value)}</td></tr>");
        }

        sb.AppendLine("</table>");

        if (report.Preflight.Count > 0)
        {
            sb.AppendLine("<h3>Предстартовые проверки</h3><ul class=\"checks\">");
            foreach (var item in report.Preflight)
            {
                sb.AppendLine($"<li class=\"{(item.IsWarning ? "warn" : "ok")}\"><span class=\"mark\">{(item.IsWarning ? "⚠" : "✓")}</span>{E(item.Message)}</li>");
            }

            sb.AppendLine("</ul>");
        }

        var metrics = ReportTables.Metrics(passes);
        if (metrics.Count > 0)
        {
            sb.AppendLine("<h2>Результаты</h2>");
            AppendTable(sb, "Метрика", titles, metrics);
            sb.AppendLine($"<p class=\"muted small\">{E(ReportTables.ComputedFootnote)}</p>");
        }

        var runs = ReportTables.Runs(passes);
        if (runs.Count > 0)
        {
            sb.AppendLine("<h3>Повторные прогоны</h3>");
            AppendTable(sb, "Показатель", titles, runs);
        }

        var telemetry = ReportTables.Telemetry(passes);
        if (telemetry.Count > 0)
        {
            sb.AppendLine("<h3>Телеметрия Windows во время теста</h3>");
            AppendTable(sb, "Показатель", titles, telemetry);
            sb.AppendLine("<p class=\"muted small\">Счётчики производительности Windows, раз в секунду, только за время самого теста - "
                          + "независимая проверка того, что нагружено. Температура, мощность и частота GPU - через nvidia-smi (только NVIDIA).</p>");
        }

        if (report.Comparison is { } comparison)
        {
            sb.AppendLine("<h3>Сравнение</h3>");
            AppendTable(sb, "Показатель", ["Сейчас", comparison.BaselineLabel, "Δ"], comparison.Rows);
        }

        var screenshots = passes.SelectMany(p => p.Runs.Where(r => r.Screenshot is not null).Select(r => (p.Profile.Title, Run: r))).ToList();
        if (screenshots.Count > 0)
        {
            sb.AppendLine("<h3>Экран результатов бенчмарка</h3><div class=\"shots\">");
            foreach (var (title, run) in screenshots)
            {
                sb.AppendLine($"<figure><a href=\"{E(run.Screenshot!)}\"><img src=\"{E(run.Screenshot!)}\" alt=\"{E(title)}\" loading=\"lazy\"></a>"
                              + $"<figcaption>{E(title)}{(run.Index > 1 || passes.Any(p => p.Runs.Count > 1) ? $", прогон {run.Index}" : "")}</figcaption></figure>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("<h2>Настройки</h2>");
        AppendTable(sb, "Параметр", titles, ReportTables.Settings(passes));

        foreach (var pass in passes.Where(p => p.Profile.Rationale.Count > 0))
        {
            sb.AppendLine($"<h3>Почему такие настройки: {E(pass.Profile.Title)}</h3><ul>");
            foreach (var line in pass.Profile.Rationale)
            {
                sb.AppendLine($"<li>{E(line)}</li>");
            }

            sb.AppendLine("</ul>");
        }

        var charted = passes.Where(p => p.Result?.Records.Count > 1 || p.TelemetrySamples.Count > 1).ToList();
        if (charted.Count > 0)
        {
            sb.AppendLine("<h2>Графики по кадрам</h2>");
            foreach (var pass in charted)
            {
                AppendCharts(sb, pass);
            }
        }

        var verified = passes.Where(p => p.Verification.Count > 0).ToList();
        if (verified.Count > 0)
        {
            sb.AppendLine("<h2>Проверка: что бенчмарк записал в файл результата</h2>");
            sb.AppendLine("<table><tr><th>Проход</th><th>Параметр</th><th>Ожидалось</th><th>В результате</th><th></th></tr>");
            foreach (var pass in verified)
            {
                foreach (var item in pass.Verification)
                {
                    sb.AppendLine($"<tr><td>{E(pass.Profile.Title)}</td><td>{E(item.Setting)}</td><td>{E(item.Expected)}</td>"
                                  + $"<td>{E(item.Actual)}</td><td class=\"{(item.Ok ? "ok" : "warn")}\">{(item.Ok ? "✓" : "⚠")}</td></tr>");
                }
            }

            sb.AppendLine("</table>");
        }

        foreach (var pass in passes.Where(p => p.Notes.Count > 0))
        {
            sb.AppendLine($"<h3>Замечания: {E(pass.Profile.Title)}</h3><ul>");
            foreach (var note in pass.Notes)
            {
                sb.AppendLine($"<li>{E(note)}</li>");
            }

            sb.AppendLine("</ul>");
        }

        foreach (var pass in passes.Where(p => p.Changes.Count > 0))
        {
            sb.AppendLine($"<details><summary>Изменённые ключи GameUserSettings.ini - {E(pass.Profile.Title)}</summary>");
            sb.AppendLine("<table><tr><th>Где</th><th>Ключ</th><th>Было</th><th>Стало</th></tr>");
            foreach (var c in pass.Changes)
            {
                sb.AppendLine($"<tr><td>{E(c.Location)}</td><td><code>{E(c.Key)}</code></td><td>{E(c.OldValue ?? "(нет)")}</td><td>{E(c.NewValue)}</td></tr>");
            }

            sb.AppendLine("</table></details>");
        }

        foreach (var pass in passes.Where(p => p.Result is not null))
        {
            sb.AppendLine($"<details><summary>Все поля файла результата - {E(pass.Profile.Title)}</summary><table class=\"kv\">");
            foreach (var (key, value) in pass.Result!.RawFields)
            {
                sb.AppendLine($"<tr><th><code>{E(key)}</code></th><td>{E(value)}</td></tr>");
            }

            sb.AppendLine("</table></details>");
        }

        sb.AppendLine("</main></body></html>");
        return sb.ToString();
    }

    private static void AppendTable(StringBuilder sb, string firstHeader, IReadOnlyList<string> headers, IReadOnlyList<ComparisonRow> rows)
    {
        sb.Append($"<table class=\"cmp\"><tr><th>{E(firstHeader)}</th>");
        foreach (var header in headers)
        {
            sb.Append($"<th>{E(header)}</th>");
        }

        sb.AppendLine("</tr>");
        foreach (var row in rows)
        {
            var indent = row.Label.StartsWith("  ", StringComparison.Ordinal) ? " class=\"indent\"" : "";
            sb.Append($"<tr><th{indent}>{E(row.Label.Trim())}</th>");
            foreach (var value in row.Values)
            {
                sb.Append($"<td>{E(value)}</td>");
            }

            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table>");
    }

    private static void AppendCharts(StringBuilder sb, PassReport pass)
    {
        sb.AppendLine($"<h3>{E(pass.Profile.Title)}</h3><div class=\"charts\">");
        AppendFrameCharts(sb, pass);
        AppendTelemetryChart(sb, pass);
        sb.AppendLine("</div>");
    }

    private static void AppendTelemetryChart(StringBuilder sb, PassReport pass)
    {
        var samples = pass.TelemetrySamples;
        if (samples.Count < 2)
        {
            return;
        }

        var t0 = samples[0].TimeUtc;
        List<(double T, double V)> Series(Func<TelemetrySample, double?> f) => samples
            .Where(s => f(s) is { } v && double.IsFinite(v))
            .Select(s => ((s.TimeUtc - t0).TotalSeconds, Math.Min(100, f(s)!.Value)))
            .ToList();

        var series = new List<(string Name, string Color, List<(double T, double V)> Points)>
        {
            ("GPU", "var(--gpu)", Series(s => s.GpuUtil)),
            ("CPU, все ядра", "var(--cpu)", Series(s => s.CpuTotal)),
            ("CPU, самое занятое ядро", "var(--core)", Series(s => s.CpuBusiestCore)),
        };
        series.RemoveAll(s => s.Points.Count < 2);
        if (series.Count == 0)
        {
            return;
        }

        sb.AppendLine("<figure>");
        sb.AppendLine(Chart(series, "%", fixedMax: 100));
        sb.AppendLine("<figcaption><span class=\"dot gpu\"></span>загрузка GPU <span class=\"dot cpu\"></span>CPU, все ядра "
                      + "<span class=\"dot core\"></span>самое загруженное ядро CPU (%, счётчики Windows)</figcaption></figure>");
    }

    private static void AppendFrameCharts(StringBuilder sb, PassReport pass)
    {
        var records = pass.Result?.Records.Where(r => double.IsFinite(r.FrameRate) && r.FrameRate > 0).ToList() ?? [];
        if (records.Count < 2)
        {
            return;
        }

        // ось X - время теста (сумма времён кадров)
        var totalMs = records.Sum(r => 1000.0 / r.FrameRate);
        var bucketMs = totalMs / Buckets;
        var fps = new List<(double T, double V)>();
        var cpu = new List<(double T, double V)>();
        var gpu = new List<(double T, double V)>();
        var elapsed = 0.0;
        var index = 0;
        for (var b = 0; b < Buckets && index < records.Count; b++)
        {
            var end = (b + 1) * bucketMs;
            var frames = 0;
            var time = 0.0;
            double cpuSum = 0, gpuSum = 0;
            int cpuCount = 0, gpuCount = 0;
            while (index < records.Count && (elapsed < end || frames == 0))
            {
                var r = records[index++];
                var ft = 1000.0 / r.FrameRate;
                elapsed += ft;
                time += ft;
                frames++;
                if (r.CpuFrameTime > 0) { cpuSum += r.CpuFrameTime; cpuCount++; }
                if (r.GpuFrameTime > 0) { gpuSum += r.GpuFrameTime; gpuCount++; }
            }

            var t = elapsed / 1000.0;
            fps.Add((t, frames * 1000.0 / time));
            if (cpuCount > 0) cpu.Add((t, cpuSum / cpuCount));
            if (gpuCount > 0) gpu.Add((t, gpuSum / gpuCount));
        }

        sb.AppendLine("<figure>");
        sb.AppendLine(Chart([("FPS", "var(--fps)", fps)], "FPS"));
        sb.AppendLine($"<figcaption>FPS по ходу теста{(pass.Runs.Count > 1 ? " (все прогоны подряд)" : "")}</figcaption></figure>");
        if (cpu.Count > 1 && gpu.Count > 1)
        {
            sb.AppendLine("<figure>");
            sb.AppendLine(Chart([("CPU", "var(--cpu)", cpu), ("GPU", "var(--gpu)", gpu)], "мс"));
            sb.AppendLine("<figcaption><span class=\"dot cpu\"></span>CPU-время кадра <span class=\"dot gpu\"></span>GPU-время кадра (мс): "
                          + "чья линия выше, тот и ограничивает FPS</figcaption></figure>");
        }
    }

    private static string Chart(
        IReadOnlyList<(string Name, string Color, List<(double T, double V)> Points)> series, string unit, double? fixedMax = null)
    {
        const int left = 44, right = 10, top = 10, bottom = 26;
        var plotW = ChartWidth - left - right;
        var plotH = ChartHeight - top - bottom;
        var maxT = Math.Max(1, series.Max(s => s.Points.Max(p => p.T)));
        var maxV = fixedMax ?? NiceCeiling(series.Max(s => s.Points.Max(p => p.V)) * 1.05);

        string X(double t) => (left + t / maxT * plotW).ToString("0.#", CultureInfo.InvariantCulture);
        string Y(double v) => (top + plotH - v / maxV * plotH).ToString("0.#", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {ChartWidth} {ChartHeight}\" role=\"img\" aria-label=\"{E(unit)}\">");
        for (var i = 0; i <= 4; i++)
        {
            var v = maxV * i / 4;
            sb.Append($"<line class=\"grid\" x1=\"{left}\" x2=\"{ChartWidth - right}\" y1=\"{Y(v)}\" y2=\"{Y(v)}\"/>");
            sb.Append($"<text class=\"axis\" x=\"{left - 6}\" y=\"{Y(v)}\" text-anchor=\"end\" dominant-baseline=\"middle\">{v.ToString("0.#", CultureInfo.InvariantCulture)}</text>");
        }

        var step = maxT > 120 ? 30 : maxT > 40 ? 10 : 5;
        for (var t = 0.0; t <= maxT; t += step)
        {
            sb.Append($"<text class=\"axis\" x=\"{X(t)}\" y=\"{ChartHeight - 8}\" text-anchor=\"middle\">{t:0} с</text>");
        }

        foreach (var (name, color, points) in series)
        {
            var path = string.Join(" ", points.Select((p, i) => $"{(i == 0 ? "M" : "L")}{X(p.T)},{Y(p.V)}"));
            sb.Append($"<path d=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"1.6\" stroke-linejoin=\"round\"><title>{E(name)}</title></path>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static double NiceCeiling(double value)
    {
        if (value <= 0 || !double.IsFinite(value))
        {
            return 1;
        }

        // чтобы деления шкалы были круглыми: 24 -> 6, 12, 18, 24
        double[] candidates = [1, 1.2, 1.6, 2, 2.4, 3.2, 4, 6, 8, 10];
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        return candidates.First(c => normalized <= c + 1e-9) * magnitude;
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);

    private const string Css = """
        :root { --bg:#f7f7f8; --fg:#1d1d20; --muted:#6b6b76; --card:#fff; --line:#e3e3e8; --fps:#2563eb; --cpu:#d9480f; --core:#9333ea; --gpu:#0f9d8a; --ok:#15803d; --warn:#b45309; --okbg:#ecfdf3; --warnbg:#fff7e6; }
        @media (prefers-color-scheme: dark) { :root { --bg:#141417; --fg:#ececf1; --muted:#9a9aa6; --card:#1d1d22; --line:#2f2f37; --fps:#6ea0ff; --cpu:#ff8a50; --core:#c084fc; --gpu:#3dd6c0; --ok:#4ade80; --warn:#fbbf24; --okbg:#12261b; --warnbg:#2b2210; } }
        .badge { margin-top:10px; font-size:13px; padding:5px 9px; border-radius:7px; } .badge.ok { background:var(--okbg); color:var(--ok); } .badge.warn { background:var(--warnbg); color:var(--warn); }
        .verdict { margin-top:18px; background:var(--card); border:1px solid var(--line); border-radius:12px; padding:6px 18px 10px; }
        .verdict h2 { margin:10px 0 6px; } .verdict ul, ul.checks { list-style:none; padding:0; margin:0; }
        .verdict li, ul.checks li { display:flex; gap:10px; padding:5px 0; } .mark { width:16px; flex:none; text-align:center; font-weight:700; }
        li.ok .mark { color:var(--ok); } li.warn .mark { color:var(--warn); } li.info .mark { color:var(--muted); }
        .shots { display:grid; grid-template-columns:repeat(auto-fit, minmax(300px, 1fr)); gap:12px; } .shots img { width:100%; height:auto; border-radius:6px; display:block; }
        .dot.core { background:var(--core); }
        * { box-sizing:border-box; }
        body { margin:0; background:var(--bg); color:var(--fg); font:15px/1.5 "Segoe UI", system-ui, sans-serif; }
        main { max-width:1000px; margin:0 auto; padding:24px 16px 64px; }
        h1 { font-size:26px; margin:0 0 4px; } h2 { font-size:19px; margin:32px 0 10px; } h3 { font-size:16px; margin:22px 0 8px; }
        .muted { color:var(--muted); } .small { font-size:13px; }
        .cards { display:grid; grid-template-columns:repeat(auto-fit, minmax(260px, 1fr)); gap:14px; margin-top:18px; }
        .card { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:16px 18px; }
        .card h2 { margin:0; font-size:15px; color:var(--muted); font-weight:600; }
        .card.cpu { border-top:3px solid var(--cpu); } .card.gpu { border-top:3px solid var(--gpu); }
        .big { font-size:40px; font-weight:700; font-variant-numeric:tabular-nums; } .big span { font-size:16px; color:var(--muted); font-weight:500; }
        .sub { display:flex; flex-wrap:wrap; gap:6px 16px; color:var(--muted); font-size:14px; } .sub b { color:var(--fg); }
        .error { color:var(--warn); margin-top:8px; }
        table { width:100%; border-collapse:collapse; background:var(--card); border:1px solid var(--line); border-radius:10px; overflow:hidden; }
        th, td { padding:7px 12px; border-bottom:1px solid var(--line); text-align:left; vertical-align:top; }
        tr:last-child th, tr:last-child td { border-bottom:none; }
        table.cmp td { text-align:right; font-variant-numeric:tabular-nums; white-space:nowrap; }
        table.cmp th:not(:first-child) { text-align:right; }
        th { font-weight:600; } table.kv th { width:34%; color:var(--muted); font-weight:500; }
        th.indent { padding-left:28px; font-weight:400; color:var(--muted); }
        td.ok { color:var(--ok); } td.warn { color:var(--warn); }
        .charts { display:grid; gap:12px; } figure { margin:0; background:var(--card); border:1px solid var(--line); border-radius:10px; padding:10px; }
        figcaption { color:var(--muted); font-size:13px; padding:4px 6px 0; }
        svg { width:100%; height:auto; display:block; } svg .grid { stroke:var(--line); stroke-width:1; } svg .axis { fill:var(--muted); font-size:11px; }
        .dot { display:inline-block; width:10px; height:10px; border-radius:50%; margin:0 4px 0 10px; } .dot.cpu { background:var(--cpu); } .dot.gpu { background:var(--gpu); }
        details { margin-top:14px; } summary { cursor:pointer; color:var(--muted); margin-bottom:8px; }
        code { font-family:Consolas, monospace; font-size:13px; }
        @media (max-width:640px) { table.cmp { display:block; overflow-x:auto; } }
        """;
}
