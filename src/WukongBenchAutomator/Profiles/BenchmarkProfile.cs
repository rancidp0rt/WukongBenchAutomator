using WukongBenchAutomator.Cli;

namespace WukongBenchAutomator.Profiles;

internal enum ProfileKind
{
    Cpu,
    Gpu,
    Custom,
}

internal sealed record BenchmarkProfile
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public ProfileKind Kind { get; init; } = ProfileKind.Custom;
    public required Resolution Resolution { get; init; }
    public string? ResolutionNote { get; init; }
    public required int RenderScalePercent { get; init; }
    public required int Quality { get; init; }
    public required bool RayTracing { get; init; }

    public int RayTracingLevel { get; init; } = GameSettingKeys.MaxRayTracingLevel;

    public IReadOnlyList<string> Rationale { get; init; } = [];

    public bool FromResultFile { get; init; }

    public int RenderHeight => (int)Math.Round(Resolution.Height * RenderScalePercent / 100.0);
    public int RenderWidth => (int)Math.Round(Resolution.Width * RenderScalePercent / 100.0);
}

internal static class GameSettingKeys
{
    public const string ScalabilitySection = "ScalabilityGroups";
    public const int MaxQuality = 5;
    public const int MaxRayTracingLevel = 4;

    public sealed record QualityGroup(string UiKey, string ScalabilityKey, string Title);

    public static readonly IReadOnlyList<QualityGroup> QualityGroups =
    [
        new("ViewDistance", "sg.ViewDistanceQuality", "Дальность прорисовки"),
        new("AntiAliasing", "sg.AntiAliasingQuality", "Сглаживание"),
        new("PostProcessing", "sg.PostProcessQuality", "Постобработка"),
        new("ShadowQuality", "sg.ShadowQuality", "Тени"),
        new("TextureQuality", "sg.TextureQuality", "Текстуры"),
        new("FxQuality", "sg.EffectsQuality", "Эффекты"),
        new("MaterialQuality", "sg.ShadingQuality", "Материалы (шейдинг)"),
        new("VegetationQuality", "sg.FoliageQuality", "Растительность"),
        new("GlobalIllumination", "sg.GlobalIlluminationQuality", "Глобальное освещение"),
        new("ReflectionQuality", "sg.ReflectionQuality", "Отражения"),
    ];

    public static string QualityName(int level) => level switch
    {
        1 => "Низкое",
        2 => "Среднее",
        3 => "Высокое",
        4 => "Сверхвысокое",
        5 => "Кинематографическое",
        _ => $"({level})",
    };

    public static string RayTracingLevelName(int level) => level switch
    {
        1 => "низкий",
        2 => "средний",
        3 => "высокий",
        4 => "сверхвысокий",
        _ => $"({level})",
    };
}
