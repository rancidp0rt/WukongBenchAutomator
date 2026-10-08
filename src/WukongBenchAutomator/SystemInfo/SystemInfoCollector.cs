using System.Globalization;
using System.Management;
using Microsoft.Win32;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.Interop;

namespace WukongBenchAutomator.SystemInfo;

internal static class SystemInfoCollector
{
    private const uint NvidiaVendorId = 0x10DE;

    public static SystemSnapshot Collect(string? gameDirectory = null)
    {
        var s = new SystemSnapshot();

        Try("ОС", () =>
        {
            var os = Query("SELECT Caption, BuildNumber, OSArchitecture FROM Win32_OperatingSystem").FirstOrDefault();
            if (os is null)
            {
                return;
            }

            s.OsName = Str(os, "Caption")
                ?.Replace("Microsoft ", "", StringComparison.Ordinal)
                .Replace("Майкрософт ", "", StringComparison.Ordinal);
            const string ntKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var displayVersion = Registry.GetValue(ntKey, "DisplayVersion", null) as string;
            var ubr = Registry.GetValue(ntKey, "UBR", null);
            var build = Str(os, "BuildNumber") + (ubr is null ? "" : $".{ubr}");
            s.OsVersion = string.Join(", ", new[] { displayVersion, $"сборка {build}", Str(os, "OSArchitecture") }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
        });

        Try("CPU", () =>
        {
            var cpus = Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor");
            var first = cpus.FirstOrDefault();
            if (first is null)
            {
                return;
            }

            s.CpuName = Str(first, "Name")?.Trim();
            s.CpuCores = cpus.Sum(c => Int(c, "NumberOfCores"));
            s.CpuThreads = cpus.Sum(c => Int(c, "NumberOfLogicalProcessors"));
            s.CpuMaxClockMhz = Int(first, "MaxClockSpeed");
            s.CpuL2CacheKb = cpus.Sum(c => Int(c, "L2CacheSize"));
            s.CpuL3CacheKb = cpus.Sum(c => Int(c, "L3CacheSize"));
        });

        Try("RAM", () =>
        {
            var cs = Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault();
            var modules = Query("SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            foreach (var m in modules)
            {
                var speed = Int(m, "ConfiguredClockSpeed");
                s.MemoryModules.Add(new MemoryModule(
                    Long(m, "Capacity") / 1024.0 / 1024 / 1024,
                    speed > 0 ? speed : Int(m, "Speed"),
                    MemoryTypeName(Int(m, "SMBIOSMemoryType")),
                    Str(m, "Manufacturer")?.Trim(),
                    Str(m, "PartNumber")?.Trim()));
            }

            var installed = s.MemoryModules.Sum(m => m.CapacityGb);
            s.RamTotalGb = installed > 0 ? installed : Long(cs, "TotalPhysicalMemory") / 1024.0 / 1024 / 1024;
        });

        Try("материнская плата", () =>
        {
            var board = Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").FirstOrDefault();
            s.Motherboard = Join(Str(board, "Manufacturer"), Str(board, "Product"));
            var bios = Query("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS").FirstOrDefault();
            s.Bios = Join(Str(bios, "SMBIOSBIOSVersion"), FormatCimDate(Str(bios, "ReleaseDate")) is { } d ? $"от {d}" : null);
        });

        Try("GPU", () => CollectGpus(s));

        Try("монитор", () =>
        {
            var display = DisplayInfo.GetPrimary();
            s.Display = display.RefreshRate > 1 ? $"{display.Current} @ {display.RefreshRate} Гц" : display.Current.ToString();
        });

        Try("план электропитания", () =>
        {
            var plan = Query("SELECT ElementName FROM Win32_PowerPlan WHERE IsActive = TRUE", @"root\cimv2\power").FirstOrDefault();
            s.PowerPlan = Str(plan, "ElementName");
        });

        if (gameDirectory is not null)
        {
            Try("диск с игрой", () => s.GameDrive = DescribeDrive(gameDirectory));
        }

        return s;
    }

    private static void CollectGpus(SystemSnapshot s)
    {
        var wmi = Query("SELECT Name, DriverVersion, DriverDate, AdapterRAM FROM Win32_VideoController");
        IReadOnlyList<GraphicsAdapter> dxgi = [];
        try
        {
            dxgi = GraphicsAdapterProbe.Enumerate();
        }
        catch (Exception ex)
        {
            Log.Debug($"DXGI недоступен: {ex}");
        }

        foreach (var adapter in dxgi.Where(a => !a.IsSoftware))
        {
            var match = wmi.FirstOrDefault(w => string.Equals(Str(w, "Name")?.Trim(), adapter.Name, StringComparison.OrdinalIgnoreCase));
            s.Gpus.Add(new GpuInfo
            {
                Name = adapter.Name,
                VendorId = adapter.VendorId,
                VramBytes = adapter.DedicatedVideoMemory,
                DriverVersion = FormatDriverVersion(Str(match, "DriverVersion"), adapter.VendorId),
                DriverDate = FormatCimDate(Str(match, "DriverDate")),
                DxrSupport = adapter.DxrText,
                SupportsDxr = adapter.SupportsDxr,
            });
        }

        if (s.Gpus.Count > 0)
        {
            return;
        }

        // DXGI не сработал - берём из WMI (там VRAM занижен для карт больше 4 ГБ)
        foreach (var w in wmi)
        {
            var name = Str(w, "Name")?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            s.Gpus.Add(new GpuInfo
            {
                Name = name,
                VramBytes = (ulong)Math.Max(0, Long(w, "AdapterRAM")),
                DriverVersion = Str(w, "DriverVersion"),
                DriverDate = FormatCimDate(Str(w, "DriverDate")),
            });
        }
    }

    private static string? DescribeDrive(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
        {
            return null;
        }

        var letter = char.ToUpperInvariant(root[0]);
        const string storage = @"root\Microsoft\Windows\Storage";
        var partition = Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", storage)
            .FirstOrDefault(p => p["DriveLetter"] is char c && char.ToUpperInvariant(c) == letter);
        if (partition is null)
        {
            return $"{letter}:";
        }

        var diskNumber = Convert.ToString(partition["DiskNumber"], CultureInfo.InvariantCulture);
        var disk = Query("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk", storage)
            .FirstOrDefault(d => Str(d, "DeviceId") == diskNumber);
        if (disk is null)
        {
            return $"{letter}:";
        }

        var media = Int(disk, "MediaType") switch
        {
            3 => "HDD",
            4 => "SSD",
            5 => "SCM",
            _ => null,
        };
        var bus = Int(disk, "BusType") switch
        {
            17 => "NVMe",
            11 => "SATA",
            7 => "USB",
            10 => "SAS",
            8 => "RAID",
            _ => null,
        };
        var kind = Join(bus, media);
        return $"{letter}: - {Str(disk, "FriendlyName")}" + (kind is null ? "" : $" ({kind})");
    }

    // NVIDIA: 32.0.15.6094 -> 560.94
    private static string? FormatDriverVersion(string? version, uint vendorId)
    {
        if (version is null || vendorId != NvidiaVendorId)
        {
            return version;
        }

        var parts = version.Split('.');
        if (parts.Length < 4)
        {
            return version;
        }

        var digits = parts[2] + parts[3].PadLeft(4, '0');
        return digits.Length >= 5
            ? $"{version} ({digits[^5..^2]}.{digits[^2..]})"
            : version;
    }

    private static string? MemoryTypeName(int smbiosType) => smbiosType switch
    {
        24 => "DDR3",
        26 => "DDR4",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => null,
    };

    private static string? FormatCimDate(string? cim)
    {
        if (string.IsNullOrWhiteSpace(cim))
        {
            return null;
        }

        try
        {
            return ManagementDateTimeConverter.ToDateTime(cim).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<ManagementBaseObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        return searcher.Get().Cast<ManagementBaseObject>().ToList();
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Debug($"Не удалось получить сведения ({what}): {ex.Message}");
        }
    }

    private static string? Str(ManagementBaseObject? o, string property)
    {
        var value = o?[property]?.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int Int(ManagementBaseObject? o, string property) =>
        o?[property] is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;

    private static long Long(ManagementBaseObject? o, string property) =>
        o?[property] is { } v ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;

    private static string? Join(params string?[] parts)
    {
        var text = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return text.Length == 0 ? null : text;
    }
}
