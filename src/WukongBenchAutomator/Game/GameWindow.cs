using System.Drawing;
using System.Text;
using WukongBenchAutomator.Interop;
using static WukongBenchAutomator.Interop.NativeMethods;

namespace WukongBenchAutomator.Game;

internal static class GameWindow
{
    private const string UnrealWindowClass = "UnrealWindow";

    public static IntPtr Find(int processId)
    {
        var best = IntPtr.Zero;
        long bestArea = 0;
        var bestIsUnreal = false;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !IsWindowVisible(hwnd) || !GetClientRect(hwnd, out var rect))
            {
                return true;
            }

            var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area < 320 * 200)
            {
                return true;
            }

            var isUnreal = ClassName(hwnd) == UnrealWindowClass;
            if ((isUnreal && !bestIsUnreal) || (isUnreal == bestIsUnreal && area > bestArea))
            {
                best = hwnd;
                bestArea = area;
                bestIsUnreal = isUnreal;
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    public static Rectangle GetClientScreenRect(IntPtr hwnd)
    {
        if (!GetClientRect(hwnd, out var rect))
        {
            return Rectangle.Empty;
        }

        var origin = new POINT();
        ClientToScreen(hwnd, ref origin);
        return new Rectangle(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    // Windows не даёт фоновому процессу забрать фокус, обходим через Alt и AttachThreadInput.
    public static bool BringToForeground(IntPtr hwnd)
    {
        if (!IsWindow(hwnd))
        {
            return false;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
        }

        if (GetForegroundWindow() == hwnd)
        {
            return true;
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread
                       && AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            InputSender.TapAlt();
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        Thread.Sleep(400);
        return GetForegroundWindow() == hwnd;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }
}
