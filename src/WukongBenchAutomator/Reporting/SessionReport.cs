using System.Text.Json.Serialization;
using WukongBenchAutomator.Analysis;
using WukongBenchAutomator.Config;
using WukongBenchAutomator.Preflight;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Results;
using WukongBenchAutomator.SystemInfo;
using WukongBenchAutomator.Telemetry;

namespace WukongBenchAutomator.Reporting;

internal sealed class RunResult
{
    public required int Index { get; init; }

    // не required: System.Text.Json не разрешает required вместе с [JsonIgnore]
    [JsonIgnore]
    public BenchmarkResult Result { get; init; } = null!;

    public double? FpsAvg => Result.FpsAvg ?? Result.Stats?.AvgFps;
    public double? Low1Fps => Result.Stats?.Low1Fps;
    public double? Fps95 => Result.Fps95;
    public string ResultFile => Path.GetFileName(Result.SourcePath);

    public TelemetrySummary? Telemetry { get; init; }

    [JsonIgnore]
    public IReadOnlyList<TelemetrySample> Samples { get; init; } = [];

    public string? Screenshot { get; init; }
    public string? NavigationMethod { get; init; }
    public TimeSpan Duration { get; init; }
}

internal sealed class PassReport
{
    public required BenchmarkProfile Profile { get; init; }

    public BenchmarkResult? Result { get; set; }

    public List<RunResult> Runs { get; } = [];
    public RunStatistics? RunStatistics { get; set; }
    public TelemetrySummary? Telemetry { get; set; }

    [JsonIgnore]
    public IReadOnlyList<TelemetrySample> TelemetrySamples { get; set; } = [];

    public ValidityAssessment? Validity { get; set; }
    public string? Error { get; set; }
    public List<string> Notes { get; } = [];
    public List<SettingChange> Changes { get; set; } = [];
    public Dictionary<string, string> PreservedUiValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<VerificationItem> Verification { get; set; } = [];
    public TimeSpan Duration { get; set; }
    public bool GameCrashed { get; set; }

    public bool Succeeded => Result is not null;
}

internal sealed class SessionReport
{
    public required DateTime StartedAt { get; init; }
    public DateTime FinishedAt { get; set; }
    public string ToolVersion { get; init; } = typeof(SessionReport).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public SystemSnapshot? System { get; init; }

    public string? BenchmarkDirectory { get; init; }
    public string? SteamBuildId { get; init; }
    public string? OcrLanguages { get; set; }
    public List<PreflightItem> Preflight { get; set; } = [];
    public List<PassReport> Passes { get; } = [];
    public List<VerdictLine> Verdict { get; set; } = [];
    public ComparisonTable? Comparison { get; set; }

    [JsonIgnore]
    public string? OutputDirectory { get; set; }

    public BenchmarkResult? AnyResult => Passes.Select(p => p.Result).FirstOrDefault(r => r is not null);
}

internal sealed record ComparisonTable(string BaselineLabel, IReadOnlyList<ComparisonRow> Rows);
