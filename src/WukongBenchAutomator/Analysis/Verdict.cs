using System.Globalization;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Reporting;

namespace WukongBenchAutomator.Analysis;

internal enum ValidityStatus
{
    Unknown,
    Valid,
    Questionable,
}

internal sealed record ValidityAssessment(ValidityStatus Status, string Text);

internal enum VerdictLevel
{
    Info,
    Good,
    Warning,
}

internal sealed record VerdictLine(VerdictLevel Level, string Text);

internal static class ValidityChecker
{
    public const double CpuBoundThreshold = 0.80;
    public const double GpuBoundThreshold = 0.90;

    public static ValidityAssessment Assess(PassReport pass)
    {
        var stats = pass.Result?.Stats;
        var gpuUtil = pass.Telemetry?.GpuUtilAvg;
        switch (pass.Profile.Kind)
        {
            case ProfileKind.Cpu when stats?.CpuBoundShare is { } cpuShare:
                return cpuShare >= CpuBoundThreshold
                    ? new(ValidityStatus.Valid, $"{Pct(cpuShare)} кадров упираются в CPU")
                    : new(ValidityStatus.Questionable, $"в CPU упираются только {Pct(cpuShare)} кадров, остальные в GPU");
            case ProfileKind.Cpu when gpuUtil is { } util:
                return util < 85
                    ? new(ValidityStatus.Valid, $"GPU загружен в среднем на {util:0}%")
                    : new(ValidityStatus.Questionable, $"GPU загружен на {util:0}%, тест мог упираться в него");
            case ProfileKind.Gpu when stats?.GpuBoundShare is { } gpuShare:
                var counters = gpuUtil switch
                {
                    null => "",
                    >= 60 => $", GPU загружен на {gpuUtil:0}%",
                    _ => $", но по счётчикам Windows GPU загружен только на {gpuUtil:0}% (возможно, посчитан не тот адаптер)",
                };
                return gpuShare >= GpuBoundThreshold || gpuUtil >= 95
                    ? new(ValidityStatus.Valid, $"{Pct(gpuShare)} кадров упираются в GPU{counters}")
                    : new(ValidityStatus.Questionable, $"в GPU упираются только {Pct(gpuShare)} кадров, мешал CPU");
            case ProfileKind.Gpu when gpuUtil is { } util:
                return util >= 90
                    ? new(ValidityStatus.Valid, $"GPU загружен в среднем на {util:0}%")
                    : new(ValidityStatus.Questionable, $"GPU загружен только на {util:0}%");
            default:
                return new(ValidityStatus.Unknown, "мало данных для проверки");
        }
    }

    internal static string Pct(double share) => (share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}

internal static class VerdictBuilder
{
    public static List<VerdictLine> Build(SessionReport report)
    {
        var lines = new List<VerdictLine>();
        var cpu = report.Passes.FirstOrDefault(p => p.Profile.Kind == ProfileKind.Cpu && p.Succeeded);
        var gpu = report.Passes.FirstOrDefault(p => p.Profile.Kind == ProfileKind.Gpu && p.Succeeded);
        var cpuFps = Fps(cpu);
        var gpuFps = Fps(gpu);

        if (cpu is not null && cpuFps is { } c)
        {
            lines.Add(new(VerdictLevel.Info, $"CPU: {c:0} FPS, 1% low {Fmt(cpu.Result!.Stats?.Low1Fps)}."));
        }

        if (gpu is not null && gpuFps is { } g)
        {
            var rt = gpu.Profile.RayTracing ? ", RT" : "";
            var quality = GameSettingKeys.QualityName(gpu.Profile.Quality).ToLowerInvariant();
            lines.Add(new(VerdictLevel.Info,
                $"GPU: {g:0} FPS, 1% low {Fmt(gpu.Result!.Stats?.Low1Fps)} ({gpu.Profile.Resolution}, {quality}{rt})."));

            lines.Add(g switch
            {
                >= 60 => new(VerdictLevel.Good, "На максимальных настройках больше 60 FPS."),
                >= 30 => new(VerdictLevel.Info, "На максимальных настройках 30-60 FPS, для 60 понадобится апскейл."),
                _ => new(VerdictLevel.Warning, "На максимальных настройках меньше 30 FPS."),
            });
        }

        if (cpuFps is { } cpuValue && gpuFps is { } gpuValue && gpuValue > 0)
        {
            var ratio = cpuValue / gpuValue;
            lines.Add(ratio switch
            {
                >= 1.25 => new(VerdictLevel.Info,
                    $"На максимальных настройках упираемся в GPU: CPU выдаёт примерно в {ratio:0.0} раза больше кадров (до {cpuValue:0} FPS)."),
                >= 0.95 => new(VerdictLevel.Info, "На максимальных настройках CPU и GPU примерно равны."),
                _ => new(VerdictLevel.Warning, "На максимальных настройках упираемся в CPU."),
            });
        }

        foreach (var pass in report.Passes.Where(p => p.Validity is { Status: not ValidityStatus.Unknown }))
        {
            lines.Add(pass.Validity!.Status == ValidityStatus.Valid
                ? new(VerdictLevel.Good, $"{pass.Profile.Title}: ок, {pass.Validity.Text}.")
                : new(VerdictLevel.Warning, $"{pass.Profile.Title}: под вопросом, {pass.Validity.Text}."));
        }

        foreach (var pass in report.Passes.Where(p => p.RunStatistics?.FpsAvg is not null && p.RunStatistics.Runs > 1))
        {
            var cv = pass.RunStatistics!.FpsAvg!.CvPercent;
            lines.Add(cv <= 3
                ? new(VerdictLevel.Good, $"{pass.Profile.Title}: разброс между {pass.RunStatistics.Runs} прогонами {cv:0.0}%.")
                : new(VerdictLevel.Warning, $"{pass.Profile.Title}: разброс между прогонами {cv:0.0}%, что-то мешало (фоновые процессы, нагрев)."));
        }

        foreach (var pass in report.Passes.Where(p => p.Telemetry?.GpuTempMax >= 85))
        {
            lines.Add(new(VerdictLevel.Warning, $"{pass.Profile.Title}: GPU грелся до {pass.Telemetry!.GpuTempMax:0}°C, возможен троттлинг."));
        }

        var warnings = report.Preflight.Count(p => p.IsWarning);
        if (warnings > 0)
        {
            lines.Add(new(VerdictLevel.Warning, $"Предупреждений перед стартом: {warnings}, результаты могут быть занижены."));
        }

        foreach (var pass in report.Passes.Where(p => !p.Succeeded))
        {
            lines.Add(new(VerdictLevel.Warning, $"{pass.Profile.Title} не выполнен: {pass.Error}"));
        }

        return lines;
    }

    private static double? Fps(PassReport? pass) => pass?.Result?.FpsAvg ?? pass?.Result?.Stats?.AvgFps;

    private static string Fmt(double? value) => value is { } v ? v.ToString("0", CultureInfo.InvariantCulture) : "-";
}
