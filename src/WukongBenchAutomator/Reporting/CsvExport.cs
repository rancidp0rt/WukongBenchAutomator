using System.Globalization;
using System.Text;
using WukongBenchAutomator.Results;
using WukongBenchAutomator.Telemetry;

namespace WukongBenchAutomator.Reporting;

internal static class CsvExport
{
    public static void WriteFrames(string path, IReadOnlyList<FrameRecord> records)
    {
        var sb = new StringBuilder("frame,time_ms,frametime_ms,fps,cpu_frametime_ms,gpu_frametime_ms,cpu_usage,gpu_usage,vram_mb\n");
        var time = 0.0;
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i];
            var frameTime = r.FrameRate > 0 ? 1000 / r.FrameRate : double.NaN;
            if (double.IsFinite(frameTime))
            {
                time += frameTime;
            }

            sb.Append(CultureInfo.InvariantCulture,
                $"{i},{N(time)},{N(frameTime)},{N(r.FrameRate)},{N(r.CpuFrameTime)},{N(r.GpuFrameTime)},{N(r.CpuUsage)},{N(r.GpuUsage)},{N(r.VideoMemoryMb)}\n");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    public static void WriteTelemetry(string path, IReadOnlyList<TelemetrySample> samples)
    {
        var sb = new StringBuilder("time_utc,cpu_total,cpu_busiest_core,cpu_effective_mhz,game_cpu,gpu_util,game_vram_mb,ram_used_gb,gpu_temp_c,gpu_power_w,gpu_clock_mhz\n");
        foreach (var s in samples)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{s.TimeUtc:O},{N(s.CpuTotal)},{N(s.CpuBusiestCore)},{N(s.CpuEffectiveMhz)},{N(s.GameCpu)},{N(s.GpuUtil)},{N(s.GameVramMb)},{N(s.RamUsedGb)},{N(s.GpuTempC)},{N(s.GpuPowerW)},{N(s.GpuClockMhz)}\n");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private static string N(double? value) =>
        value is { } v && double.IsFinite(v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";
}
