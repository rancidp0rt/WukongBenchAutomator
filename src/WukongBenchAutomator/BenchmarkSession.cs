using System.Text.RegularExpressions;
using WukongBenchAutomator.Analysis;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Config;
using WukongBenchAutomator.Game;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.Preflight;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Reporting;
using WukongBenchAutomator.Steam;
using WukongBenchAutomator.SystemInfo;
using WukongBenchAutomator.Vision;

namespace WukongBenchAutomator;

internal sealed class BenchmarkSession(Options options)
{
    private const int MinRenderScale = 25;

    private readonly CancellationTokenSource _cts = new();
    private BenchmarkInstall? _install;
    private GameController? _game;
    private ConfigBackup? _backup;
    private int _cleanupDone;

    public int Run()
    {
        var startedAt = DateTime.Now;
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();

        _install = LocateInstall();
        var outputDirectory = CreateOutputDirectory(startedAt);
        Log.AttachFile(Path.Combine(outputDirectory, "run.log"));
        Log.Ok($"Benchmark Tool: {_install.RootDir}");
        Log.Info($"Отчёт будет сохранён в {outputDirectory}");

        _game = new GameController(_install, options.Launch);
        if (_game.KillAll() > 0)
        {
            Log.Warn("Бенчмарк уже был запущен - процесс завершён, чтобы применить настройки.");
        }

        Log.Step("Сбор характеристик ПК...");
        var system = SystemInfoCollector.Collect(_install.RootDir);
        var display = DisplayInfo.GetPrimary();
        Log.Info($"CPU: {system.CpuName ?? "?"}; GPU: {system.PrimaryGpu?.Name ?? "?"}; монитор: {display.Current}");

        Log.Step("Предстартовые проверки...");
        var preflight = PreflightChecks.Run(system, _install, options);
        foreach (var item in preflight)
        {
            if (item.IsWarning)
            {
                Log.Warn(item.Message);
            }
            else
            {
                Log.Info(item.Message);
            }
        }

        var ocr = options.UseOcr ? ScreenTextReader.TryCreate() : null;
        Log.Info(ocr is null
            ? "Кнопки меню будут нажиматься по координатам."
            : $"Кнопки меню ищутся по тексту на экране (OCR Windows: {string.Join(", ", ocr.Languages)}), запасной вариант - координаты.");

        var report = new SessionReport
        {
            StartedAt = startedAt,
            System = system,
            BenchmarkDirectory = _install.RootDir,
            SteamBuildId = _install.BuildId,
            Preflight = preflight,
            OcrLanguages = ocr is null ? null : string.Join(", ", ocr.Languages),
        };

        var (rayTracing, rayTracingReason) = DecideRayTracing(system);
        var profiles = BuildProfiles(display, rayTracing, rayTracingReason);

        try
        {
            PrepareConfig(outputDirectory);
            var runner = new PassRunner(options, _install, _game, system, ocr, _cts.Token);

            if (options.Warmup)
            {
                Log.Step("Прогревочный прогон (результат не учитывается)...");
                runner.Run(profiles[0] with { Title = "Прогрев" }, outputDirectory, 1, "warmup");
            }

            foreach (var profile in profiles)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var pass = runner.Run(profile, outputDirectory, options.Runs);

                // упали с RT, которую включили сами, - пробуем без неё
                if (!pass.Succeeded && pass.GameCrashed && profile.RayTracing && options.RayTracing == RayTracingMode.Auto)
                {
                    Log.Warn($"С трассировкой лучей бенчмарк вылетел - повторяю '{profile.Title}' без трассировки.");
                    var withoutRt = profile.Id == "gpu"
                        ? ProfileFactory.CreateGpu(options, display, false, "с ней тест не запустился на этом ПК")
                        : profile with { RayTracing = false };
                    var fallback = runner.Run(withoutRt, outputDirectory, options.Runs, profile.Id + "_no_rt");
                    fallback.Notes.Add($"Первая попытка с трассировкой лучей не удалась: {pass.Error}. Тест повторён без RT.");
                    pass = fallback;
                }

                if (ShouldAdapt(pass))
                {
                    pass = Adapt(pass, runner, outputDirectory);
                }

                report.Passes.Add(pass);
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warn("Прогон прерван пользователем.");
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            Log.Debug(ex.ToString());
        }
        finally
        {
            Cleanup();
        }

        report.FinishedAt = DateTime.Now;
        if (report.Passes.Count > 0)
        {
            report.Verdict = VerdictBuilder.Build(report);
            var historyPath = Path.Combine(Path.GetDirectoryName(outputDirectory)!, "history.jsonl");
            report.Comparison = BuildComparison(report, historyPath);
            History.Append(historyPath, report);
            ReportWriter.WriteAll(report, outputDirectory);
        }

        Log.DetachFile();
        return report.Passes.Count == profiles.Count && report.Passes.All(p => p.Succeeded) ? 0 : 1;
    }

    private BenchmarkInstall LocateInstall()
    {
        if (options.GameDir is not null)
        {
            return SteamLocator.FromDirectory(options.GameDir);
        }

        Log.Step("Поиск Black Myth: Wukong Benchmark Tool в библиотеках Steam...");
        return SteamLocator.FindBenchmark(out var steamRoot)
               ?? throw new InvalidOperationException(
                   steamRoot is null
                       ? "Steam не найден. Установите Steam и Benchmark Tool или укажите папку параметром --game-dir."
                       : $"Benchmark Tool (Steam App {SteamLocator.BenchmarkAppId}) не найден в библиотеках Steam. "
                         + "Установите его: steam://install/3132990, или укажите папку параметром --game-dir.");
    }

    private string CreateOutputDirectory(DateTime startedAt)
    {
        var root = options.OutputRoot ?? Path.Combine(AppContext.BaseDirectory, "results");
        var dir = Path.Combine(root, startedAt.ToString("yyyy-MM-dd_HH-mm-ss"));
        Directory.CreateDirectory(dir);
        return Path.GetFullPath(dir);
    }

    private List<BenchmarkProfile> BuildProfiles(DisplayInfo display, bool rayTracing, string rayTracingReason)
    {
        BenchmarkProfile BuiltIn(string id) => id == "cpu"
            ? ProfileFactory.CreateCpu(options)
            : ProfileFactory.CreateGpu(options, display, rayTracing, rayTracingReason);

        List<BenchmarkProfile> all;
        if (options.ProfilesFile is { } file)
        {
            var path = Path.GetFullPath(file);
            all = CustomProfileLoader.Load(File.ReadAllText(path), Path.GetFileName(path), BuiltIn, display, rayTracing);
        }
        else
        {
            all = [BuiltIn("cpu"), BuiltIn("gpu")];
        }

        var selected = all.Where(p => options.Includes(p.Id)).ToList();
        if (selected.Count == 0)
        {
            throw new InvalidOperationException($"--only: нет проходов с такими id. Доступны: {string.Join(", ", all.Select(p => p.Id))}");
        }

        foreach (var p in selected)
        {
            if (!display.Supports(p.Resolution))
            {
                Log.Warn($"Монитор не сообщает о режиме {p.Resolution} ('{p.Title}'); игра может выбрать ближайший.");
            }

            Log.Info($"{p.Title}: {p.Resolution}, рендер {p.RenderScalePercent}%, качество '{GameSettingKeys.QualityName(p.Quality)}', "
                     + $"RT {(p.RayTracing ? "вкл" : "выкл")}{(options.Runs > 1 ? $", прогонов: {options.Runs}" : "")}");
        }

        return selected;
    }

    private bool ShouldAdapt(PassReport pass) =>
        options.Adaptive
        && pass.Profile.Kind == ProfileKind.Cpu
        && pass.Validity?.Status == ValidityStatus.Questionable
        && pass.Profile.RenderScalePercent > MinRenderScale;

    private PassReport Adapt(PassReport pass, PassRunner runner, string outputDirectory)
    {
        Log.Warn($"{pass.Profile.Title} упирался в видеокарту ({pass.Validity!.Text}) - повторяю с масштабом рендера {MinRenderScale}%.");
        var lower = pass.Profile.Id == "cpu"
            ? ProfileFactory.CreateCpu(options, MinRenderScale)
            : pass.Profile with { RenderScalePercent = MinRenderScale };
        var retry = runner.Run(lower, outputDirectory, options.Runs, pass.Profile.Id + "_adaptive");

        var before = pass.Result?.Stats?.CpuBoundShare ?? 0;
        var after = retry.Result?.Stats?.CpuBoundShare ?? 0;
        if (retry.Succeeded && after > before)
        {
            retry.Notes.Add($"Автоподстройка: при масштабе {pass.Profile.RenderScalePercent}% процессором были ограничены "
                            + $"{ValidityChecker.Pct(before)} кадров, при {MinRenderScale}% - {ValidityChecker.Pct(after)}. "
                            + $"В отчёте второй прогон, первый сохранён в папке {pass.Profile.Id}.");
            return retry;
        }

        pass.Notes.Add($"Автоподстройка: повтор с масштабом {MinRenderScale}% не сделал тест более 'процессорным' "
                       + $"({ValidityChecker.Pct(after)} кадров), оставлен исходный результат.");
        return pass;
    }

    private ComparisonTable? BuildComparison(SessionReport report, string historyPath)
    {
        if (options.CompareFile is { } file)
        {
            try
            {
                var path = Path.GetFullPath(file);
                return History.Compare(report, History.LoadReport(path), Path.GetFileName(Path.GetDirectoryName(path)) ?? "сравниваемый отчёт");
            }
            catch (Exception ex)
            {
                Log.Warn($"Не удалось прочитать отчёт для сравнения {file}: {ex.Message}");
                return null;
            }
        }

        return History.Compare(report, History.FindPrevious(historyPath, report), "прошлый прогон");
    }

    private void PrepareConfig(string outputDirectory)
    {
        var configPath = _install!.ConfigPath;
        _backup = new ConfigBackup(configPath);
        if (_backup.Exists)
        {
            Log.Warn("Найдена резервная копия настроек от прерванного прогона - восстанавливаю исходный конфиг.");
            _backup.Restore();
        }

        if (!File.Exists(configPath))
        {
            CreateConfigByFirstLaunch(configPath);
        }

        File.Copy(configPath, Path.Combine(outputDirectory, "GameUserSettings.original.ini"), overwrite: true);
        _backup.Create();
        Log.Ok($"Исходные настройки сохранены ({_backup.BackupPath}), после прогона они будут восстановлены.");
    }

    private void CreateConfigByFirstLaunch(string configPath)
    {
        Log.Warn("GameUserSettings.ini ещё нет - запускаю бенчмарк один раз, чтобы он создал настройки по умолчанию...");
        var token = _cts.Token;
        _game!.Launch();
        using var process = GameController.WaitForProcess(TimeSpan.FromMinutes(5), token);
        var hwnd = GameController.WaitForWindow(process, TimeSpan.FromMinutes(5), token);
        GameController.Sleep(TimeSpan.FromSeconds(45), token);
        if (!GameController.CloseGracefully(process, hwnd, TimeSpan.FromSeconds(60)))
        {
            _game.KillAll();
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                "Бенчмарк не создал GameUserSettings.ini. Запустите Benchmark Tool вручную, дождитесь главного меню, "
                + "закройте его и повторите.", configPath);
        }
    }

    private (bool Enabled, string Reason) DecideRayTracing(SystemSnapshot system)
    {
        var gpu = system.PrimaryGpu;
        return options.RayTracing switch
        {
            RayTracingMode.On => (true, "включено параметром --rt on"),
            RayTracingMode.Off => (false, "выключено параметром --rt off"),
            _ when gpu?.SupportsDxr == true => (true, $"{gpu.Name} поддерживает DirectX Raytracing"),
            _ when gpu is not null && gpu.DxrSupport == "нет" => (false, $"{gpu.Name} не поддерживает DirectX Raytracing"),
            _ when gpu is not null && LooksRayTracingCapable(gpu.Name) => (true, $"{gpu.Name} - GPU с аппаратной трассировкой лучей"),
            _ => (false, "поддержку DirectX Raytracing определить не удалось"),
        };
    }

    // Запасной вариант, если D3D12 не ответил.
    internal static bool LooksRayTracingCapable(string gpuName) =>
        gpuName.Contains("RTX", StringComparison.OrdinalIgnoreCase)
        || gpuName.Contains("Arc", StringComparison.Ordinal)
        || Regex.IsMatch(gpuName, @"RX\s*(6[4-9]|7[6-9]|9[0-9])\d{2}", RegexOptions.IgnoreCase);

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        if (!_cts.IsCancellationRequested)
        {
            // первый Ctrl+C: останавливаемся аккуратно и пишем отчёт по тому, что успели
            e.Cancel = true;
            Log.Warn("Ctrl+C: останавливаю прогон и восстанавливаю настройки...");
            _cts.Cancel();
            return;
        }

        Cleanup();
    }

    private void Cleanup()
    {
        if (Interlocked.Exchange(ref _cleanupDone, 1) == 1)
        {
            return;
        }

        try
        {
            _game?.KillAll();
            if (_backup is null)
            {
                return;
            }

            if (options.KeepSettings)
            {
                File.Delete(_backup.BackupPath);
                Log.Info("Параметр --keep-settings: настройки последнего прохода оставлены в GameUserSettings.ini.");
            }
            else if (_backup.Restore())
            {
                Log.Ok("Исходный GameUserSettings.ini восстановлен.");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Ошибка при восстановлении: {ex.Message}");
        }
    }
}
