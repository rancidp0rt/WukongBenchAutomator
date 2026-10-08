using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FakeBenchmark;

// Имитация меню бенчмарка для Run-FakeE2E.ps1. В файл результата пишет настройки из GameUserSettings.ini,
// чтобы было видно, применил ли их инструмент.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.Run(new FakeForm());
    }
}

internal sealed class FakeForm : Form
{
    // shifted: кнопки далеко от запасных координат, пройти можно только через OCR
    private static readonly bool Shifted = Environment.GetEnvironmentVariable("FAKE_B1_LAYOUT") == "shifted";

    // слабая видеокарта: при 50% CPU-тест упирается в GPU, при 25% уже в CPU
    private static readonly bool WeakGpu = Environment.GetEnvironmentVariable("FAKE_B1_WEAK_GPU") == "1";

    private static readonly RectangleF BenchmarkButton = Shifted ? new(0.22f, 0.62f, 0.17f, 0.055f) : new(0.05f, 0.43f, 0.13f, 0.05f);
    private static readonly RectangleF ConfirmButton = Shifted ? new(0.55f, 0.70f, 0.14f, 0.055f) : new(0.33f, 0.555f, 0.12f, 0.05f);

    private string _state = "splash";
    private readonly string _configPath;

    public FakeForm()
    {
        Text = "Fake Black Myth: Wukong Benchmark";
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true; // чтобы клики не ушли в другие окна
        BackColor = Color.FromArgb(46, 50, 60);
        DoubleBuffered = true;
        KeyPreview = true;

        // exe лежит в <install>\b1\Binaries\Win64\
        var install = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        _configPath = Path.Combine(install, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _state == "splash")
        {
            _state = "menu";
            Invalidate();
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var fx = e.X / (float)ClientSize.Width;
        var fy = e.Y / (float)ClientSize.Height;
        if (_state == "menu" && BenchmarkButton.Contains(fx, fy))
        {
            _state = "dialog";
        }
        else if (_state == "dialog" && ConfirmButton.Contains(fx, fy))
        {
            _state = "running";
            _ = Task.Run(RunBenchmark);
        }

        Invalidate();
        base.OnMouseClick(e);
    }

    private void RunBenchmark()
    {
        // "загрузка сцены": 16 раз читаем файл 32 МБ, чтобы вырос счётчик чтения
        var blob = Path.Combine(Path.GetTempPath(), "fake-b1-level.bin");
        if (!File.Exists(blob) || new FileInfo(blob).Length != 32L * 1024 * 1024)
        {
            File.WriteAllBytes(blob, new byte[32 * 1024 * 1024]);
        }

        var buffer = new byte[1024 * 1024];
        for (var i = 0; i < 16; i++)
        {
            using var stream = new FileStream(blob, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
            while (stream.Read(buffer, 0, buffer.Length) > 0)
            {
            }

            Thread.Sleep(150);
        }

        Thread.Sleep(6000); // 'прогон'
        WriteResult();
        BeginInvoke(() =>
        {
            _state = "results";
            Invalidate();
        });
    }

    private void WriteResult()
    {
        var ini = File.ReadAllText(_configPath);
        string Ui(string key) => Regex.Match(ini, $"\"{key}\",\\s*\"([^\"]*)\"").Groups[1].Value;
        string Key(string key) => Regex.Match(ini, $@"(?m)^{Regex.Escape(key)}=(.*)$").Groups[1].Value.Trim();

        var rtx = Ui("Rtx") == "1" ? Ui("RtxLevel") : "0";
        var scale = Key("sg.ResolutionQuality");
        var quality = int.Parse(Ui("QualityLevel"), CultureInfo.InvariantCulture);
        var scaleShare = int.Parse(scale, CultureInfo.InvariantCulture) / 100.0;
        var gpuMs = (1 + (2 + quality * 4) * scaleShare) * (WeakGpu ? 2 : 1) + (rtx == "0" ? 0 : 12);
        var cpuMs = 7.5;

        var random = new Random(quality);
        var records = new StringBuilder();
        var count = 0;
        for (var t = 0.0; t < 20_000; count++)
        {
            var cpu = cpuMs * (0.9 + 0.2 * random.NextDouble());
            var gpu = gpuMs * (0.9 + 0.2 * random.NextDouble());
            var ft = Math.Max(cpu, gpu);
            t += ft;
            if (count > 0)
            {
                records.Append(',');
            }

            records.Append(CultureInfo.InvariantCulture,
                $"{{\"FrameRate\":{1000 / ft:0.00},\"CPUUsage\":45,\"GPUUsage\":{Math.Min(99, 100 * gpu / ft):0},\"CPUFrameTime\":{cpu:0.000},\"GPUFrameTime\":{gpu:0.000},\"VideoMemoryUsage\":5000}}");
        }

        var json = string.Create(CultureInfo.InvariantCulture,
            $"{{\"FPSAvg\":{1000 / Math.Max(cpuMs, gpuMs):0.0},\"FPSMax\":{1100 / Math.Max(cpuMs, gpuMs):0.0},\"FPSMin\":{800 / Math.Max(cpuMs, gpuMs):0.0},"
            + $"\"FPS95\":{900 / Math.Max(cpuMs, gpuMs):0.0},\"CPUAvg\":45,\"GPUAvg\":90,\"GameVer\":\"fake\",\"CPUModel\":\"Fake CPU\",\"GPUModel\":\"Fake GPU\","
            + $"\"ScreenResolution\":\"{Key("ResolutionSizeX")} × {Key("ResolutionSizeY")}\",\"QualityLevel\":{quality},\"ImageQuality\":{scale},"
            + $"\"Rtx\":{rtx},\"InsertFrame\":{Ui("InsertFrame")},\"Records\":[{records}]}}");

        var dir = Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory", "Tool");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

        // пишем в два приёма: инструмент не должен схватить недописанный файл
        var bytes = Encoding.UTF8.GetBytes(json);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes, 0, bytes.Length / 2);
        stream.Flush();
        Thread.Sleep(3000);
        stream.Write(bytes, bytes.Length / 2, bytes.Length - bytes.Length / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using var font = new Font("Segoe UI", 20);
        using var small = new Font("Segoe UI", 15);
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        g.DrawString($"FAKE BENCHMARK - state: {_state}", font, Brushes.White, 30, 30);
        g.DrawString("Окно проверки WukongBenchAutomator. Закроется автоматически.", small, Brushes.Gray, 32, 80);

        RectangleF Scale(RectangleF r) => new(r.X * w, r.Y * h, r.Width * w, r.Height * h);
        if (_state is "menu" or "dialog")
        {
            g.FillRectangle(Brushes.DimGray, Scale(BenchmarkButton));
            g.DrawString("Тест быстродействия", small, Brushes.White, Scale(BenchmarkButton));
        }

        if (_state == "dialog")
        {
            g.FillRectangle(Brushes.SteelBlue, Scale(ConfirmButton));
            g.DrawString("Подтвердить", small, Brushes.White, Scale(ConfirmButton));
        }
    }
}
