using System.Text.Json.Serialization;

namespace WukongBenchAutomator.Results;

internal sealed class BenchmarkResult
{
    public required string SourcePath { get; init; }

    public double? FpsAvg { get; init; }
    public double? FpsMin { get; init; }
    public double? FpsMax { get; init; }

    // в меню бенчмарка это '5% low'
    public double? Fps95 { get; init; }

    public double? CpuUsageAvg { get; init; }
    public double? GpuUsageAvg { get; init; }
    public double? VideoMemGb { get; init; }

    public string? GameVersion { get; init; }
    public string? OsVersion { get; init; }
    public string? CpuModel { get; init; }
    public string? GpuModel { get; init; }
    public string? GpuDriver { get; init; }
    public string? VideoMemSize { get; init; }
    public string? SystemMemory { get; init; }

    public string? ScreenResolution { get; init; }
    public int? ScreenMode { get; init; }
    public int? QualityLevel { get; init; }
    public int? ImageQuality { get; init; }
    public int? ViewDistance { get; init; }
    public int? Rtx { get; init; }
    public int? Dlss { get; init; }
    public int? InsertFrame { get; init; }
    public int? Dx12 { get; init; }

    [JsonIgnore]
    public IReadOnlyList<FrameRecord> Records { get; init; } = [];

    public FrameStats? Stats { get; init; }

    public IReadOnlyDictionary<string, string> RawFields { get; init; } = new Dictionary<string, string>();
}

internal readonly record struct FrameRecord(
    double FrameRate,
    double CpuFrameTime,
    double GpuFrameTime,
    double CpuUsage,
    double GpuUsage,
    double VideoMemoryMb);
