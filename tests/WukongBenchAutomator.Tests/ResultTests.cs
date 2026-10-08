using System.Globalization;
using System.Text;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Results;

namespace WukongBenchAutomator.Tests;

public class ResultParserTests
{
    internal static string SampleJson(IEnumerable<(double Fps, double Cpu, double Gpu)> frames, string extra = "")
    {
        var records = string.Join(",", frames.Select(f => string.Create(CultureInfo.InvariantCulture,
            $$"""{"FrameRate":{{f.Fps}},"CPUUsage":40.5,"GPUUsage":97.1,"CPUFrameTime":{{f.Cpu}},"GPUFrameTime":{{f.Gpu}},"VideoMemoryUsage":9000}""")));
        return $$"""
            {
              "FPSAvg": 61.2, "FPSMax": 89.5, "FPSMin": 42.3, "FPS95": 53.3,
              "CPUAvg": 40.9, "GPUAvg": 98.0, "VideoMem": 8.9,
              "GameVer": "1.0.8.14860", "CPUModel": "Test CPU", "GPUModel": "Test GPU", "GpuDriverVer": "566.36",
              "ScreenResolution": "2560 × 1440", "ScreenMode": 1, "QualityLevel": 5, "ImageQuality": 100,
              "Rtx": 4, "Dlss": 0, "InsertFrame": 0, "Dx12": 1{{extra}},
              "Records": [{{records}}]
            }
            """;
    }

    [Fact]
    public void Parse_ReadsSummaryAndRecords()
    {
        var json = SampleJson([(60, 8, 16), (50, 9, 20)]);

        var result = ResultParser.Parse(Encoding.UTF8.GetBytes(json), "1791296990");

        Assert.Equal(61.2, result.FpsAvg);
        Assert.Equal(42.3, result.FpsMin);
        Assert.Equal(53.3, result.Fps95);
        Assert.Equal(98.0, result.GpuUsageAvg);
        Assert.Equal("2560 × 1440", result.ScreenResolution);
        Assert.Equal(4, result.Rtx);
        Assert.Equal(2, result.Records.Count);
        Assert.NotNull(result.Stats);
        Assert.Equal("1.0.8.14860", result.RawFields["GameVer"]);
        Assert.DoesNotContain("Records", result.RawFields.Keys);
    }

    [Fact]
    public void Parse_AcceptsNumericStringsBomAndTrailingNulls()
    {
        var json = """{"fpsavg":"72.5","Records":[{"framerate":"70"}]}""";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).Concat(new byte[] { 0, 0 }).ToArray();

        var result = ResultParser.Parse(bytes, "x");

        Assert.Equal(72.5, result.FpsAvg);
        Assert.Single(result.Records);
    }

    [Theory]
    [InlineData("{\"FPSAvg\": 60, \"Records\": [")] // файл ещё дописывается
    [InlineData("{\"Name\": \"something else\"}")]
    [InlineData("[1, 2, 3]")]
    public void Parse_NotAResult_Throws(string json)
    {
        Assert.Throws<FormatException>(() => ResultParser.Parse(Encoding.UTF8.GetBytes(json), "x"));
    }
}

public class FrameStatsTests
{
    [Fact]
    public void Compute_PercentilesAndBottleneck()
    {
        // 990 кадров по 10 мс (100 FPS, упор в CPU) и 10 кадров по 50 мс (20 FPS, упор в GPU)
        var records = Enumerable.Repeat(new FrameRecord(100, 9, 5, 50, 60, 4000), 990)
            .Concat(Enumerable.Repeat(new FrameRecord(20, 10, 45, 50, 99, 4100), 10))
            .ToList();

        var stats = FrameStats.Compute(records)!;

        Assert.Equal(1000, stats.FrameCount);
        Assert.Equal(10.4, stats.DurationSeconds, 6);
        Assert.Equal(1000 / 10.4, stats.AvgFps, 6);
        Assert.Equal(100, stats.MedianFps, 6);
        Assert.Equal(100, stats.Low1Fps, 6); // ровно 1% медленных кадров - 99-й перцентиль ещё быстрый
        Assert.Equal(20, stats.Low01Fps, 6);
        Assert.Equal(0.99, stats.CpuBoundShare!.Value, 6);
        Assert.Equal(0.01, stats.GpuBoundShare!.Value, 6);
        Assert.Equal(4100, stats.PeakVideoMemoryMb);
    }

    [Fact]
    public void Compute_IgnoresInvalidFrames_AndReturnsNullWhenEmpty()
    {
        Assert.Null(FrameStats.Compute([new FrameRecord(0, 1, 1, 0, 0, 0), new FrameRecord(double.NaN, 1, 1, 0, 0, 0)]));
    }

    [Theory]
    [InlineData(50, 5)]
    [InlineData(99, 10)]
    [InlineData(100, 10)]
    [InlineData(1, 1)]
    public void Percentile_NearestRank(double percent, double expected)
    {
        double[] sorted = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Assert.Equal(expected, FrameStats.Percentile(sorted, percent));
    }
}

public class SettingsVerifierTests
{
    private static readonly BenchmarkProfile Gpu = new()
    {
        Id = "gpu",
        Title = "GPU",
        Resolution = new Resolution(2560, 1440),
        RenderScalePercent = 100,
        Quality = 5,
        RayTracing = true,
    };

    [Fact]
    public void Compare_AllMatch()
    {
        var result = ResultParser.Parse(Encoding.UTF8.GetBytes(ResultParserTests.SampleJson([(60, 8, 16)])), "x");

        var items = SettingsVerifier.Compare(Gpu, result);

        Assert.Equal(5, items.Count);
        Assert.All(items, i => Assert.True(i.Ok, i.Setting));
    }

    [Fact]
    public void Compare_DetectsMismatch()
    {
        var result = ResultParser.Parse(Encoding.UTF8.GetBytes(ResultParserTests.SampleJson([(60, 8, 16)])), "x");

        var items = SettingsVerifier.Compare(Gpu with { Resolution = new Resolution(1920, 1080), RayTracing = false }, result);

        Assert.False(items.Single(i => i.Setting == "Разрешение").Ok);
        Assert.False(items.Single(i => i.Setting == "Трассировка лучей").Ok);
        Assert.True(items.Single(i => i.Setting == "Пресет качества").Ok);
    }

    [Fact]
    public void ProfileFromResult_RestoresSettings()
    {
        var result = ResultParser.Parse(Encoding.UTF8.GetBytes(ResultParserTests.SampleJson([(60, 8, 16)])), "x");

        var profile = SettingsVerifier.ProfileFromResult("gpu", "GPU", result);

        Assert.Equal(new Resolution(2560, 1440), profile.Resolution);
        Assert.Equal(5, profile.Quality);
        Assert.True(profile.RayTracing);
        Assert.True(profile.FromResultFile);
    }
}
