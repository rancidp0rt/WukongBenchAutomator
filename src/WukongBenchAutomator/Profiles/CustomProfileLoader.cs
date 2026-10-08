using System.Text.Json;
using System.Text.RegularExpressions;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.SystemInfo;

namespace WukongBenchAutomator.Profiles;

// Формат описан в README, раздел про --profiles.
internal static partial class CustomProfileLoader
{
    public static List<BenchmarkProfile> Load(
        string json, string sourceName, Func<string, BenchmarkProfile> builtIn, DisplayInfo display, bool rayTracingAuto)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{sourceName}: некорректный JSON - {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException($"{sourceName}: ожидается JSON-массив профилей");
            }

            var profiles = new List<BenchmarkProfile>();
            var index = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                index++;
                var profile = element.ValueKind switch
                {
                    JsonValueKind.String => BuiltIn(element.GetString()!, builtIn, sourceName, index),
                    JsonValueKind.Object => Custom(element, display, rayTracingAuto, sourceName, index),
                    _ => throw new FormatException($"{sourceName}, профиль №{index}: ожидается строка или объект"),
                };

                if (profiles.Any(p => p.Id == profile.Id))
                {
                    throw new FormatException($"{sourceName}: id '{profile.Id}' встречается дважды");
                }

                profiles.Add(profile);
            }

            return profiles.Count > 0 ? profiles : throw new FormatException($"{sourceName}: список профилей пуст");
        }
    }

    private static BenchmarkProfile BuiltIn(string id, Func<string, BenchmarkProfile> builtIn, string source, int index) =>
        id.ToLowerInvariant() is "cpu" or "gpu"
            ? builtIn(id.ToLowerInvariant())
            : throw new FormatException($"{source}, профиль №{index}: встроенные профили - \"cpu\" и \"gpu\", получено \"{id}\"");

    private static BenchmarkProfile Custom(JsonElement e, DisplayInfo display, bool rayTracingAuto, string source, int index)
    {
        string Where() => $"{source}, профиль №{index}";

        var id = Str(e, "id") ?? throw new FormatException($"{Where()}: нет поля id");
        if (!IdRegex().IsMatch(id))
        {
            throw new FormatException($"{Where()}: id '{id}' - только латиница, цифры, '-' и '_'");
        }

        var kind = (Str(e, "kind") ?? "custom").ToLowerInvariant() switch
        {
            "cpu" => ProfileKind.Cpu,
            "gpu" => ProfileKind.Gpu,
            "custom" => ProfileKind.Custom,
            var other => throw new FormatException($"{Where()}: kind '{other}' - ожидается cpu, gpu или custom"),
        };

        var resolutionText = Str(e, "resolution") ?? "auto";
        if (resolutionText is not ("auto" or "native") && !Resolution.TryParse(resolutionText, out _))
        {
            throw new FormatException($"{Where()}: resolution '{resolutionText}' - ожидается auto, native или WxH");
        }

        var (resolution, note) = ProfileFactory.ChooseGpuResolution(resolutionText, display);
        var rayTracing = e.TryGetProperty("rayTracing", out var rt)
            ? rt.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when rt.GetString() == "auto" => rayTracingAuto,
                _ => throw new FormatException($"{Where()}: rayTracing - true, false или \"auto\""),
            }
            : false;

        return new BenchmarkProfile
        {
            Id = id,
            Title = Str(e, "title") ?? id,
            Kind = kind,
            Resolution = resolution,
            ResolutionNote = resolutionText == "auto" ? note : null,
            RenderScalePercent = Int(e, "renderScale", 100, 25, 100, Where),
            Quality = Int(e, "quality", 5, 1, GameSettingKeys.MaxQuality, Where),
            RayTracing = rayTracing,
            RayTracingLevel = Int(e, "rayTracingLevel", GameSettingKeys.MaxRayTracingLevel, 1, GameSettingKeys.MaxRayTracingLevel, Where),
            Rationale = [$"Пользовательский профиль из {source}."],
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name, int fallback, int min, int max, Func<string> where)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return fallback;
        }

        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= min && n <= max
            ? n
            : throw new FormatException($"{where()}: {name} - целое от {min} до {max}");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$")]
    private static partial Regex IdRegex();
}
