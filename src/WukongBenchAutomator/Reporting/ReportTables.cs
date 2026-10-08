using System.Globalization;
using WukongBenchAutomator.Analysis;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Results;
using WukongBenchAutomator.Telemetry;

namespace WukongBenchAutomator.Reporting;

internal sealed record KeyValueRow(string Label, string Value);

internal sealed record ComparisonRow(string Label, IReadOnlyList<string> Values);

internal static class ReportTables
{
    public const string Missing = "-";
    public const string ComputedMark = " *";
    public const string ComputedFootnote =
        "* посчитано инструментом по покадровым записям бенчмарка (Records); перцентили - по времени кадра.";

    public static string Subtitle(SessionReport report)
    {
        var parts = new List<string> { report.StartedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) };
        if (report.FinishedAt > report.StartedAt)
        {
            parts.Add($"длительность {Format((report.FinishedAt - report.StartedAt).TotalMinutes, "0")} мин");
        }
        else if (report.System is null)
        {
            parts.Add("отчёт по готовым файлам результата");
        }

        parts.Add($"Wukong Bench Automator {report.ToolVersion}");
        return string.Join(" · ", parts);
    }

    public static List<KeyValueRow> Hardware(SessionReport report)
    {
        var rows = new List<KeyValueRow>();
        var bench = report.AnyResult;
        var s = report.System;

        if (s is not null)
        {
            Add(rows, "ОС", JoinNonEmpty(s.OsName, s.OsVersion is null ? null : $"({s.OsVersion})"));
            Add(rows, "Процессор", s.CpuSummary);
            var gpus = s.Gpus.Where(g => !g.IsSoftware).ToList();
            for (var i = 0; i < gpus.Count; i++)
            {
                Add(rows, gpus.Count == 1 ? "Видеокарта" : $"Видеокарта {i + 1}", gpus[i].Summary);
            }

            Add(rows, "Оперативная память", s.RamSummary);
            Add(rows, "Материнская плата", JoinNonEmpty(s.Motherboard, s.Bios is null ? null : $"(BIOS {s.Bios})"));
            Add(rows, "Монитор", s.Display);
            Add(rows, "Диск с бенчмарком", s.GameDrive);
            Add(rows, "План электропитания", s.PowerPlan);
        }
        else if (bench is not null)
        {
            // --report: есть только то, что записал бенчмарк
            Add(rows, "ОС", bench.OsVersion);
            Add(rows, "Процессор", bench.CpuModel);
            Add(rows, "Видеокарта", JoinNonEmpty(bench.GpuModel, bench.VideoMemSize is null ? null : $"({bench.VideoMemSize})"));
            Add(rows, "Оперативная память", bench.SystemMemory);
        }

        if (bench is not null)
        {
            Add(rows, "Драйвер GPU (по данным бенчмарка)", bench.GpuDriver);
            Add(rows, "Версия Benchmark Tool", JoinNonEmpty(bench.GameVersion, bench.Dx12 == 1 ? "(DirectX 12)" : null));
        }

        Add(rows, "Сборка в Steam", report.SteamBuildId);
        return rows;
    }

    public static List<ComparisonRow> Metrics(IReadOnlyList<PassReport> passes)
    {
        string Fps(double? v) => Format(v, "0.0");
        string Ms(double? v) => Format(v, "0.00");
        string Pct(double? v) => v is null ? Missing : $"{Format(v * 100, "0")}%";

        var rows = new List<(string Label, Func<BenchmarkResult, FrameStats?, string> Value)>
        {
            ("Средний FPS", (r, _) => Fps(r.FpsAvg)),
            ("Минимальный FPS", (r, _) => Fps(r.FpsMin)),
            ("Максимальный FPS", (r, _) => Fps(r.FpsMax)),
            ("5% low (FPS95 из бенчмарка)", (r, _) => Fps(r.Fps95)),
            ("1% low" + ComputedMark, (_, s) => Fps(s?.Low1Fps)),
            ("0.1% low" + ComputedMark, (_, s) => Fps(s?.Low01Fps)),
            ("Медианный FPS" + ComputedMark, (_, s) => Fps(s?.MedianFps)),
            ("Среднее время кадра, мс" + ComputedMark, (_, s) => Ms(s?.AvgFrameTimeMs)),
            ("99-й перцентиль времени кадра, мс" + ComputedMark, (_, s) => Ms(s?.P99FrameTimeMs)),
            ("CPU-время кадра (среднее), мс" + ComputedMark, (_, s) => Ms(s?.AvgCpuFrameTimeMs)),
            ("GPU-время кадра (среднее), мс" + ComputedMark, (_, s) => Ms(s?.AvgGpuFrameTimeMs)),
            ("Кадров, ограниченных CPU" + ComputedMark, (_, s) => Pct(s?.CpuBoundShare)),
            ("Кадров, ограниченных GPU" + ComputedMark, (_, s) => Pct(s?.GpuBoundShare)),
            ("Загрузка CPU (средняя), %", (r, s) => Format(r.CpuUsageAvg ?? s?.AvgCpuUsage, "0")),
            ("Загрузка GPU (средняя), %", (r, s) => Format(r.GpuUsageAvg ?? s?.AvgGpuUsage, "0")),
            ("Видеопамять, ГБ", (r, s) => Format(r.VideoMemGb ?? s?.PeakVideoMemoryMb / 1024, "0.0")),
            ("Фризы (кадр дольше 2× медианы)" + ComputedMark,
                (_, s) => s is null ? Missing : $"{s.StutterCount} ({Format(s.StuttersPerMinute, "0.0")}/мин)"),
            ("Кадров в записи / длительность" + ComputedMark,
                (_, s) => s is null ? Missing : $"{s.FrameCount} / {Format(s.DurationSeconds, "0")} с"),
        };

        return rows
            .Select(row => new ComparisonRow(
                row.Label,
                passes.Select(p => p.Result is null ? Missing : row.Value(p.Result, p.Result.Stats)).ToList()))
            .Where(row => row.Values.Any(v => v != Missing))
            .ToList();
    }

    public static List<ComparisonRow> Telemetry(IReadOnlyList<PassReport> passes)
    {
        string AvgMax(double? avg, double? max, string format) =>
            avg is null ? Missing : max is null ? Format(avg, format) : $"{Format(avg, format)} / {Format(max, format)}";

        var rows = new List<(string Label, Func<TelemetrySummary, string> Value)>
        {
            ("Загрузка CPU, все ядра (ср. / макс.), %", t => AvgMax(t.CpuTotalAvg, t.CpuTotalMax, "0")),
            ("Самое загруженное ядро CPU (ср.), %", t => Format(t.CpuBusiestCoreAvg, "0")),
            ("Эффективная частота CPU (ср.), МГц", t => Format(t.CpuEffectiveMhzAvg, "0")),
            ("Доля CPU, занятая игрой (ср.), %", t => Format(t.GameCpuAvg, "0")),
            ("Загрузка GPU (ср. / макс.), %", t => AvgMax(t.GpuUtilAvg, t.GpuUtilMax, "0")),
            ("Видеопамять игры (макс.), ГБ", t => Format(t.GameVramMaxMb / 1024, "0.0")),
            ("ОЗУ занято (макс.), ГБ", t => Format(t.RamUsedMaxGb, "0.0")),
            ("Температура GPU (ср. / макс.), °C", t => AvgMax(t.GpuTempAvg, t.GpuTempMax, "0")),
            ("Мощность GPU (ср. / макс.), Вт", t => AvgMax(t.GpuPowerAvg, t.GpuPowerMax, "0")),
            ("Частота GPU (ср.), МГц", t => Format(t.GpuClockAvg, "0")),
            ("Замеров (раз в секунду)", t => t.Samples.ToString(CultureInfo.InvariantCulture)),
        };

        if (passes.All(p => p.Telemetry is null))
        {
            return [];
        }

        return rows
            .Select(row => new ComparisonRow(row.Label, passes.Select(p => p.Telemetry is null ? Missing : row.Value(p.Telemetry)).ToList()))
            .Where(row => row.Values.Any(v => v != Missing))
            .ToList();
    }

    public static List<ComparisonRow> Runs(IReadOnlyList<PassReport> passes)
    {
        if (passes.All(p => p.RunStatistics is null))
        {
            return [];
        }

        string MeanSd(MetricSpread? m) => m is null ? Missing : $"{Format(m.Mean, "0.0")} ± {Format(m.StdDev, "0.0")}";
        string Range(MetricSpread? m) => m is null ? Missing : $"{Format(m.Min, "0.0")} ... {Format(m.Max, "0.0")}";

        var rows = new List<(string Label, Func<RunStatistics, string> Value)>
        {
            ("Прогонов", s => s.Runs.ToString(CultureInfo.InvariantCulture)),
            ("Средний FPS: среднее ± σ", s => MeanSd(s.FpsAvg)),
            ("Средний FPS: мин ... макс", s => Range(s.FpsAvg)),
            ("Средний FPS: разброс (CV)", s => s.FpsAvg is null ? Missing : $"{Format(s.FpsAvg.CvPercent, "0.0")}%"),
            ("1% low: среднее ± σ", s => MeanSd(s.Low1)),
            ("5% low (FPS95): среднее ± σ", s => MeanSd(s.Fps95)),
        };

        return rows
            .Select(row => new ComparisonRow(row.Label, passes.Select(p => p.RunStatistics is null ? "1 прогон" : row.Value(p.RunStatistics)).ToList()))
            .ToList();
    }

    public static List<ComparisonRow> Settings(IReadOnlyList<PassReport> passes)
    {
        var rows = new List<(string Label, Func<PassReport, string?> Value)>
        {
            ("Разрешение экрана", p => p.Profile.Resolution.Width == 0
                ? null
                : p.Profile.Resolution + (p.Profile.ResolutionNote is null ? "" : $" ({p.Profile.ResolutionNote})")),
            ("Режим экрана", p => p.Profile.FromResultFile ? null : "полноэкранный"),
            ("Масштаб рендера", p => p.Profile.Resolution.Width == 0
                ? $"{p.Profile.RenderScalePercent}%"
                : $"{p.Profile.RenderScalePercent}% (рендер {p.Profile.RenderWidth}x{p.Profile.RenderHeight})"),
            ("Пресет качества", p => p.Profile.Quality > 0 ? GameSettingKeys.QualityName(p.Profile.Quality) : null),
        };

        foreach (var group in GameSettingKeys.QualityGroups)
        {
            rows.Add(($"  {group.Title}", p => p.Profile.FromResultFile ? null : GameSettingKeys.QualityName(p.Profile.Quality)));
        }

        rows.AddRange(new (string, Func<PassReport, string?>)[]
        {
            ("Полная трассировка лучей", p => p.Profile.RayTracing
                ? $"вкл, уровень '{GameSettingKeys.RayTracingLevelName(p.Profile.RayTracingLevel)}'"
                : "выкл"),
            ("Генерация кадров", p => p.Profile.FromResultFile
                ? p.Result?.InsertFrame is { } fg ? (fg == 0 ? "выкл" : $"вкл ({fg})") : null
                : "выкл"),
            ("Вертикальная синхронизация", p => p.Profile.FromResultFile ? null : "выкл"),
            ("Ограничение FPS", p => p.Profile.FromResultFile ? null : "выкл"),
            ("Динамическое разрешение", p => p.Profile.FromResultFile ? null : "выкл"),
            ("Апскейлер (SuperResolutionSampling)", p => Preserved(p, "SuperResolutionSampling")
                ?? (p.Result?.Dlss is { } dlss ? $"значение {dlss} (по файлу результата)" : null)),
            ("Размытие в движении (MotionBlur)", p => Preserved(p, "MotionBlur")),
        });

        return rows
            .Select(row => new ComparisonRow(row.Label, passes.Select(p => row.Value(p) ?? Missing).ToList()))
            .Where(row => row.Values.Any(v => v != Missing))
            .ToList();
    }

    public static string Format(double? value, string format) =>
        value is { } v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : Missing;

    private static string? Preserved(PassReport pass, string key) =>
        pass.PreservedUiValues.TryGetValue(key, out var v) ? $"без изменений (значение {v})" : null;

    private static void Add(List<KeyValueRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new KeyValueRow(label, value));
        }
    }

    private static string? JoinNonEmpty(params string?[] parts)
    {
        var text = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }
}
