using System.Text;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Config;
using WukongBenchAutomator.Profiles;

namespace WukongBenchAutomator.Tests;

public class IniFileTests
{
    [Fact]
    public void Set_ReplacesValue_AndKeepsOtherLinesIntact()
    {
        const string text = "[A]\r\nKey=1\r\n; comment\r\nOther=x\r\n\r\n[B]\r\nKey=2\r\n";
        var ini = IniFile.Parse(text);

        ini.Set("A", "key", "42");

        Assert.Equal("[A]\r\nKey=42\r\n; comment\r\nOther=x\r\n\r\n[B]\r\nKey=2\r\n", ini.ToText());
    }

    [Fact]
    public void Set_AppendsNewKey_BeforeBlankSeparator()
    {
        var ini = IniFile.Parse("[A]\nX=1\n\n[B]\nY=2\n");

        ini.Set("A", "Z", "3");

        Assert.Equal("[A]\nX=1\nZ=3\n\n[B]\nY=2\n", ini.ToText());
    }

    [Fact]
    public void Set_CreatesMissingSection()
    {
        var ini = IniFile.Parse("[A]\nX=1\n");

        ini.Set("ScalabilityGroups", "sg.ShadowQuality", "0");

        Assert.Equal("[A]\nX=1\n\n[ScalabilityGroups]\nsg.ShadowQuality=0\n", ini.ToText());
        Assert.Equal("0", ini.Get("scalabilitygroups", "SG.SHADOWQUALITY"));
    }

    [Fact]
    public void Utf16WithBom_RoundTripsByteForByte()
    {
        const string text = "[/Script/GSGameSettings.GSGameUserSettings]\r\nUISettingData=((\"Rtx\", \"0\"))\r\nВерсия=5\r\n";
        var bytes = new UnicodeEncoding(false, true).GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(text))
            .ToArray();

        var ini = IniFile.FromBytes(bytes);

        Assert.Equal(bytes, ini.ToBytes());
        Assert.Equal("((\"Rtx\", \"0\"))", ini.Get("/Script/GSGameSettings.GSGameUserSettings", "UISettingData"));
    }

    [Fact]
    public void FindSectionContainingKey_FindsUiSettingSection()
    {
        var ini = IniFile.Load(TestFiles.Path("GameUserSettings.ini"));

        Assert.Equal("/Script/GSGameSettings.GSGameUserSettings", ini.FindSectionContainingKey("UISettingData"));
    }
}

public class UiSettingDataTests
{
    private const string Raw = """(("ScreenMode", "1"),("ImageQuality", "720"),("Rtx", "0"),("Brightness", "50"))""";

    [Fact]
    public void ParseAndSerialize_RoundTripsExactly()
    {
        Assert.Equal(Raw, UiSettingData.Parse(Raw).Serialize());
    }

    [Fact]
    public void Indexer_ReplacesExistingAndAppendsNewKeys()
    {
        var data = UiSettingData.Parse(Raw);

        data["rtx"] = "1";
        data["RtxLevel"] = "4";

        Assert.Equal("""(("ScreenMode", "1"),("ImageQuality", "720"),("Rtx", "1"),("Brightness", "50"),("RtxLevel", "4"))""", data.Serialize());
        Assert.Equal(720, data.GetInt("ImageQuality"));
    }

    [Fact]
    public void Parse_UnknownFormat_Throws()
    {
        Assert.Throws<FormatException>(() => UiSettingData.Parse("ScreenMode=1;Rtx=0"));
    }

    [Fact]
    public void Parse_Empty_IsAllowed()
    {
        Assert.Empty(UiSettingData.Parse("()").Entries);
    }
}

public class GameSettingsPatcherTests
{
    private const string Main = "/Script/GSGameSettings.GSGameUserSettings";

    private static BenchmarkProfile Cpu() => ProfileFactory.CreateCpu(Options.Parse([]));

    private static BenchmarkProfile Gpu(bool rt) => new()
    {
        Id = "gpu",
        Title = "GPU",
        Resolution = new Resolution(2560, 1440),
        RenderScalePercent = 100,
        Quality = 5,
        RayTracing = rt,
    };

    [Fact]
    public void CpuProfile_WritesLowSettingsToBothRepresentations()
    {
        var ini = IniFile.Load(TestFiles.Path("GameUserSettings.ini"));

        var result = GameSettingsPatcher.Apply(ini, Cpu());
        var ui = UiSettingData.Parse(ini.Get(Main, "UISettingData")!);

        Assert.Equal("1", ui["QualityLevel"]);
        Assert.All(GameSettingKeys.QualityGroups, g => Assert.Equal("1", ui[g.UiKey]));
        Assert.All(GameSettingKeys.QualityGroups, g => Assert.Equal("0", ini.Get("ScalabilityGroups", g.ScalabilityKey)));
        Assert.Equal("360", ui["ImageQuality"]); // 50% от 720
        Assert.Equal("50", ini.Get("ScalabilityGroups", "sg.ResolutionQuality"));
        Assert.Equal("0", ui["Rtx"]);
        Assert.Equal("0", ini.Get("ScalabilityGroups", "sg.RayTracingQuality"));
        Assert.Equal("0", ui["InsertFrame"]);
        Assert.Equal("0", ui["Vsync"]);
        Assert.Equal("0", ui["LockFrameRate"]);
        Assert.Equal("0", ui["ScreenMode"]);
        Assert.Equal("1280", ini.Get(Main, "ResolutionSizeX"));
        Assert.Equal("720", ini.Get(Main, "LastUserConfirmedResolutionSizeY"));
        Assert.Equal("0", ini.Get(Main, "FullscreenMode"));
        Assert.Equal("False", ini.Get(Main, "bUseVSync"));
        Assert.Equal("0.000000", ini.Get(Main, "FrameRateLimit"));

        // То, что инструмент не трогает, остаётся как было.
        Assert.Equal("50", ui["Brightness"]);
        Assert.Equal("3", ui["SuperResolutionSampling"]);
        Assert.Equal("0", ini.Get(Main, "AudioQualityLevel"));
        Assert.Equal("False", ini.Get("/Script/Engine.GameUserSettings", "bUseDesiredScreenHeight"));
        Assert.Null(ini.Get(Main, "DesiredScreenWidth"));
        Assert.Equal("3", result.PreservedUiValues["SuperResolutionSampling"]);
        Assert.Contains(result.Changes, c => c.Key == "QualityLevel" && c.OldValue == "3" && c.NewValue == "1");
    }

    [Fact]
    public void GpuProfileWithRayTracing_EnablesMaxRtLevel()
    {
        var ini = IniFile.Load(TestFiles.Path("GameUserSettings.ini"));

        GameSettingsPatcher.Apply(ini, Gpu(rt: true));
        var ui = UiSettingData.Parse(ini.Get(Main, "UISettingData")!);

        Assert.Equal("5", ui["QualityLevel"]);
        Assert.All(GameSettingKeys.QualityGroups, g => Assert.Equal("4", ini.Get("ScalabilityGroups", g.ScalabilityKey)));
        Assert.Equal("1440", ui["ImageQuality"]);
        Assert.Equal("100", ini.Get("ScalabilityGroups", "sg.ResolutionQuality"));
        Assert.Equal("1", ui["Rtx"]);
        Assert.Equal("4", ui["RtxLevel"]);
        Assert.Equal("3", ini.Get("ScalabilityGroups", "sg.RayTracingQuality"));
        Assert.Equal("2560", ini.Get(Main, "ResolutionSizeX"));
    }

    [Fact]
    public void Apply_IsIdempotent()
    {
        var ini = IniFile.Load(TestFiles.Path("GameUserSettings.ini"));
        GameSettingsPatcher.Apply(ini, Gpu(rt: false));
        var once = ini.ToText();

        var second = GameSettingsPatcher.Apply(ini, Gpu(rt: false));

        Assert.Equal(once, ini.ToText());
        Assert.Empty(second.Changes);
    }

    [Fact]
    public void Apply_WithoutUiSettingData_Throws()
    {
        var ini = IniFile.Parse("[ScalabilityGroups]\nsg.ShadowQuality=2\n");

        Assert.Throws<InvalidDataException>(() => GameSettingsPatcher.Apply(ini, Cpu()));
    }
}

internal static class TestFiles
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", name);
}
