using System.Diagnostics;
using System.Globalization;

namespace WukongBenchAutomator.Telemetry;

internal sealed class NvidiaSmiReader : IDisposable
{
    private const string Query =
        "--query-gpu=temperature.gpu,power.draw,clocks.gr,utilization.gpu --format=csv,noheader,nounits -lms 1000";

    private readonly Process _process;
    private readonly object _sync = new();
    private Reading? _latest;

    internal sealed record Reading(DateTime TimeUtc, double? TempC, double? PowerW, double? ClockMhz, double? Utilization);

    private NvidiaSmiReader(Process process) => _process = process;

    public static NvidiaSmiReader? TryStart()
    {
        var exe = new[]
            {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
            }
            .FirstOrDefault(File.Exists);
        if (exe is null)
        {
            return null;
        }

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(exe, Query)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            var reader = new NvidiaSmiReader(process);
            process.OutputDataReceived += (_, e) => reader.OnLine(e.Data);
            process.Start();
            process.BeginOutputReadLine();
            return reader;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Reading? Latest
    {
        get
        {
            lock (_sync)
            {
                return _latest is { } r && DateTime.UtcNow - r.TimeUtc < TimeSpan.FromSeconds(3) ? r : null;
            }
        }
    }

    internal static Reading? Parse(string? line, DateTime timeUtc)
    {
        var parts = line?.Split(',');
        if (parts is not { Length: >= 4 })
        {
            return null;
        }

        static double? Value(string text) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

        return new Reading(timeUtc, Value(parts[0]), Value(parts[1]), Value(parts[2]), Value(parts[3]));
    }

    private void OnLine(string? line)
    {
        var now = DateTime.UtcNow;
        if (Parse(line, now) is not { } reading)
        {
            return;
        }

        lock (_sync)
        {
            // несколько GPU - берём самую загруженную
            if (_latest is null || now - _latest.TimeUtc > TimeSpan.FromMilliseconds(800)
                                || (reading.Utilization ?? 0) >= (_latest.Utilization ?? 0))
            {
                _latest = reading;
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
        catch (Exception)
        {
            // процесс уже завершился
        }

        _process.Dispose();
    }
}
