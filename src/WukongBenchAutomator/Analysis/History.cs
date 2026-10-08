using System.Globalization;
using System.Text;
using System.Text.Json;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.Reporting;

namespace WukongBenchAutomator.Analysis;

internal sealed record HistoryPass(string Id, string Title, string Resolution, double? FpsAvg, double? Low1Fps, double? Fps95);

internal sealed record HistoryEntry(DateTime Date, string? Cpu, string? Gpu, string? GameVersion, List<HistoryPass> Passes);

internal static class History
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static HistoryEntry ToEntry(SessionReport report) => new(
        report.StartedAt,
        report.System?.CpuName ?? report.AnyResult?.CpuModel,
        report.System?.PrimaryGpu?.Name ?? report.AnyResult?.GpuModel,
        report.AnyResult?.GameVersion,
        report.Passes.Where(p => p.Succeeded).Select(p => new HistoryPass(
            p.Profile.Id,
            p.Profile.Title,
            p.Profile.Resolution.ToString(),
            p.Result!.FpsAvg ?? p.Result.Stats?.AvgFps,
            p.Result.Stats?.Low1Fps,
            p.Result.Fps95)).ToList());

    public static void Append(string path, SessionReport report)
    {
        try
        {
            var entry = ToEntry(report);
            if (entry.Passes.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Debug($"Не удалось записать историю: {ex.Message}");
        }
    }

    public static HistoryEntry? FindPrevious(string path, SessionReport current)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var me = ToEntry(current);
        HistoryEntry? last = null;
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                var entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonOptions);
                if (entry is not null && entry.Cpu == me.Cpu && entry.Gpu == me.Gpu && entry.Date < current.StartedAt)
                {
                    last = entry;
                }
            }
            catch (JsonException)
            {
                // битая строка
            }
        }

        return last;
    }

    public static HistoryEntry LoadReport(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var date = root.TryGetProperty("StartedAt", out var d) && d.TryGetDateTime(out var dt) ? dt : File.GetLastWriteTime(path);
        var passes = new List<HistoryPass>();
        if (root.TryGetProperty("Passes", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var pass in array.EnumerateArray())
            {
                if (!pass.TryGetProperty("Profile", out var profile) || !pass.TryGetProperty("Result", out var result)
                    || result.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var resolution = profile.TryGetProperty("Resolution", out var r) && r.ValueKind == JsonValueKind.Object
                    ? $"{Number(r, "Width"):0}x{Number(r, "Height"):0}"
                    : "";
                var stats = result.TryGetProperty("Stats", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
                passes.Add(new HistoryPass(
                    Text(profile, "Id") ?? "?",
                    Text(profile, "Title") ?? "?",
                    resolution,
                    Number(result, "FpsAvg") ?? (stats.ValueKind == JsonValueKind.Object ? Number(stats, "AvgFps") : null),
                    stats.ValueKind == JsonValueKind.Object ? Number(stats, "Low1Fps") : null,
                    Number(result, "Fps95")));
            }
        }

        var system = root.TryGetProperty("System", out var sys) && sys.ValueKind == JsonValueKind.Object ? sys : default;
        return new HistoryEntry(date, system.ValueKind == JsonValueKind.Object ? Text(system, "CpuName") : null, null, null, passes);
    }

    public static ComparisonTable? Compare(SessionReport current, HistoryEntry? baseline, string label)
    {
        if (baseline is null)
        {
            return null;
        }

        var now = ToEntry(current);
        var rows = new List<ComparisonRow>();
        foreach (var pass in now.Passes)
        {
            var old = baseline.Passes.FirstOrDefault(p => p.Id == pass.Id);
            if (old is null)
            {
                continue;
            }

            var suffix = old.Resolution.Length > 0 && old.Resolution != pass.Resolution ? $" ({old.Resolution} -> {pass.Resolution})" : "";
            rows.Add(Row($"{pass.Title}: средний FPS{suffix}", pass.FpsAvg, old.FpsAvg));
            rows.Add(Row($"{pass.Title}: 1% low", pass.Low1Fps, old.Low1Fps));
        }

        return rows.Count == 0 ? null : new ComparisonTable($"{label} ({baseline.Date:yyyy-MM-dd HH:mm})", rows);
    }

    internal static ComparisonRow Row(string label, double? now, double? then) => new(label,
    [
        ReportTables.Format(now, "0.0"),
        ReportTables.Format(then, "0.0"),
        now is { } a && then is { } b && b > 0 ? (a / b - 1).ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture) : ReportTables.Missing,
    ]);

    private static double? Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
