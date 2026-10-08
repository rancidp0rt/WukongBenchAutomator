using WukongBenchAutomator.Results;

namespace WukongBenchAutomator.Analysis;

internal sealed record MetricSpread(double Mean, double Min, double Max, double StdDev)
{
    public double CvPercent => Mean > 0 ? StdDev / Mean * 100 : 0;

    public static MetricSpread? Of(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is { } d && double.IsFinite(d)).Select(v => v!.Value).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var mean = list.Average();
        var variance = list.Count > 1 ? list.Sum(v => (v - mean) * (v - mean)) / (list.Count - 1) : 0;
        return new MetricSpread(mean, list.Min(), list.Max(), Math.Sqrt(variance));
    }
}

internal sealed class RunStatistics
{
    public int Runs { get; init; }
    public MetricSpread? FpsAvg { get; init; }
    public MetricSpread? Low1 { get; init; }
    public MetricSpread? Fps95 { get; init; }
    public MetricSpread? FpsMin { get; init; }

    public static RunStatistics Compute(IReadOnlyList<BenchmarkResult> runs) => new()
    {
        Runs = runs.Count,
        FpsAvg = MetricSpread.Of(runs.Select(r => r.FpsAvg ?? r.Stats?.AvgFps)),
        Low1 = MetricSpread.Of(runs.Select(r => r.Stats?.Low1Fps)),
        Fps95 = MetricSpread.Of(runs.Select(r => r.Fps95)),
        FpsMin = MetricSpread.Of(runs.Select(r => r.FpsMin)),
    };
}

internal static class RunAggregator
{
    public static BenchmarkResult Aggregate(IReadOnlyList<BenchmarkResult> runs)
    {
        if (runs.Count == 1)
        {
            return runs[0];
        }

        var first = runs[0];
        double? Mean(Func<BenchmarkResult, double?> f)
        {
            var values = runs.Select(f).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : values.Average();
        }

        var records = runs.SelectMany(r => r.Records).ToList();
        return new BenchmarkResult
        {
            SourcePath = first.SourcePath,
            FpsAvg = Mean(r => r.FpsAvg),
            FpsMin = Mean(r => r.FpsMin),
            FpsMax = Mean(r => r.FpsMax),
            Fps95 = Mean(r => r.Fps95),
            CpuUsageAvg = Mean(r => r.CpuUsageAvg),
            GpuUsageAvg = Mean(r => r.GpuUsageAvg),
            VideoMemGb = runs.Max(r => r.VideoMemGb),
            GameVersion = first.GameVersion,
            OsVersion = first.OsVersion,
            CpuModel = first.CpuModel,
            GpuModel = first.GpuModel,
            GpuDriver = first.GpuDriver,
            VideoMemSize = first.VideoMemSize,
            SystemMemory = first.SystemMemory,
            ScreenResolution = first.ScreenResolution,
            ScreenMode = first.ScreenMode,
            QualityLevel = first.QualityLevel,
            ImageQuality = first.ImageQuality,
            ViewDistance = first.ViewDistance,
            Rtx = first.Rtx,
            Dlss = first.Dlss,
            InsertFrame = first.InsertFrame,
            Dx12 = first.Dx12,
            Records = records,
            Stats = FrameStats.Compute(records),
            RawFields = first.RawFields,
        };
    }
}
