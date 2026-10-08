using System.Diagnostics;
using System.Text.RegularExpressions;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.SystemInfo;

namespace WukongBenchAutomator.Telemetry;

internal sealed partial class TelemetrySampler : IDisposable
{
    private const uint NvidiaVendorId = 0x10DE;

    private readonly int _gamePid;
    private readonly double _baseClockMhz;
    private readonly double _totalRamGb;
    private readonly List<TelemetrySample> _samples = [];
    private readonly object _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly NvidiaSmiReader? _nvidia;
    private readonly Thread _thread;
    private int _stopped;

    private TelemetrySampler(int gamePid, SystemSnapshot system)
    {
        _gamePid = gamePid;
        _baseClockMhz = system.CpuMaxClockMhz;
        _totalRamGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024 / 1024;
        _nvidia = system.Gpus.Any(g => g.VendorId == NvidiaVendorId) ? NvidiaSmiReader.TryStart() : null;
        _thread = new Thread(Loop) { IsBackground = true, Name = "telemetry" };
        _thread.Start();
    }

    public static TelemetrySampler? TryStart(int gamePid, SystemSnapshot system)
    {
        try
        {
            return new TelemetrySampler(gamePid, system);
        }
        catch (Exception ex)
        {
            Log.Debug($"Телеметрия недоступна: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<TelemetrySample> Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
        {
            _stop.Cancel();
            _thread.Join(TimeSpan.FromSeconds(5));
            _nvidia?.Dispose();
        }

        lock (_sync)
        {
            return _samples.ToList();
        }
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        try
        {
            using var pdh = PdhQuery.TryCreate();
            if (pdh is null)
            {
                return;
            }

            pdh.TryAdd("cpu", @"\Processor Information(*)\% Processor Time");
            pdh.TryAdd("perf", @"\Processor Information(_Total)\% Processor Performance");
            pdh.TryAdd("gpu", @"\GPU Engine(*)\Utilization Percentage");
            pdh.TryAdd("vram", @"\GPU Process Memory(*)\Dedicated Usage");
            pdh.TryAdd("mem", @"\Memory\Available MBytes");

            using var game = TryGetProcess(_gamePid);
            pdh.Collect();
            var lastCpuTime = CpuTime(game);
            var lastTime = DateTime.UtcNow;

            while (!_stop.Token.WaitHandle.WaitOne(1000))
            {
                if (!pdh.Collect())
                {
                    continue;
                }

                var now = DateTime.UtcNow;
                var cpu = pdh.Read("cpu");
                var perf = pdh.Read("perf", allowAbove100: true);
                var memory = pdh.Read("mem");

                var cpuTime = CpuTime(game);
                double? gameCpu = cpuTime is { } t && lastCpuTime is { } lt
                    ? (t - lt).TotalMilliseconds / ((now - lastTime).TotalMilliseconds * Environment.ProcessorCount) * 100
                    : null;
                lastCpuTime = cpuTime;
                lastTime = now;

                var nvidia = _nvidia?.Latest;
                var sample = new TelemetrySample(
                    now,
                    CpuTotal: cpu.Where(c => c.Instance == "_Total").Select(c => (double?)c.Value).FirstOrDefault(),
                    CpuBusiestCore: cpu.Where(c => !c.Instance.Contains("_Total", StringComparison.Ordinal)).Select(c => (double?)c.Value).Max(),
                    CpuEffectiveMhz: _baseClockMhz > 0 && perf.Count > 0 ? perf[0].Value / 100 * _baseClockMhz : null,
                    GameCpu: gameCpu,
                    GpuUtil: GameGpuUtilization(pdh.Read("gpu"), _gamePid),
                    GameVramMb: GameVideoMemoryMb(pdh.Read("vram"), _gamePid),
                    RamUsedGb: memory.Count > 0 && _totalRamGb > 0 ? _totalRamGb - memory[0].Value / 1024 : null,
                    GpuTempC: nvidia?.TempC,
                    GpuPowerW: nvidia?.PowerW,
                    GpuClockMhz: nvidia?.ClockMhz);

                lock (_sync)
                {
                    _samples.Add(sample);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Ошибка телеметрии: {ex}");
        }
    }

    // Считаем как 'ГП' в диспетчере задач: сумма по движку, максимум по движкам.
    internal static double? GameGpuUtilization(IReadOnlyList<(string Instance, double Value)> engines, int pid)
    {
        var parsed = engines
            .Select(e => (Match: EngineRegex().Match(e.Instance), e.Value))
            .Where(x => x.Match.Success)
            .Select(x => (
                Pid: int.Parse(x.Match.Groups["pid"].Value),
                Engine: x.Match.Groups["luid"].Value + "|" + x.Match.Groups["type"].Value,
                x.Value))
            .ToList();
        if (parsed.Count == 0)
        {
            return null;
        }

        var own = parsed.Where(p => p.Pid == pid).ToList();
        var source = own.Count > 0 ? own : parsed;
        return Math.Min(100, source.GroupBy(p => p.Engine).Max(g => g.Sum(p => p.Value)));
    }

    internal static double? GameVideoMemoryMb(IReadOnlyList<(string Instance, double Value)> memory, int pid)
    {
        var prefix = $"pid_{pid}_";
        var own = memory.Where(m => m.Instance.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        return own.Count == 0 ? null : own.Sum(m => m.Value) / 1024 / 1024;
    }

    private static Process? TryGetProcess(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TimeSpan? CpuTime(Process? process)
    {
        try
        {
            if (process is null)
            {
                return null;
            }

            process.Refresh();
            return process.HasExited ? null : process.TotalProcessorTime;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^pid_(?<pid>\d+)_luid_(?<luid>0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+)_phys_\d+_eng_\d+_engtype_(?<type>.*)$")]
    private static partial Regex EngineRegex();
}
