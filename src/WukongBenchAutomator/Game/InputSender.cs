using System.Runtime.InteropServices;
using static WukongBenchAutomator.Interop.NativeMethods;

namespace WukongBenchAutomator.Game;

internal static class InputSender
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    public static void PressKey(ushort virtualKey)
    {
        var scan = (ushort)MapVirtualKey(virtualKey, 0);
        Send(
            Keyboard(virtualKey, scan, 0),
            Keyboard(virtualKey, scan, KEYEVENTF_KEYUP));
    }

    public static void Click(int screenX, int screenY)
    {
        SetCursorPos(screenX, screenY);
        Thread.Sleep(60);

        // SendInput ждёт координаты 0..65535 по всему виртуальному столу
        var left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN) - 1);
        var height = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN) - 1);
        var dx = (int)Math.Round((screenX - left) * 65535.0 / width);
        var dy = (int)Math.Round((screenY - top) * 65535.0 / height);
        const uint absolute = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        Send(Mouse(dx, dy, MOUSEEVENTF_MOVE | absolute));
        Thread.Sleep(120);
        Send(Mouse(dx, dy, MOUSEEVENTF_LEFTDOWN | absolute));
        Thread.Sleep(80);
        Send(Mouse(dx, dy, MOUSEEVENTF_LEFTUP | absolute));
    }

    // Без нажатия Alt Windows не даст фоновому процессу вызвать SetForegroundWindow.
    public static void TapAlt() => Send(Keyboard(VK_MENU, 0, 0), Keyboard(VK_MENU, 0, KEYEVENTF_KEYUP));

    private static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, InputSize);

    private static INPUT Keyboard(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private static INPUT Mouse(int dx, int dy, uint flags) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } },
    };
}
