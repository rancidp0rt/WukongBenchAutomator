using System.Runtime.InteropServices;
using WukongBenchAutomator.Infrastructure;

namespace WukongBenchAutomator.Telemetry;

// PdhAddEnglishCounter: английские пути работают и на русской Windows.
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;

    // PDH_FMT_COUNTERVALUE_ITEM_W: szName, затем CStatus и double (выравнивание 8)
    private const int ItemSize = 24;
    private const int StatusOffset = 8;
    private const int ValueOffset = 16;

    private readonly Dictionary<string, IntPtr> _counters = new(StringComparer.Ordinal);
    private IntPtr _query;

    private PdhQuery(IntPtr query) => _query = query;

    public static PdhQuery? TryCreate() =>
        PdhOpenQuery(null, IntPtr.Zero, out var query) == 0 ? new PdhQuery(query) : null;

    public bool TryAdd(string key, string englishPath)
    {
        var status = PdhAddEnglishCounter(_query, englishPath, IntPtr.Zero, out var counter);
        if (status != 0)
        {
            Log.Debug($"Счётчик {englishPath} недоступен (0x{status:X8})");
            return false;
        }

        _counters[key] = counter;
        return true;
    }

    public bool Collect() => PdhCollectQueryData(_query) == 0;

    public IReadOnlyList<(string Instance, double Value)> Read(string key, bool allowAbove100 = false)
    {
        if (!_counters.TryGetValue(key, out var counter))
        {
            return [];
        }

        var format = PDH_FMT_DOUBLE | (allowAbove100 ? PDH_FMT_NOCAP100 : 0);
        uint size = 0;
        if (PdhGetFormattedCounterArray(counter, format, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA || size == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(counter, format, ref size, out var count, buffer) != 0)
            {
                return [];
            }

            var values = new List<(string, double)>((int)count);
            for (var i = 0; i < count; i++)
            {
                var item = buffer + i * ItemSize;
                var status = (uint)Marshal.ReadInt32(item, StatusOffset);
                if (status > 1) // PDH_CSTATUS_VALID_DATA / PDH_CSTATUS_NEW_DATA
                {
                    continue;
                }

                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? string.Empty;
                var value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, ValueOffset));
                values.Add((name, value));
            }

            return values;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
