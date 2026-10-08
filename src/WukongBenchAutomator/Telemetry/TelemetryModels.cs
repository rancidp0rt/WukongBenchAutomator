namespace WukongBenchAutomator.Telemetry;

internal sealed record TelemetrySample(
    DateTime TimeUtc,
    double? CpuTotal,
    double? CpuBusiestCore,
    double? CpuEffectiveMhz,
    double? GameCpu,
    double? GpuUtil,
    double? GameVramMb,
    double? RamUsedGb,
    double? GpuTempC,
    double? GpuPowerW,
    double? GpuClockMhz);

internal sealed class TelemetrySummary
{
    public int Samples { get; init; }

    public double? CpuTotalAvg { get; init; }
    public double? CpuTotalMax { get; init; }

    // высокая загрузка одного ядра = упор в один поток
    public double? CpuBusiestCoreAvg { get; init; }

    public double? CpuEffectiveMhzAvg { get; init; }

    public double? GameCpuAvg { get; init; }

    public double? GpuUtilAvg { get; init; }
    public double? GpuUtilMax { get; init; }

    public double? GameVramMaxMb { get; init; }
    public double? RamUsedMaxGb { get; init; }

    // только NVIDIA (nvidia-smi)
    public double? GpuTempAvg { get; init; }
    public double? GpuTempMax { get; init; }
    public double? GpuPowerAvg { get; init; }
    public double? GpuPowerMax { get; init; }
    public double? GpuClockAvg { get; init; }

    public static TelemetrySummary? From(IReadOnlyCollection<TelemetrySample> samples)
    {
        if (samples.Count == 0)
        {
            return null;
        }

        double? Avg(Func<TelemetrySample, double?> f)
        {
            var values = samples.Select(f).Where(v => v is { } d && double.IsFinite(d)).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Average();
        }

        double? Max(Func<TelemetrySample, double?> f)
        {
            var values = samples.Select(f).Where(v => v is { } d && double.IsFinite(d)).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Max();
        }

        return new TelemetrySummary
        {
            Samples = samples.Count,
            CpuTotalAvg = Avg(s => s.CpuTotal),
            CpuTotalMax = Max(s => s.CpuTotal),
            CpuBusiestCoreAvg = Avg(s => s.CpuBusiestCore),
            CpuEffectiveMhzAvg = Avg(s => s.CpuEffectiveMhz),
            GameCpuAvg = Avg(s => s.GameCpu),
            GpuUtilAvg = Avg(s => s.GpuUtil),
            GpuUtilMax = Max(s => s.GpuUtil),
            GameVramMaxMb = Max(s => s.GameVramMb),
            RamUsedMaxGb = Max(s => s.RamUsedGb),
            GpuTempAvg = Avg(s => s.GpuTempC),
            GpuTempMax = Max(s => s.GpuTempC),
            GpuPowerAvg = Avg(s => s.GpuPowerW),
            GpuPowerMax = Max(s => s.GpuPowerW),
            GpuClockAvg = Avg(s => s.GpuClockMhz),
        };
    }

    public static TelemetrySummary? Combine(IReadOnlyCollection<TelemetrySummary> parts)
    {
        if (parts.Count <= 1)
        {
            return parts.FirstOrDefault();
        }

        double? Avg(Func<TelemetrySummary, double?> f)
        {
            var values = parts.Select(f).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Average();
        }

        double? Max(Func<TelemetrySummary, double?> f)
        {
            var values = parts.Select(f).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Max();
        }

        return new TelemetrySummary
        {
            Samples = parts.Sum(p => p.Samples),
            CpuTotalAvg = Avg(p => p.CpuTotalAvg),
            CpuTotalMax = Max(p => p.CpuTotalMax),
            CpuBusiestCoreAvg = Avg(p => p.CpuBusiestCoreAvg),
            CpuEffectiveMhzAvg = Avg(p => p.CpuEffectiveMhzAvg),
            GameCpuAvg = Avg(p => p.GameCpuAvg),
            GpuUtilAvg = Avg(p => p.GpuUtilAvg),
            GpuUtilMax = Max(p => p.GpuUtilMax),
            GameVramMaxMb = Max(p => p.GameVramMaxMb),
            RamUsedMaxGb = Max(p => p.RamUsedMaxGb),
            GpuTempAvg = Avg(p => p.GpuTempAvg),
            GpuTempMax = Max(p => p.GpuTempMax),
            GpuPowerAvg = Avg(p => p.GpuPowerAvg),
            GpuPowerMax = Max(p => p.GpuPowerMax),
            GpuClockAvg = Avg(p => p.GpuClockAvg),
        };
    }
}
