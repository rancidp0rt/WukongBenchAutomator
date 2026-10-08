using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WukongBenchAutomator.Results;

internal static class ResultParser
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static BenchmarkResult Parse(string path) => Parse(File.ReadAllBytes(path), path);

    public static BenchmarkResult Parse(byte[] bytes, string sourcePath)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd().Trim('\0', ' ', '\r', '\n', '\t', '﻿');

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{Path.GetFileName(sourcePath)}: не JSON ({ex.Message})", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"{Path.GetFileName(sourcePath)}: ожидался JSON-объект");
            }

            var fields = root.EnumerateObject()
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);

            var records = fields.TryGetValue("Records", out var recordsElement) && recordsElement.ValueKind == JsonValueKind.Array
                ? recordsElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(ParseRecord).ToList()
                : [];

            double? Num(string name) => fields.TryGetValue(name, out var e) ? ToDouble(e) : null;
            int? Int(string name) => Num(name) is { } d ? (int)Math.Round(d) : null;
            string? Str(string name) => fields.TryGetValue(name, out var e) ? ToText(e) : null;

            if (Num("FPSAvg") is null && records.Count == 0)
            {
                throw new FormatException($"{Path.GetFileName(sourcePath)}: нет полей FPSAvg/Records - это не результат бенчмарка");
            }

            var raw = fields
                .Where(f => f.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
                .ToDictionary(f => f.Key, f => ToText(f.Value) ?? "", StringComparer.OrdinalIgnoreCase);

            return new BenchmarkResult
            {
                SourcePath = sourcePath,
                FpsAvg = Num("FPSAvg"),
                FpsMin = Num("FPSMin"),
                FpsMax = Num("FPSMax"),
                Fps95 = Num("FPS95"),
                CpuUsageAvg = Num("CPUAvg"),
                GpuUsageAvg = Num("GPUAvg"),
                VideoMemGb = Num("VideoMem"),
                GameVersion = Str("GameVer"),
                OsVersion = Str("SysVer"),
                CpuModel = Str("CPUModel"),
                GpuModel = Str("GPUModel"),
                GpuDriver = Str("GpuDriverVer"),
                VideoMemSize = Str("VideoMemSize"),
                SystemMemory = Str("SysMem"),
                ScreenResolution = Str("ScreenResolution"),
                ScreenMode = Int("ScreenMode"),
                QualityLevel = Int("QualityLevel"),
                ImageQuality = Int("ImageQuality"),
                ViewDistance = Int("ViewDistance"),
                Rtx = Int("Rtx"),
                Dlss = Int("Dlss"),
                InsertFrame = Int("InsertFrame"),
                Dx12 = Int("Dx12"),
                Records = records,
                Stats = FrameStats.Compute(records),
                RawFields = raw,
            };
        }
    }

    private static FrameRecord ParseRecord(JsonElement e)
    {
        double Get(string name)
        {
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return ToDouble(p.Value) ?? double.NaN;
                }
            }

            return double.NaN;
        }

        return new FrameRecord(
            Get("FrameRate"),
            Get("CPUFrameTime"),
            Get("GPUFrameTime"),
            Get("CPUUsage"),
            Get("GPUUsage"),
            Get("VideoMemoryUsage"));
    }

    private static double? ToDouble(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String when double.TryParse(
            e.GetString()?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        _ => null,
    };

    private static string? ToText(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    };
}
