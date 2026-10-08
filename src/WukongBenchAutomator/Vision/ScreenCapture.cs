using System.Runtime.InteropServices;
using WukongBenchAutomator.Game;

namespace WukongBenchAutomator.Vision;

internal sealed class CapturedImage(int width, int height, byte[] bgra)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Bgra { get; } = bgra;

    // В эксклюзивном полноэкранном режиме BitBlt может вернуть чёрный кадр.
    public bool IsBlank()
    {
        var maxChannel = 0;
        for (var i = 0; i < Bgra.Length; i += 4 * 97)
        {
            maxChannel = Math.Max(maxChannel, Math.Max(Bgra[i], Math.Max(Bgra[i + 1], Bgra[i + 2])));
            if (maxChannel > 24)
            {
                return false;
            }
        }

        return true;
    }

    public CapturedImage Downscale(int factor)
    {
        var width = Width / factor;
        var height = Height / factor;
        var result = new byte[width * height * 4];
        var n = factor * factor;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int b = 0, g = 0, r = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    for (var dx = 0; dx < factor; dx++)
                    {
                        var i = ((y * factor + dy) * Width + x * factor + dx) * 4;
                        b += Bgra[i];
                        g += Bgra[i + 1];
                        r += Bgra[i + 2];
                    }
                }

                var o = (y * width + x) * 4;
                result[o] = (byte)(b / n);
                result[o + 1] = (byte)(g / n);
                result[o + 2] = (byte)(r / n);
                result[o + 3] = 255;
            }
        }

        return new CapturedImage(width, height, result);
    }
}

internal static class ScreenCapture
{
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint Colors; // bmiColors[1]
    }

    public static CapturedImage? CaptureClient(IntPtr hwnd)
    {
        var rect = GameWindow.GetClientScreenRect(hwnd);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
        try
        {
            var previous = SelectObject(memoryDc, bitmap);
            var copied = BitBlt(memoryDc, 0, 0, rect.Width, rect.Height, screenDc, rect.X, rect.Y, SRCCOPY | CAPTUREBLT);
            SelectObject(memoryDc, previous); // GetDIBits требует, чтобы битмап не был выбран в DC
            if (!copied)
            {
                return null;
            }

            var info = new BitmapInfo
            {
                Size = 40,
                Width = rect.Width,
                Height = -rect.Height, // отрицательная высота - строки сверху вниз
                Planes = 1,
                BitCount = 32,
            };
            var pixels = new byte[rect.Width * rect.Height * 4];
            if (GetDIBits(memoryDc, bitmap, 0, (uint)rect.Height, pixels, ref info, 0) == 0)
            {
                return null;
            }

            // BitBlt оставляет альфу нулевой
            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }

            return new CapturedImage(rect.Width, rect.Height, pixels);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int srcX, int srcY, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);
}
