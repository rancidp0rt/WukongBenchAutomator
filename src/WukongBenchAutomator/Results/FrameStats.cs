namespace WukongBenchAutomator.Results;

internal sealed class FrameStats
{
    public int FrameCount { get; init; }
    public double DurationSeconds { get; init; }

    // взвешено по времени, а не среднее мгновенных FPS
    public double AvgFps { get; init; }
    public double MedianFps { get; init; }
    public double Low1Fps { get; init; }
    public double Low01Fps { get; init; }
    public double AvgFrameTimeMs { get; init; }
    public double P99FrameTimeMs { get; init; }

    public double? AvgCpuFrameTimeMs { get; init; }
    public double? AvgGpuFrameTimeMs { get; init; }

    public double? CpuBoundShare { get; init; }
    public double? GpuBoundShare => CpuBoundShare is { } cpu ? 1 - cpu : null;

    public double? AvgCpuUsage { get; init; }
    public double? AvgGpuUsage { get; init; }
    public double? PeakVideoMemoryMb { get; init; }

    public int StutterCount { get; init; }
    public double StuttersPerMinute => DurationSeconds > 0 ? StutterCount / (DurationSeconds / 60) : 0;

    public static FrameStats? Compute(IReadOnlyList<FrameRecord> records)
    {
        var frames = records.Where(r => double.IsFinite(r.FrameRate) && r.FrameRate > 0).ToList();
        if (frames.Count == 0)
        {
            return null;
        }

        var frameTimes = frames.Select(r => 1000.0 / r.FrameRate).ToArray();
        var sorted = frameTimes.Order().ToArray();
        var totalMs = frameTimes.Sum();

        var withBoth = frames.Where(r => r.CpuFrameTime > 0 && r.GpuFrameTime > 0).ToList();
        var medianFrameTime = Percentile(sorted, 50);

        return new FrameStats
        {
            FrameCount = frames.Count,
            DurationSeconds = totalMs / 1000.0,
            AvgFps = frames.Count / (totalMs / 1000.0),
            MedianFps = 1000.0 / medianFrameTime,
            StutterCount = frameTimes.Count(ft => ft > 2 * medianFrameTime),
            Low1Fps = 1000.0 / Percentile(sorted, 99),
            Low01Fps = 1000.0 / Percentile(sorted, 99.9),
            AvgFrameTimeMs = totalMs / frames.Count,
            P99FrameTimeMs = Percentile(sorted, 99),
            AvgCpuFrameTimeMs = AverageOf(frames, r => r.CpuFrameTime),
            AvgGpuFrameTimeMs = AverageOf(frames, r => r.GpuFrameTime),
            CpuBoundShare = withBoth.Count == 0
                ? null
                : withBoth.Count(r => r.CpuFrameTime >= r.GpuFrameTime) / (double)withBoth.Count,
            AvgCpuUsage = AverageOf(frames, r => r.CpuUsage),
            AvgGpuUsage = AverageOf(frames, r => r.GpuUsage),
            PeakVideoMemoryMb = frames.Any(r => r.VideoMemoryMb > 0) ? frames.Max(r => r.VideoMemoryMb) : null,
        };
    }

    // nearest-rank
    internal static double Percentile(double[] sortedAscending, double percent)
    {
        var rank = (int)Math.Ceiling(percent / 100.0 * sortedAscending.Length);
        return sortedAscending[Math.Clamp(rank - 1, 0, sortedAscending.Length - 1)];
    }

    private static double? AverageOf(List<FrameRecord> frames, Func<FrameRecord, double> selector)
    {
        var values = frames.Select(selector).Where(v => double.IsFinite(v) && v > 0).ToList();
        return values.Count == 0 ? null : values.Average();
    }
}
