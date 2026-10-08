using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Steam;
using WukongBenchAutomator.SystemInfo;

namespace WukongBenchAutomator.Preflight;

internal sealed record PreflightItem(bool IsWarning, string Message);

internal static class PreflightChecks
{
    private const double BackgroundCpuWarningPercent = 15;

    public static List<PreflightItem> Run(SystemSnapshot system, BenchmarkInstall install, Options options)
    {
        var items = new List<PreflightItem>();

        if (GetSystemPowerStatus(out var power))
        {
            if (power.ACLineStatus == 0)
            {
                items.Add(new(true, "Ноутбук работает от батареи - CPU и GPU, скорее всего, ограничены по мощности. Подключите зарядку."));
            }
            else if (power.ACLineStatus == 1 && power.BatteryFlag != 128 && power.BatteryFlag != 255)
            {
                items.Add(new(false, "Ноутбук подключён к сети."));
            }
        }

        if (system.PowerPlan is { } plan)
        {
            var lower = plan.ToLowerInvariant();
            if (lower.Contains("эконом") || lower.Contains("saver"))
            {
                items.Add(new(true, $"Включён план электропитания '{plan}' - он снижает частоты. Лучше 'Высокая производительность'."));
            }
            else
            {
                items.Add(new(false, $"План электропитания: '{plan}'."));
            }
        }

        var (cpuLoad, topProcesses) = MeasureBackgroundLoad(TimeSpan.FromSeconds(3));
        if (cpuLoad is { } load)
        {
            if (load > BackgroundCpuWarningPercent)
            {
                var top = topProcesses.Count == 0 ? "" : $" Больше всего: {string.Join(", ", topProcesses)}.";
                items.Add(new(true, $"Фоновая загрузка CPU {load:0}% до старта - результат CPU-теста может быть занижен.{top}"));
            }
            else
            {
                items.Add(new(false, $"Фоновая загрузка CPU до старта: {load:0}%."));
            }
        }

        if (GetMemoryStatus() is { } memory)
        {
            var totalGb = memory.TotalPhys / 1024.0 / 1024 / 1024;
            var freeGb = memory.AvailPhys / 1024.0 / 1024 / 1024;
            if (totalGb < 15)
            {
                items.Add(new(true, $"ОЗУ {totalGb:0.#} ГБ - меньше минимальных для бенчмарка 16 ГБ; возможны подгрузки и фризы."));
            }

            if (freeGb < 4)
            {
                items.Add(new(true, $"Свободно только {freeGb:0.#} ГБ ОЗУ - закройте лишние программы."));
            }
            else
            {
                items.Add(new(false, $"Свободно ОЗУ: {freeGb:0.#} из {totalGb:0.#} ГБ."));
            }
        }

        foreach (var (path, what) in new[] { (install.RootDir, "диске с бенчмарком"), (Path.GetTempPath(), "диске с папкой TEMP (туда пишутся результаты)") })
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
                if (drive.AvailableFreeSpace < 2L * 1024 * 1024 * 1024)
                {
                    items.Add(new(true, $"На {what} ({drive.Name}) свободно меньше 2 ГБ."));
                }
            }
            catch (Exception)
            {
                // сетевые и нестандартные пути не проверяем
            }
        }

        if (options.Launch == LaunchMode.Steam && Process.GetProcessesByName("steam").Length == 0)
        {
            items.Add(new(false, "Steam не запущен - он будет запущен автоматически (нужен вход в аккаунт)."));
        }

        if (system.PrimaryGpu?.DriverDate is { } date
            && DateTime.TryParseExact(date, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var driverDate)
            && DateTime.Now - driverDate > TimeSpan.FromDays(365))
        {
            items.Add(new(true, $"Драйвер видеокарты от {date} - старше года; новые драйверы часто быстрее в UE5-играх."));
        }

        return items;
    }

    private static (double? Load, List<string> Top) MeasureBackgroundLoad(TimeSpan interval)
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
        {
            return (null, []);
        }

        var before = SnapshotProcessTimes();
        Thread.Sleep(interval);
        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
        {
            return (null, []);
        }

        var idle = idle2 - idle1;
        var total = kernel2 - kernel1 + (user2 - user1); // kernel включает idle
        var load = total <= 0 ? 0 : (total - idle) * 100.0 / total;

        var self = Environment.ProcessId;
        var cores = Environment.ProcessorCount;
        var top = SnapshotProcessTimes()
            .Where(p => p.Key.Pid != self && p.Key.Pid != 0 && before.ContainsKey(p.Key))
            .Select(p => (p.Key.Name, Percent: (p.Value - before[p.Key]).TotalMilliseconds / (interval.TotalMilliseconds * cores) * 100))
            .Where(p => p.Percent >= 2)
            .OrderByDescending(p => p.Percent)
            .Take(3)
            .Select(p => $"{p.Name} ({p.Percent:0}%)")
            .ToList();
        return (load, top);
    }

    private static Dictionary<(int Pid, string Name), TimeSpan> SnapshotProcessTimes()
    {
        var result = new Dictionary<(int, string), TimeSpan>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    result[(process.Id, process.ProcessName)] = process.TotalProcessorTime;
                }
                catch (Exception)
                {
                    // системные процессы недоступны без прав администратора
                }
            }
        }

        return result;
    }

    private static MemoryStatusEx? GetMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
