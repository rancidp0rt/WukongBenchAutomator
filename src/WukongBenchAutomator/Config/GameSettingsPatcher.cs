using System.Globalization;
using WukongBenchAutomator.Profiles;

namespace WukongBenchAutomator.Config;

internal sealed record SettingChange(string Location, string Key, string? OldValue, string NewValue);

internal sealed class PatchResult
{
    public List<SettingChange> Changes { get; } = [];

    public Dictionary<string, string> PreservedUiValues { get; } = new(StringComparer.OrdinalIgnoreCase);
}

// Настройки лежат в двух местах: UISettingData (значения меню) и [ScalabilityGroups] (sg.* = значение меню - 1).
// При старте игра сверяет одно с другим, поэтому пишем оба. ImageQuality в UISettingData - высота рендера в пикселях.
internal static class GameSettingsPatcher
{
    public const string UiKey = "UISettingData";

    private static readonly string[] PreservedKeys =
        ["SuperResolutionSampling", "MotionBlur", "ScreenResolution", "WindowFullImageQuality", "Brightness"];

    public static PatchResult Apply(IniFile ini, BenchmarkProfile profile)
    {
        var main = FindMainSection(ini);
        var ui = UiSettingData.Parse(ini.Get(main, UiKey) ?? "()");
        var result = new PatchResult();

        void SetUi(string key, int value)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            var old = ui[key];
            if (old != text)
            {
                result.Changes.Add(new SettingChange(UiKey, key, old, text));
            }

            ui[key] = text;
        }

        void SetIni(string section, string key, string value, bool onlyIfExists = false)
        {
            var old = ini.Get(section, key);
            if (onlyIfExists && old is null)
            {
                return;
            }

            if (old != value)
            {
                result.Changes.Add(new SettingChange(section, key, old, value));
            }

            ini.Set(section, key, value);
        }

        // UISettingData
        SetUi("ScreenMode", 0); // 0 - полноэкранный режим
        SetUi("ImageQuality", profile.RenderHeight);
        SetUi("Vsync", 0);
        SetUi("LockFrameRate", 0);
        SetUi("InsertFrame", 0); // генерация кадров
        SetUi("QualityLevel", profile.Quality);
        foreach (var group in GameSettingKeys.QualityGroups)
        {
            SetUi(group.UiKey, profile.Quality);
        }

        SetUi("Rtx", profile.RayTracing ? 1 : 0);
        if (profile.RayTracing)
        {
            SetUi("RtxLevel", profile.RayTracingLevel);
        }

        foreach (var key in PreservedKeys)
        {
            if (ui[key] is { } value)
            {
                result.PreservedUiValues[key] = value;
            }
        }

        ini.Set(main, UiKey, ui.Serialize());

        // стандартные поля UGameUserSettings
        var width = profile.Resolution.Width.ToString(CultureInfo.InvariantCulture);
        var height = profile.Resolution.Height.ToString(CultureInfo.InvariantCulture);
        SetIni(main, "ResolutionSizeX", width);
        SetIni(main, "ResolutionSizeY", height);
        SetIni(main, "LastUserConfirmedResolutionSizeX", width);
        SetIni(main, "LastUserConfirmedResolutionSizeY", height);
        SetIni(main, "DesiredScreenWidth", width, onlyIfExists: true);
        SetIni(main, "DesiredScreenHeight", height, onlyIfExists: true);
        SetIni(main, "LastUserConfirmedDesiredScreenWidth", width, onlyIfExists: true);
        SetIni(main, "LastUserConfirmedDesiredScreenHeight", height, onlyIfExists: true);
        SetIni(main, "FullscreenMode", "0");
        SetIni(main, "LastConfirmedFullscreenMode", "0");
        SetIni(main, "PreferredFullscreenMode", "0");
        SetIni(main, "bUseVSync", "False");
        SetIni(main, "bUseDynamicResolution", "False");
        SetIni(main, "FrameRateLimit", "0.000000");

        // [ScalabilityGroups]
        var sg = GameSettingKeys.ScalabilitySection;
        SetIni(sg, "sg.ResolutionQuality", profile.RenderScalePercent.ToString(CultureInfo.InvariantCulture));
        var engineQuality = (profile.Quality - 1).ToString(CultureInfo.InvariantCulture);
        foreach (var group in GameSettingKeys.QualityGroups)
        {
            SetIni(sg, group.ScalabilityKey, engineQuality);
        }

        var rtQuality = profile.RayTracing ? profile.RayTracingLevel - 1 : 0;
        SetIni(sg, "sg.RayTracingQuality", rtQuality.ToString(CultureInfo.InvariantCulture));

        return result;
    }

    public static string FindMainSection(IniFile ini) =>
        ini.FindSectionContainingKey(UiKey)
        ?? ini.Sections.FirstOrDefault(s => s.EndsWith("GSGameUserSettings", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException(
            "В GameUserSettings.ini нет UISettingData. Запустите Benchmark Tool вручную один раз, "
            + "чтобы он создал свои настройки, и повторите.");
}
