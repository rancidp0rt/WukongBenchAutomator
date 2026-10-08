using System.Drawing;
using System.Globalization;

namespace WukongBenchAutomator.Cli;

internal enum Command
{
    Run,
    Report,
    SysInfo,
    Ocr,
}

internal enum RayTracingMode
{
    Auto,
    On,
    Off,
}

internal enum LaunchMode
{
    Steam,
    Direct,
}

internal sealed class Options
{
    public Command Command { get; private set; } = Command.Run;
    public bool ShowHelp { get; private set; }

    public string? GameDir { get; private set; }

    public IReadOnlySet<string>? OnlyIds { get; private set; }

    public bool RunCpu => Includes("cpu");
    public bool RunGpu => Includes("gpu");
    public RayTracingMode RayTracing { get; private set; } = RayTracingMode.Auto;

    public int Runs { get; private set; } = 1;

    public bool Warmup { get; private set; }

    public bool Adaptive { get; private set; } = true;

    public bool UseOcr { get; private set; } = true;

    public string? ProfilesFile { get; private set; }
    public string? CompareFile { get; private set; }

    public Resolution CpuResolution { get; private set; } = new(1280, 720);
    public int CpuRenderScale { get; private set; } = 50;

    public string GpuResolution { get; private set; } = "auto";

    public TimeSpan PassTimeout { get; private set; } = TimeSpan.FromMinutes(20);
    public TimeSpan MenuDelay { get; private set; } = TimeSpan.FromSeconds(20);
    public LaunchMode Launch { get; private set; } = LaunchMode.Steam;
    public string? OutputRoot { get; private set; }
    public bool KeepSettings { get; private set; }

    public PointF BenchmarkButton { get; private set; } = new(0.113f, 0.457f);
    public PointF ConfirmButton { get; private set; } = new(0.393f, 0.580f);

    public string? ReportCpuFile { get; private set; }
    public string? ReportGpuFile { get; private set; }
    public string? OcrImage { get; private set; }

    public static string Usage => """
        Wukong Bench Automator - автоматический CPU- и GPU-прогон Black Myth: Wukong Benchmark Tool.

        Использование:
          WukongBenchAutomator.exe [параметры]                 полный прогон: CPU-тест, затем GPU-тест
          WukongBenchAutomator.exe --sysinfo                   только характеристики ПК
          WukongBenchAutomator.exe --ocr <снимок.png>          что OCR видит на снимке меню (диагностика)
          WukongBenchAutomator.exe --report <cpu.json> [<gpu.json>]
                                                               отчёт по готовым файлам результатов бенчмарка

        Параметры прогона:
          --game-dir <путь>        папка Benchmark Tool (по умолчанию ищется через Steam)
          --only <id[,id]>         выполнить только указанные проходы: cpu, gpu или id своих профилей
          --runs <1-10>            сколько раз запускать каждый проход (среднее + разброс), по умолчанию 1
          --warmup                 прогревочный прогон перед замерами (не учитывается)
          --profiles <file.json>   свой набор проходов (см. README), например серия разрешений
          --compare <report.json>  сравнить с другим отчётом (по умолчанию - с прошлым прогоном на этом ПК)
          --no-adaptive            не повторять CPU-тест с масштабом 25%, если он упёрся в видеокарту
          --no-ocr                 не искать кнопки по тексту на экране, только по координатам
          --rt auto|on|off         трассировка лучей в GPU-тесте (auto: если GPU поддерживает DXR)
          --cpu-res <WxH>          разрешение CPU-теста (по умолчанию 1280x720)
          --cpu-scale <25-100>     масштаб рендера CPU-теста, % (по умолчанию 50)
          --gpu-res auto|native|<WxH>
                                   разрешение GPU-теста (auto: наибольшее 16:9, которое поддерживает монитор)
          --timeout <мин>          таймаут одного прохода (по умолчанию 20)
          --menu-delay <сек>       пауза между появлением окна и нажатиями в меню (по умолчанию 20)
          --launch steam|direct    запуск через Steam (по умолчанию) или напрямую через exe
          --output <папка>         куда складывать отчёты (по умолчанию .\results рядом с exe)
          --keep-settings          не возвращать исходный GameUserSettings.ini после прогона
          --click-benchmark <x,y>  координаты кнопки 'Тест быстродействия' в долях окна (0.113,0.457)
          --click-confirm <x,y>    координаты кнопки 'Подтвердить' в долях окна (0.393,0.580)
          -h, --help               эта справка

        Во время прогона не трогайте мышь и клавиатуру: инструмент сам проходит меню бенчмарка.
        """;

    public static Options Parse(IReadOnlyList<string> args)
    {
        var o = new Options();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                case "/?":
                    o.ShowHelp = true;
                    break;
                case "--sysinfo":
                    o.Command = Command.SysInfo;
                    break;
                case "--ocr":
                    o.Command = Command.Ocr;
                    o.OcrImage = Next(args, ref i, arg);
                    break;
                case "--report":
                    o.Command = Command.Report;
                    o.ReportCpuFile = Next(args, ref i, arg);
                    if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        o.ReportGpuFile = args[++i];
                    }
                    break;
                case "--game-dir":
                    o.GameDir = Next(args, ref i, arg);
                    break;
                case "--only":
                    var ids = Next(args, ref i, arg)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (ids.Count == 0 || ids.Any(id => !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
                    {
                        throw new ArgumentException("--only: ожидаются id проходов через запятую, например cpu или gpu");
                    }

                    o.OnlyIds = ids;
                    break;
                case "--runs":
                    o.Runs = ParseInt(Next(args, ref i, arg), arg, 1, 10);
                    break;
                case "--warmup":
                    o.Warmup = true;
                    break;
                case "--profiles":
                    o.ProfilesFile = Next(args, ref i, arg);
                    break;
                case "--compare":
                    o.CompareFile = Next(args, ref i, arg);
                    break;
                case "--no-adaptive":
                    o.Adaptive = false;
                    break;
                case "--no-ocr":
                    o.UseOcr = false;
                    break;
                case "--rt":
                    o.RayTracing = ParseEnum<RayTracingMode>(Next(args, ref i, arg), arg);
                    break;
                case "--cpu-res":
                    o.CpuResolution = ParseResolution(Next(args, ref i, arg), arg);
                    break;
                case "--cpu-scale":
                    o.CpuRenderScale = ParseInt(Next(args, ref i, arg), arg, 25, 100);
                    break;
                case "--gpu-res":
                    var gpuRes = Next(args, ref i, arg).ToLowerInvariant();
                    if (gpuRes is not ("auto" or "native"))
                    {
                        ParseResolution(gpuRes, arg);
                    }
                    o.GpuResolution = gpuRes;
                    break;
                case "--timeout":
                    o.PassTimeout = TimeSpan.FromMinutes(ParseInt(Next(args, ref i, arg), arg, 3, 180));
                    break;
                case "--menu-delay":
                    o.MenuDelay = TimeSpan.FromSeconds(ParseInt(Next(args, ref i, arg), arg, 0, 600));
                    break;
                case "--launch":
                    o.Launch = ParseEnum<LaunchMode>(Next(args, ref i, arg), arg);
                    break;
                case "--output":
                    o.OutputRoot = Next(args, ref i, arg);
                    break;
                case "--keep-settings":
                    o.KeepSettings = true;
                    break;
                case "--click-benchmark":
                    o.BenchmarkButton = ParsePoint(Next(args, ref i, arg), arg);
                    break;
                case "--click-confirm":
                    o.ConfirmButton = ParsePoint(Next(args, ref i, arg), arg);
                    break;
                default:
                    throw new ArgumentException($"Неизвестный параметр: {arg}");
            }
        }

        return o;
    }

    public bool Includes(string profileId) => OnlyIds is null || OnlyIds.Contains(profileId);

    private static string Next(IReadOnlyList<string> args, ref int i, string name)
    {
        if (i + 1 >= args.Count)
        {
            throw new ArgumentException($"{name}: не указано значение");
        }

        return args[++i];
    }

    private static T ParseEnum<T>(string value, string name) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(result)
            ? result
            : throw new ArgumentException(
                $"{name}: недопустимое значение '{value}', ожидается {string.Join("|", Enum.GetNames<T>()).ToLowerInvariant()}");

    private static int ParseInt(string value, string name, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
            ? n
            : throw new ArgumentException($"{name}: ожидается целое число от {min} до {max}, получено '{value}'");

    private static Resolution ParseResolution(string value, string name) =>
        Resolution.TryParse(value, out var r) && r.Width >= 640 && r.Height >= 360
            ? r
            : throw new ArgumentException($"{name}: ожидается разрешение вида 1920x1080, получено '{value}'");

    private static PointF ParsePoint(string value, string name)
    {
        var parts = value.Split(',', ';');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && x is > 0 and < 1 && y is > 0 and < 1)
        {
            return new PointF(x, y);
        }

        throw new ArgumentException($"{name}: ожидаются доли окна вида 0.11,0.46, получено '{value}'");
    }
}
