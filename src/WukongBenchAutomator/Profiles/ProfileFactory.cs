using WukongBenchAutomator.Cli;
using WukongBenchAutomator.SystemInfo;

namespace WukongBenchAutomator.Profiles;

internal static class ProfileFactory
{
    public static BenchmarkProfile CreateCpu(Options options, int? renderScaleOverride = null)
    {
        var scale = renderScaleOverride ?? options.CpuRenderScale;
        return CreateCpu(options.CpuResolution, scale);
    }

    private static BenchmarkProfile CreateCpu(Resolution resolution, int scale) => new()
    {
        Id = "cpu",
        Title = "CPU-тест",
        Kind = ProfileKind.Cpu,
        Resolution = resolution,
        RenderScalePercent = scale,
        Quality = 1,
        RayTracing = false,
        Rationale =
        [
            $"{resolution}, рендер {scale}% ({Scale(resolution, scale)}): видеокарте почти нечего делать, FPS упирается в CPU.",
            "Все настройки качества на минимуме: тени, GI, отражения и постобработка нагружают в основном GPU.",
            "Без трассировки лучей, генерации кадров, VSync и лимита FPS.",
            "Проверка: доля кадров, где CPU медленнее GPU. Если она меньше 80%, тест повторяется с рендером 25%.",
        ],
    };

    public static BenchmarkProfile CreateGpu(Options options, DisplayInfo display, bool rayTracing, string rayTracingReason)
    {
        var (resolution, note) = ChooseGpuResolution(options.GpuResolution, display);
        var rt = rayTracing
            ? $"Полная трассировка лучей на максимуме, это самая тяжёлая нагрузка для GPU ({rayTracingReason})."
            : $"Без трассировки лучей ({rayTracingReason}).";

        return new BenchmarkProfile
        {
            Id = "gpu",
            Title = "GPU-тест",
            Kind = ProfileKind.Gpu,
            Resolution = resolution,
            ResolutionNote = note,
            RenderScalePercent = 100,
            Quality = GameSettingKeys.MaxQuality,
            RayTracing = rayTracing,
            Rationale =
            [
                $"{resolution} ({note}), рендер 100% без апскейла.",
                "Все настройки качества на максимуме (кинематографическое).",
                rt,
                "Без генерации кадров, VSync и лимита FPS.",
                "Проверка: доля кадров, где GPU медленнее CPU, и загрузка GPU по счётчикам Windows.",
            ],
        };
    }

    // 16:9, потому что запасные координаты кнопок посчитаны под эту раскладку.
    public static (Resolution Resolution, string Note) ChooseGpuResolution(string mode, DisplayInfo display)
    {
        if (mode == "native")
        {
            return (display.Current, "родное разрешение монитора");
        }

        if (mode != "auto" && Resolution.TryParse(mode, out var explicitResolution))
        {
            return (explicitResolution, "задано параметром --gpu-res");
        }

        if (display.Current.Is16By9)
        {
            return (display.Current, "родное разрешение монитора");
        }

        var best = display.Modes
            .Where(m => m.Is16By9 && m.Width <= display.Current.Width && m.Height <= display.Current.Height)
            .OrderByDescending(m => m.Width * m.Height)
            .FirstOrDefault();

        return best.Width > 0
            ? (best, $"наибольшее 16:9 для монитора {display.Current}")
            : (display.Current, "родное разрешение монитора");
    }

    private static string Scale(Resolution r, int percent) =>
        $"{Math.Round(r.Width * percent / 100.0)}x{Math.Round(r.Height * percent / 100.0)}";
}
