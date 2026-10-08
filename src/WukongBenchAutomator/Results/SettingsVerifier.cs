using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Profiles;

namespace WukongBenchAutomator.Results;

internal sealed record VerificationItem(string Setting, string Expected, string Actual, bool Ok);

internal static class SettingsVerifier
{
    public static List<VerificationItem> Compare(BenchmarkProfile profile, BenchmarkResult result)
    {
        var items = new List<VerificationItem>();

        if (result.ScreenResolution is { } screen)
        {
            var ok = Resolution.TryParse(screen, out var actual) && actual == profile.Resolution;
            items.Add(new VerificationItem("Разрешение", profile.Resolution.ToString(), screen, ok));
        }

        if (result.QualityLevel is { } quality)
        {
            items.Add(new VerificationItem(
                "Пресет качества",
                GameSettingKeys.QualityName(profile.Quality),
                GameSettingKeys.QualityName(quality),
                quality == profile.Quality));
        }

        if (result.ImageQuality is { } image)
        {
            // в результате масштаб в %, но на всякий случай принимаем и высоту в пикселях
            var actual = image <= 100 ? $"{image}%" : $"{image} px";
            items.Add(new VerificationItem(
                "Масштаб рендера",
                $"{profile.RenderScalePercent}%",
                actual,
                image == profile.RenderScalePercent || image == profile.RenderHeight));
        }

        if (result.Rtx is { } rtx)
        {
            items.Add(new VerificationItem(
                "Трассировка лучей",
                profile.RayTracing ? $"вкл (уровень {profile.RayTracingLevel})" : "выкл",
                rtx > 0 ? $"вкл (уровень {rtx})" : "выкл",
                (rtx > 0) == profile.RayTracing));
        }

        if (result.InsertFrame is { } frameGeneration)
        {
            items.Add(new VerificationItem(
                "Генерация кадров",
                "выкл",
                frameGeneration == 0 ? "выкл" : $"вкл ({frameGeneration})",
                frameGeneration == 0));
        }

        return items;
    }

    public static BenchmarkProfile ProfileFromResult(string id, string title, BenchmarkResult result)
    {
        Resolution.TryParse(result.ScreenResolution, out var resolution);
        return new BenchmarkProfile
        {
            Id = id,
            Title = title,
            Kind = id switch
            {
                "cpu" => ProfileKind.Cpu,
                "gpu" => ProfileKind.Gpu,
                _ => ProfileKind.Custom,
            },
            Resolution = resolution,
            RenderScalePercent = result.ImageQuality is { } iq and > 0 and <= 100 ? iq : 100,
            Quality = result.QualityLevel ?? 0,
            RayTracing = result.Rtx > 0,
            RayTracingLevel = result.Rtx ?? 0,
            FromResultFile = true,
        };
    }
}
