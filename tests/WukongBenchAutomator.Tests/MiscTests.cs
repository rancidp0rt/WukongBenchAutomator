using System.Drawing;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Steam;
using WukongBenchAutomator.SystemInfo;

namespace WukongBenchAutomator.Tests;

public class VdfTests
{
    [Fact]
    public void Parse_LibraryFolders_WithEscapedPaths()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "apps" { "228980" "261575852" }
                }
                // комментарий
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                    "apps" { "3132990" "8123456789" }
                }
            }
            """;

        var root = VdfNode.Parse(vdf);
        var libraries = root["libraryfolders"]!;

        Assert.Equal(@"C:\Program Files (x86)\Steam", libraries["0"]!.GetString("path"));
        Assert.Equal(@"D:\SteamLibrary", libraries["1"]!.GetString("PATH"));
        Assert.Equal("8123456789", libraries["1"]!["apps"]!.GetString("3132990"));
    }

    [Fact]
    public void Parse_AppManifest()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"3132990"
                "name"		"Black Myth: Wukong Benchmark Tool"
                "installdir"		"BlackMythWukongBenchmarkTool"
                "buildid"		"15392184"
            }
            """;

        var state = VdfNode.Parse(acf)["AppState"]!;

        Assert.Equal("BlackMythWukongBenchmarkTool", state.GetString("installdir"));
        Assert.Equal("15392184", state.GetString("buildid"));
    }

    [Theory]
    [InlineData("\"a\" {")]
    [InlineData("\"a\" \"b\" }")]
    [InlineData("\"unterminated")]
    public void Parse_Malformed_Throws(string text)
    {
        Assert.Throws<FormatException>(() => VdfNode.Parse(text));
    }
}

public class OptionsTests
{
    [Fact]
    public void Defaults()
    {
        var o = Options.Parse([]);

        Assert.Equal(Command.Run, o.Command);
        Assert.True(o.RunCpu);
        Assert.True(o.RunGpu);
        Assert.Equal(RayTracingMode.Auto, o.RayTracing);
        Assert.Equal(new Resolution(1280, 720), o.CpuResolution);
        Assert.Equal(50, o.CpuRenderScale);
        Assert.Equal("auto", o.GpuResolution);
        Assert.Equal(LaunchMode.Steam, o.Launch);
    }

    [Fact]
    public void Parse_AllOptions()
    {
        var o = Options.Parse(
        [
            "--game-dir", @"D:\Games\Bench", "--only", "gpu", "--rt", "off", "--cpu-res", "1600x900", "--cpu-scale", "25",
            "--gpu-res", "3840x2160", "--timeout", "30", "--menu-delay", "40", "--launch", "direct", "--keep-settings",
            "--click-benchmark", "0.12,0.47", "--click-confirm", "0.4,0.6",
        ]);

        Assert.Equal(@"D:\Games\Bench", o.GameDir);
        Assert.False(o.RunCpu);
        Assert.True(o.RunGpu);
        Assert.Equal(RayTracingMode.Off, o.RayTracing);
        Assert.Equal(new Resolution(1600, 900), o.CpuResolution);
        Assert.Equal(25, o.CpuRenderScale);
        Assert.Equal("3840x2160", o.GpuResolution);
        Assert.Equal(TimeSpan.FromMinutes(30), o.PassTimeout);
        Assert.Equal(TimeSpan.FromSeconds(40), o.MenuDelay);
        Assert.Equal(LaunchMode.Direct, o.Launch);
        Assert.True(o.KeepSettings);
        Assert.Equal(new PointF(0.12f, 0.47f), o.BenchmarkButton);
        Assert.Equal(new PointF(0.4f, 0.6f), o.ConfirmButton);
    }

    [Fact]
    public void Parse_Report()
    {
        var o = Options.Parse(["--report", "cpu.json", "gpu.json", "--output", "out"]);

        Assert.Equal(Command.Report, o.Command);
        Assert.Equal("cpu.json", o.ReportCpuFile);
        Assert.Equal("gpu.json", o.ReportGpuFile);
        Assert.Equal("out", o.OutputRoot);
    }

    [Fact]
    public void Parse_PowerOptions()
    {
        var o = Options.Parse(["--only", "cpu,gpu-4k", "--runs", "3", "--warmup", "--profiles", "p.json", "--compare", "old.json", "--no-adaptive", "--no-ocr"]);

        Assert.True(o.RunCpu);
        Assert.False(o.RunGpu);
        Assert.True(o.Includes("GPU-4K"));
        Assert.Equal(3, o.Runs);
        Assert.True(o.Warmup);
        Assert.Equal("p.json", o.ProfilesFile);
        Assert.Equal("old.json", o.CompareFile);
        Assert.False(o.Adaptive);
        Assert.False(o.UseOcr);
    }

    [Theory]
    [InlineData("--only", "cpu gpu")]
    [InlineData("--runs", "0")]
    [InlineData("--runs", "11")]
    [InlineData("--cpu-scale", "10")]
    [InlineData("--rt", "maybe")]
    [InlineData("--gpu-res", "big")]
    [InlineData("--click-confirm", "2,3")]
    [InlineData("--unknown", "x")]
    public void Parse_InvalidValues_Throw(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => Options.Parse([name, value]));
    }
}

public class ProfileTests
{
    private static DisplayInfo Display(int w, int h, params (int W, int H)[] modes) =>
        new(new Resolution(w, h), 60, modes.Select(m => new Resolution(m.W, m.H)).ToList());

    [Fact]
    public void GpuResolution_Native16By9()
    {
        var (resolution, _) = ProfileFactory.ChooseGpuResolution("auto", Display(2560, 1440, (1920, 1080), (2560, 1440)));
        Assert.Equal(new Resolution(2560, 1440), resolution);
    }

    [Fact]
    public void GpuResolution_16By10Monitor_PicksLargest16By9Mode()
    {
        var display = Display(1920, 1200, (1280, 720), (1680, 1050), (1920, 1080), (1920, 1200));

        var (resolution, note) = ProfileFactory.ChooseGpuResolution("auto", display);

        Assert.Equal(new Resolution(1920, 1080), resolution);
        Assert.Contains("16:9", note);
    }

    [Fact]
    public void GpuResolution_NativeAndExplicit()
    {
        var display = Display(3440, 1440, (2560, 1440), (3440, 1440));

        Assert.Equal(new Resolution(3440, 1440), ProfileFactory.ChooseGpuResolution("native", display).Resolution);
        Assert.Equal(new Resolution(2560, 1440), ProfileFactory.ChooseGpuResolution("auto", display).Resolution);
        Assert.Equal(new Resolution(1920, 1080), ProfileFactory.ChooseGpuResolution("1920x1080", display).Resolution);
    }

    [Fact]
    public void CpuProfile_IsLowAndWithoutRayTracing()
    {
        var cpu = ProfileFactory.CreateCpu(Options.Parse([]));

        Assert.Equal(1, cpu.Quality);
        Assert.False(cpu.RayTracing);
        Assert.Equal(640, cpu.RenderWidth);
        Assert.Equal(360, cpu.RenderHeight);
        Assert.NotEmpty(cpu.Rationale);
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", true)]
    [InlineData("AMD Radeon RX 7900 XTX", true)]
    [InlineData("AMD Radeon RX 6600", true)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", true)]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", false)]
    [InlineData("AMD Radeon RX 5700 XT", false)]
    [InlineData("AMD Radeon(TM) Graphics", false)]
    public void RayTracingNameHeuristic(string name, bool expected)
    {
        Assert.Equal(expected, BenchmarkSession.LooksRayTracingCapable(name));
    }

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("2560 × 1440", 2560, 1440)]
    [InlineData(" 1280*720 ", 1280, 720)]
    public void Resolution_TryParse(string text, int w, int h)
    {
        Assert.True(Resolution.TryParse(text, out var r));
        Assert.Equal(new Resolution(w, h), r);
    }
}
