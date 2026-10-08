using System.Buffers.Binary;
using System.Drawing;
using System.Text;
using System.Text.Json;
using WukongBenchAutomator.Analysis;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Preflight;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Reporting;
using WukongBenchAutomator.Results;
using WukongBenchAutomator.SystemInfo;
using WukongBenchAutomator.Telemetry;
using WukongBenchAutomator.Vision;

namespace WukongBenchAutomator.Tests;

internal static class Make
{
    public static BenchmarkResult Result(double fpsAvg, double cpuMs, double gpuMs, int frames = 200, double? fpsMin = null) =>
        ResultParser.Parse(Encoding.UTF8.GetBytes(ResultParserTests.SampleJson(
            Enumerable.Range(0, frames).Select(i => (Fps: 1000 / Math.Max(cpuMs, gpuMs) * (1 + (i % 10 - 5) * 0.01), Cpu: cpuMs, Gpu: gpuMs))
        ).Replace("\"FPSAvg\": 61.2", $"\"FPSAvg\": {fpsAvg.ToString(System.Globalization.CultureInfo.InvariantCulture)}")
         .Replace("\"FPSMin\": 42.3", $"\"FPSMin\": {(fpsMin ?? fpsAvg * 0.7).ToString(System.Globalization.CultureInfo.InvariantCulture)}")), "x");

    public static BenchmarkProfile Profile(ProfileKind kind, string id = "p") => new()
    {
        Id = id,
        Title = kind switch { ProfileKind.Cpu => "CPU-тест", ProfileKind.Gpu => "GPU-тест", _ => id },
        Kind = kind,
        Resolution = new Resolution(1920, 1080),
        RenderScalePercent = kind == ProfileKind.Cpu ? 50 : 100,
        Quality = kind == ProfileKind.Cpu ? 1 : 5,
        RayTracing = kind == ProfileKind.Gpu,
    };

    public static PassReport Pass(ProfileKind kind, BenchmarkResult result, string? id = null)
    {
        var pass = new PassReport { Profile = Profile(kind, id ?? kind.ToString().ToLowerInvariant()), Result = result };
        pass.Validity = ValidityChecker.Assess(pass);
        return pass;
    }
}

public class RunStatisticsTests
{
    [Fact]
    public void Spread_MeanStdDevAndCv()
    {
        var spread = MetricSpread.Of([100, 102, 98, null, double.NaN])!;

        Assert.Equal(100, spread.Mean, 6);
        Assert.Equal(2, spread.StdDev, 6); // выборочное σ
        Assert.Equal(98, spread.Min);
        Assert.Equal(102, spread.Max);
        Assert.Equal(2, spread.CvPercent, 6);
    }

    [Fact]
    public void Aggregate_AveragesSummaryAndPoolsFrames()
    {
        var a = Make.Result(100, 9, 4, frames: 100);
        var b = Make.Result(110, 9, 4, frames: 150);

        var merged = RunAggregator.Aggregate([a, b]);
        var stats = RunStatistics.Compute([a, b]);

        Assert.Equal(105, merged.FpsAvg!.Value, 6);
        Assert.Equal(250, merged.Records.Count);
        Assert.Equal(250, merged.Stats!.FrameCount);
        Assert.Equal(2, stats.Runs);
        Assert.Equal(105, stats.FpsAvg!.Mean, 6);
        Assert.Same(a, RunAggregator.Aggregate([a]));
    }
}

public class ValidityAndVerdictTests
{
    [Fact]
    public void CpuPass_Valid_WhenMostFramesCpuBound()
    {
        var pass = Make.Pass(ProfileKind.Cpu, Make.Result(120, 8, 3));
        Assert.Equal(ValidityStatus.Valid, pass.Validity!.Status);
    }

    [Fact]
    public void CpuPass_Questionable_WhenGpuBound()
    {
        var pass = Make.Pass(ProfileKind.Cpu, Make.Result(60, 8, 16));
        Assert.Equal(ValidityStatus.Questionable, pass.Validity!.Status);
    }

    [Fact]
    public void GpuPass_Valid_WhenGpuBound_AndCustomIsUnknown()
    {
        Assert.Equal(ValidityStatus.Valid, Make.Pass(ProfileKind.Gpu, Make.Result(40, 8, 25)).Validity!.Status);
        Assert.Equal(ValidityStatus.Unknown, Make.Pass(ProfileKind.Custom, Make.Result(40, 8, 25)).Validity!.Status);
    }

    [Fact]
    public void Verdict_NamesGpuAsBottleneck_AndRatesPlayability()
    {
        var report = new SessionReport { StartedAt = DateTime.Now };
        report.Passes.Add(Make.Pass(ProfileKind.Cpu, Make.Result(120, 8, 3)));
        report.Passes.Add(Make.Pass(ProfileKind.Gpu, Make.Result(40, 8, 25)));
        report.Preflight = [new PreflightItem(true, "батарея")];

        var verdict = VerdictBuilder.Build(report);

        Assert.Contains(verdict, l => l.Text.Contains("упираемся в GPU") && l.Text.Contains("3.0 раза"));
        Assert.Contains(verdict, l => l.Text.Contains("30-60 FPS"));
        Assert.Contains(verdict, l => l.Level == VerdictLevel.Good && l.Text.StartsWith("CPU-тест: ок"));
        Assert.Contains(verdict, l => l.Level == VerdictLevel.Warning && l.Text.Contains("перед стартом"));
    }

    [Fact]
    public void Verdict_FlagsUnstableRuns()
    {
        var pass = Make.Pass(ProfileKind.Gpu, Make.Result(40, 8, 25));
        pass.RunStatistics = RunStatistics.Compute([Make.Result(40, 8, 25), Make.Result(50, 8, 25)]);
        var report = new SessionReport { StartedAt = DateTime.Now };
        report.Passes.Add(pass);

        Assert.Contains(VerdictBuilder.Build(report), l => l.Level == VerdictLevel.Warning && l.Text.Contains("разброс"));
    }
}

public class CustomProfileLoaderTests
{
    private static readonly DisplayInfo Display = new(new Resolution(2560, 1440), 144, [new(1920, 1080), new(2560, 1440), new(3840, 2160)]);

    private static BenchmarkProfile BuiltIn(string id) => Make.Profile(id == "cpu" ? ProfileKind.Cpu : ProfileKind.Gpu, id);

    [Fact]
    public void Load_BuiltInsAndCustomWithDefaults()
    {
        const string json = """
            [ "cpu", "GPU",
              // серия разрешений
              { "id": "gpu-4k", "title": "GPU 4K", "kind": "gpu", "resolution": "3840x2160", "rayTracing": "auto" },
              { "id": "low-1080p", "resolution": "1920x1080", "quality": 1, "renderScale": 67, "rayTracing": false }, ]
            """;

        var profiles = CustomProfileLoader.Load(json, "p.json", BuiltIn, Display, rayTracingAuto: true);

        Assert.Equal(new[] { "cpu", "gpu", "gpu-4k", "low-1080p" }, profiles.Select(p => p.Id));
        var fourK = profiles[2];
        Assert.Equal(ProfileKind.Gpu, fourK.Kind);
        Assert.Equal(new Resolution(3840, 2160), fourK.Resolution);
        Assert.Equal(5, fourK.Quality);
        Assert.Equal(100, fourK.RenderScalePercent);
        Assert.True(fourK.RayTracing);
        Assert.Equal(4, fourK.RayTracingLevel);
        var low = profiles[3];
        Assert.Equal("low-1080p", low.Title);
        Assert.Equal(ProfileKind.Custom, low.Kind);
        Assert.Equal(67, low.RenderScalePercent);
        Assert.False(low.RayTracing);
    }

    [Fact]
    public void ShippedSampleProfiles_AreValid()
    {
        var json = File.ReadAllText(TestFiles.Path("profiles-resolution-sweep.json"));

        var profiles = CustomProfileLoader.Load(json, "sample", BuiltIn, Display, rayTracingAuto: true);

        Assert.Equal(6, profiles.Count);
        Assert.Equal(new Resolution(3840, 2160), profiles.Single(p => p.Id == "gpu-4k").Resolution);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[\"vr\"]")]
    [InlineData("[{\"title\":\"no id\"}]")]
    [InlineData("[{\"id\":\"bad id\"}]")]
    [InlineData("[{\"id\":\"a\",\"quality\":7}]")]
    [InlineData("[{\"id\":\"a\",\"resolution\":\"huge\"}]")]
    [InlineData("[{\"id\":\"a\",\"kind\":\"ram\"}]")]
    [InlineData("[\"cpu\",\"cpu\"]")]
    [InlineData("[ not json")]
    public void Load_Invalid_Throws(string json)
    {
        Assert.Throws<FormatException>(() => CustomProfileLoader.Load(json, "p.json", BuiltIn, Display, rayTracingAuto: false));
    }
}

public class HistoryTests
{
    private static SessionReport Report(DateTime when, double cpuFps, double gpuFps)
    {
        var report = new SessionReport { StartedAt = when };
        report.Passes.Add(Make.Pass(ProfileKind.Cpu, Make.Result(cpuFps, 8, 3), "cpu"));
        report.Passes.Add(Make.Pass(ProfileKind.Gpu, Make.Result(gpuFps, 8, 25), "gpu"));
        return report;
    }

    [Fact]
    public void AppendAndFindPrevious_ThenCompare()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wb-history-{Guid.NewGuid():N}.jsonl");
        try
        {
            History.Append(path, Report(new DateTime(2026, 10, 1), 100, 40));
            History.Append(path, Report(new DateTime(2026, 10, 5), 104, 41));
            var current = Report(new DateTime(2026, 10, 8), 110, 44);

            var previous = History.FindPrevious(path, current);
            var table = History.Compare(current, previous, "прошлый прогон");

            Assert.Equal(new DateTime(2026, 10, 5), previous!.Date);
            Assert.NotNull(table);
            var cpuRow = table!.Rows.First(r => r.Label.StartsWith("CPU-тест: средний FPS"));
            Assert.Equal(new[] { "110.0", "104.0", "+5.8%" }, cpuRow.Values);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadReport_ReadsOwnReportJson()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wb-report-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(Report(new DateTime(2026, 9, 30), 90, 35)));

            var baseline = History.LoadReport(path);

            Assert.Equal(2, baseline.Passes.Count);
            Assert.Equal("cpu", baseline.Passes[0].Id);
            Assert.Equal(90, baseline.Passes[0].FpsAvg);
            Assert.Equal("1920x1080", baseline.Passes[0].Resolution);
            Assert.NotNull(baseline.Passes[1].Low1Fps);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class TelemetryTests
{
    [Fact]
    public void GameGpuUtilization_SumsPerEngineAndTakesMax()
    {
        (string, double)[] engines =
        [
            ("pid_100_luid_0x00000000_0x0000F8BE_phys_0_eng_0_engtype_3D", 60),
            ("pid_100_luid_0x00000000_0x0000F8BE_phys_0_eng_0_engtype_3D", 25), // второй экземпляр того же движка
            ("pid_100_luid_0x00000000_0x0000F8BE_phys_0_eng_2_engtype_Compute 0", 30),
            ("pid_200_luid_0x00000000_0x0000F8BE_phys_0_eng_0_engtype_3D", 10), // чужой процесс
        ];

        Assert.Equal(85, TelemetrySampler.GameGpuUtilization(engines, 100));
        Assert.Equal(60 + 25 + 10, TelemetrySampler.GameGpuUtilization(engines, 999)); // игры нет - вся система
        Assert.Null(TelemetrySampler.GameGpuUtilization([], 100));
    }

    [Fact]
    public void GameVideoMemory_SumsInstancesOfProcess()
    {
        (string, double)[] memory =
        [
            ("pid_100_luid_0x00000000_0x0000F8BE_phys_0", 3.0 * 1024 * 1024 * 1024),
            ("pid_100_luid_0x00000000_0x0000AAAA_phys_0", 1.0 * 1024 * 1024 * 1024),
            ("pid_1000_luid_0x00000000_0x0000F8BE_phys_0", 5.0 * 1024 * 1024 * 1024),
        ];

        Assert.Equal(4096, TelemetrySampler.GameVideoMemoryMb(memory, 100));
    }

    [Fact]
    public void Summary_AveragesAndMaxima_AndTestWindowCutsMenu()
    {
        var t0 = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var samples = Enumerable.Range(0, 200)
            .Select(i => new TelemetrySample(t0.AddSeconds(i), i < 50 ? 5 : 40, 90, 4500, 30, i < 50 ? 10 : 99, 8000, 12, 70, 250, 2600))
            .ToList();

        var window = PassRunner.TestWindow(samples, t0.AddSeconds(199), 146);
        var summary = TelemetrySummary.From(window)!;

        Assert.Equal(147, window.Count); // секунды 53..199 включительно - меню и загрузка отрезаны
        Assert.Equal(40, summary.CpuTotalAvg);
        Assert.Equal(99, summary.GpuUtilAvg);
        Assert.Equal(70, summary.GpuTempMax);
        Assert.Equal(8000, summary.GameVramMaxMb);
    }

    [Fact]
    public void NvidiaSmi_ParsesLineAndNotAvailable()
    {
        var now = DateTime.UtcNow;
        var r = NvidiaSmiReader.Parse("64, 215.30, 2610, 98", now)!;
        Assert.Equal(64, r.TempC);
        Assert.Equal(215.3, r.PowerW);
        Assert.Equal(2610, r.ClockMhz);
        Assert.Null(NvidiaSmiReader.Parse("64, [N/A], 2610, 98", now)!.PowerW);
        Assert.Null(NvidiaSmiReader.Parse("garbage", now));
    }
}

public class VisionTests
{
    private static readonly Size Screen = new(1920, 1080);

    [Fact]
    public void FindButton_PrefersShortLabelNearExpectedPosition()
    {
        TextLine[] lines =
        [
            new("Black Myth: Wukong Benchmark Tool", new RectangleF(800, 40, 400, 30)),
            new("Benchmark", new RectangleF(160, 480, 140, 28)),
            new("Тест быстродействия", new RectangleF(120, 485, 250, 30)),
        ];

        var point = ScreenTextReader.FindButton(lines, Game.MenuNavigator.BenchmarkKeywords, new PointF(0.113f, 0.457f), Screen);

        Assert.NotNull(point);
        Assert.InRange(point!.Value.Y, 480, 516);
        Assert.InRange(point.Value.X, 120, 370);
    }

    [Fact]
    public void FindButton_ToleratesOcrTypos_AndRejectsFarMatches()
    {
        // OCR спутал 'й' с 'и', но ключ 'быстроде' всё равно совпадает
        TextLine[] typo = [new("Тест быстродеиствия", new RectangleF(600, 470, 250, 30))];
        Assert.NotNull(ScreenTextReader.FindButton(typo, Game.MenuNavigator.BenchmarkKeywords, new PointF(0.3f, 0.45f), Screen));

        TextLine[] far = [new("Подтвердить", new RectangleF(1800, 1040, 100, 30))];
        Assert.Null(ScreenTextReader.FindButton(far, Game.MenuNavigator.ConfirmKeywords, new PointF(0.1f, 0.1f), Screen));
    }

    [Theory]
    [InlineData("подтвердить", "подтвердить", 0)]
    [InlineData("подтвердить", "одтвердить", 1)]         // OCR потерял первую букву у края плашки
    [InlineData("подтвердить", "нажмитеподтвердить", 0)]
    [InlineData("тестбыстродействия", "естбыстродеиствия", 2)]
    public void ApproximateSubstringDistance_Cases(string key, string text, int expected)
    {
        Assert.Equal(expected, ScreenTextReader.ApproximateSubstringDistance(key, text));
    }

    [Fact]
    public void ApproximateSubstringDistance_UnrelatedWordsAreFarBeyondTolerance()
    {
        Assert.True(ScreenTextReader.ApproximateSubstringDistance("benchmark", "settings") > ScreenTextReader.AllowedEdits("benchmark") + 3);
        Assert.True(ScreenTextReader.ApproximateSubstringDistance("подтвердить", "отмена") > ScreenTextReader.AllowedEdits("подтвердить") + 3);
    }

    [Fact]
    public void FindButton_ToleratesClippedFirstLetter()
    {
        // реальный вывод OCR со снимка из сквозного теста
        TextLine[] lines =
        [
            new("ест быстродействия", new RectangleF(441, 679, 229, 25)),
            new("одтвердить", new RectangleF(1079, 771, 130, 19)),
        ];

        var confirm = ScreenTextReader.FindButton(lines, Game.MenuNavigator.ConfirmKeywords, new PointF(0.393f, 0.580f), Screen);

        Assert.NotNull(confirm);
        Assert.InRange(confirm!.Value.X, 1079, 1209);
    }

    [Fact]
    public void FindButton_IgnoresTitlesThatMerelyContainKeyword()
    {
        // заголовок на заставке не должен считаться кнопкой
        TextLine[] splash = [new("FAKE BENCHMARK - state: splash", new RectangleF(30, 30, 500, 40))];
        Assert.Null(ScreenTextReader.FindButton(splash, Game.MenuNavigator.BenchmarkKeywords, new PointF(0.113f, 0.457f), Screen));
    }

    [Fact]
    public void Normalize_KeepsLettersOnly()
    {
        Assert.Equal("тестбыстродействия", ScreenTextReader.Normalize("  Тест  быстродействия! "));
        Assert.Equal("基准测试", ScreenTextReader.Normalize("基准 测试"));
    }

    [Fact]
    public void Png_ValidStructure_AndCrc()
    {
        Assert.Equal(0xCBF43926u, PngWriter.Crc32(Encoding.ASCII.GetBytes("123456789")) ^ 0xFFFFFFFF);

        var pixels = new byte[40 * 20 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)i;
            pixels[i + 3] = 255;
        }

        var path = Path.Combine(Path.GetTempPath(), $"wb-{Guid.NewGuid():N}.png");
        try
        {
            PngWriter.Save(new CapturedImage(40, 20, pixels), path, maxWidth: 20);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);
            Assert.Equal("IHDR", Encoding.ASCII.GetString(bytes, 12, 4));
            Assert.Equal(20, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16))); // уменьшено вдвое
            Assert.Equal(10, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
            Assert.Equal("IEND", Encoding.ASCII.GetString(bytes, bytes.Length - 8, 4));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BlankDetection()
    {
        Assert.True(new CapturedImage(100, 100, new byte[100 * 100 * 4]).IsBlank());
        var bright = Enumerable.Repeat((byte)200, 100 * 100 * 4).ToArray();
        Assert.False(new CapturedImage(100, 100, bright).IsBlank());
    }
}

public class StutterTests
{
    [Fact]
    public void CountsFramesLongerThanTwiceMedian()
    {
        var records = Enumerable.Repeat(new FrameRecord(100, 5, 5, 0, 0, 0), 597)
            .Concat([new FrameRecord(40, 5, 5, 0, 0, 0), new FrameRecord(30, 5, 5, 0, 0, 0), new FrameRecord(60, 5, 5, 0, 0, 0)])
            .ToList();

        var stats = FrameStats.Compute(records)!;

        Assert.Equal(2, stats.StutterCount); // 25 и 33 мс > 2×10 мс; 16.7 мс - нет
    }
}
