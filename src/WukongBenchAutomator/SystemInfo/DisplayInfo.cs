using System.Runtime.InteropServices;
using WukongBenchAutomator.Cli;
using static WukongBenchAutomator.Interop.NativeMethods;

namespace WukongBenchAutomator.SystemInfo;

internal sealed record DisplayInfo(Resolution Current, int RefreshRate, IReadOnlyList<Resolution> Modes)
{
    public static DisplayInfo GetPrimary()
    {
        var current = new Resolution(1920, 1080);
        var refreshRate = 0;
        var mode = NewDevMode();
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref mode) && mode.dmPelsWidth > 0)
        {
            current = new Resolution(mode.dmPelsWidth, mode.dmPelsHeight);
            refreshRate = mode.dmDisplayFrequency;
        }

        var modes = new HashSet<Resolution>();
        for (var i = 0; i < 10_000; i++)
        {
            var m = NewDevMode();
            if (!EnumDisplaySettings(null, i, ref m))
            {
                break;
            }

            modes.Add(new Resolution(m.dmPelsWidth, m.dmPelsHeight));
        }

        return new DisplayInfo(current, refreshRate, modes.OrderBy(r => r.Width * r.Height).ToList());
    }

    public bool Supports(Resolution resolution) => Modes.Count == 0 || Modes.Contains(resolution);

    private static DEVMODE NewDevMode() => new()
    {
        dmDeviceName = string.Empty,
        dmFormName = string.Empty,
        dmSize = (short)Marshal.SizeOf<DEVMODE>(),
    };
}
