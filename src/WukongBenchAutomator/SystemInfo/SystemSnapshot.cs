namespace WukongBenchAutomator.SystemInfo;

internal sealed class SystemSnapshot
{
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }

    public string? CpuName { get; set; }
    public int CpuCores { get; set; }
    public int CpuThreads { get; set; }
    public int CpuMaxClockMhz { get; set; }
    public int CpuL2CacheKb { get; set; }
    public int CpuL3CacheKb { get; set; }

    public List<GpuInfo> Gpus { get; } = [];

    public GpuInfo? PrimaryGpu => Gpus
        .Where(g => !g.IsSoftware)
        .OrderByDescending(g => g.VramBytes)
        .FirstOrDefault();

    public double RamTotalGb { get; set; }
    public List<MemoryModule> MemoryModules { get; } = [];

    public string? Motherboard { get; set; }
    public string? Bios { get; set; }
    public string? Display { get; set; }
    public string? PowerPlan { get; set; }
    public string? GameDrive { get; set; }

    public string? RamSummary
    {
        get
        {
            if (RamTotalGb <= 0)
            {
                return null;
            }

            var text = $"{RamTotalGb:0.#} ГБ";
            if (MemoryModules.Count == 0)
            {
                return text;
            }

            var groups = MemoryModules
                .GroupBy(m => (m.CapacityGb, m.Type, m.SpeedMts))
                .Select(g => $"{g.Count()}×{g.Key.CapacityGb:0.#} ГБ"
                             + (g.Key.Type is null ? "" : $" {g.Key.Type}")
                             + (g.Key.SpeedMts > 0 ? $"-{g.Key.SpeedMts}" : ""));
            var vendor = MemoryModules.Select(m => m.Manufacturer).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
            return $"{text} ({string.Join(" + ", groups)}{(vendor is null ? "" : $", {vendor}")})";
        }
    }

    public string? CpuSummary
    {
        get
        {
            if (CpuName is null)
            {
                return null;
            }

            var parts = new List<string>();
            if (CpuCores > 0)
            {
                parts.Add($"{CpuCores} ядер / {CpuThreads} потоков");
            }

            if (CpuMaxClockMhz > 0)
            {
                parts.Add($"базовая частота {CpuMaxClockMhz / 1000.0:0.0#} ГГц");
            }

            if (CpuL3CacheKb > 0)
            {
                parts.Add($"L3 {CpuL3CacheKb / 1024.0:0.#} МБ");
            }

            return parts.Count == 0 ? CpuName : $"{CpuName} ({string.Join(", ", parts)})";
        }
    }
}

internal sealed class GpuInfo
{
    public required string Name { get; init; }
    public ulong VramBytes { get; set; }
    public string? DriverVersion { get; set; }
    public string? DriverDate { get; set; }
    public string? DxrSupport { get; set; }
    public bool SupportsDxr { get; set; }
    public bool IsSoftware { get; set; }
    public uint VendorId { get; set; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (VramBytes > 0)
            {
                parts.Add($"{VramBytes / 1024.0 / 1024 / 1024:0.#} ГБ VRAM");
            }

            if (DriverVersion is not null)
            {
                parts.Add($"драйвер {DriverVersion}" + (DriverDate is null ? "" : $" от {DriverDate}"));
            }

            if (DxrSupport is not null)
            {
                parts.Add($"трассировка лучей: {DxrSupport}");
            }

            return parts.Count == 0 ? Name : $"{Name} ({string.Join(", ", parts)})";
        }
    }
}

internal sealed record MemoryModule(double CapacityGb, int SpeedMts, string? Type, string? Manufacturer, string? PartNumber);
