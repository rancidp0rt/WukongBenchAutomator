using System.Diagnostics;
using WukongBenchAutomator.Analysis;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Config;
using WukongBenchAutomator.Game;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.Profiles;
using WukongBenchAutomator.Reporting;
using WukongBenchAutomator.Results;
using WukongBenchAutomator.Steam;
using WukongBenchAutomator.SystemInfo;
using WukongBenchAutomator.Telemetry;
using WukongBenchAutomator.Vision;

namespace WukongBenchAutomator;

internal sealed class PassRunner(
    Options options,
    BenchmarkInstall install,
    GameController game,
    SystemSnapshot system,
    ScreenTextReader? ocr,
    CancellationToken token)
{
    private const int MaxStartAttempts = 4;
    private static readonly TimeSpan StartRetryInterval = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan StartConfirmationTimeout = TimeSpan.FromMinutes(8);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(30);

    // Старт теста ловим по чтению с диска: после 'Подтвердить' игра грузит сцену, это сотни МБ.
    private const ulong LevelLoadReadThresholdBytes = 300UL * 1024 * 1024;
    private const double MaxMenuReadRateBytesPerSecond = 20.0 * 1024 * 1024;

    public PassReport Run(BenchmarkProfile profile, string outputDirectory, int runs, string? directoryName = null)
    {
        var pass = new PassReport { Profile = profile };
        var passDirectory = Directory.CreateDirectory(Path.Combine(outputDirectory, directoryName ?? profile.Id)).FullName;
        var stopwatch = Stopwatch.StartNew();
        Log.Step($"===== {profile.Title} =====");

        try
        {
            var ini = IniFile.Load(install.ConfigPath);
            var patch = GameSettingsPatcher.Apply(ini, profile);
            ini.Save(install.ConfigPath);
            File.WriteAllBytes(Path.Combine(passDirectory, "GameUserSettings.applied.ini"), ini.ToBytes());
            pass.Changes = patch.Changes;
            pass.PreservedUiValues = patch.PreservedUiValues;
            Log.Ok($"Настройки применены: изменено ключей - {patch.Changes.Count}.");

            for (var run = 1; run <= runs; run++)
            {
                token.ThrowIfCancellationRequested();
                if (runs > 1)
                {
                    Log.Step($"{profile.Title}: прогон {run} из {runs}");
                }

                pass.Runs.Add(RunOnce(profile, passDirectory, run, runs));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            pass.Error = ex.Message;
            pass.GameCrashed = ex is GameExitedException;
            Log.Error($"{profile.Title}: {ex.Message}");
            Log.Debug(ex.ToString());
        }
        finally
        {
            game.KillAll();
            pass.Duration = stopwatch.Elapsed;
        }

        Summarize(pass, runs);
        return pass;
    }

    private static void Summarize(PassReport pass, int requestedRuns)
    {
        if (pass.Runs.Count == 0)
        {
            return;
        }

        if (pass.Error is not null)
        {
            // часть прогонов прошла: засчитываем, ошибку пишем в замечания
            pass.Notes.Add($"Выполнено прогонов: {pass.Runs.Count} из {requestedRuns}; последний завершился ошибкой: {pass.Error}");
            pass.Error = null;
        }

        var results = pass.Runs.Select(r => r.Result).ToList();
        pass.Result = RunAggregator.Aggregate(results);
        pass.RunStatistics = results.Count > 1 ? RunStatistics.Compute(results) : null;
        pass.Telemetry = TelemetrySummary.Combine(pass.Runs.Select(r => r.Telemetry).OfType<TelemetrySummary>().ToList());
        pass.TelemetrySamples = pass.Runs[0].Samples;
        pass.Verification = SettingsVerifier.Compare(pass.Profile, pass.Runs[0].Result);
        pass.Validity = ValidityChecker.Assess(pass);

        var methods = pass.Runs.Select(r => r.NavigationMethod).OfType<string>().Distinct().ToList();
        if (methods.Count > 0)
        {
            pass.Notes.Add($"Меню бенчмарка пройдено {string.Join("; ", methods)}.");
        }
    }

    private RunResult RunOnce(BenchmarkProfile profile, string passDirectory, int run, int runs)
    {
        var suffix = runs > 1 ? $"_run{run}" : "";
        var watcher = new ResultWatcher();
        watcher.TakeSnapshot();
        Log.Debug($"Папки результатов: {string.Join("; ", watcher.Directories)}");
        var navigator = new MenuNavigator(options.BenchmarkButton, options.ConfirmButton, ocr)
        {
            DiagnosticsDirectory = Path.Combine(passDirectory, "screens"),
        };

        var stopwatch = Stopwatch.StartNew();
        TelemetrySampler? sampler = null;
        game.Launch();
        try
        {
            Log.Info($"Жду запуска процесса {GameController.ShippingProcessName}...");
            using (var process = GameController.WaitForProcess(TimeSpan.FromMinutes(5), token))
            {
                GameController.WaitForWindow(process, TimeSpan.FromMinutes(5), token);
                sampler = TelemetrySampler.TryStart(process.Id, system);
            }

            var result = WaitForResult(watcher, navigator);
            var screenshot = CaptureResultsScreen(passDirectory, suffix);
            var samples = sampler?.Stop() ?? [];
            var window = TestWindow(samples, File.GetLastWriteTimeUtc(result.SourcePath), result.Stats?.DurationSeconds ?? 140);

            File.Copy(result.SourcePath, Path.Combine(passDirectory, $"benchmark_raw{suffix}.json"), overwrite: true);
            if (result.Records.Count > 0)
            {
                CsvExport.WriteFrames(Path.Combine(passDirectory, $"frames{suffix}.csv"), result.Records);
            }

            if (window.Count > 0)
            {
                CsvExport.WriteTelemetry(Path.Combine(passDirectory, $"telemetry{suffix}.csv"), window);
            }

            var telemetry = TelemetrySummary.From(window);
            Log.Ok($"{profile.Title}{(runs > 1 ? $" (прогон {run})" : "")}: средний FPS {ReportTables.Format(result.FpsAvg, "0.0")}, "
                   + $"1% low {ReportTables.Format(result.Stats?.Low1Fps, "0.0")}"
                   + (telemetry?.GpuUtilAvg is { } gpu ? $", загрузка GPU {gpu:0}%" : "")
                   + $" (файл {Path.GetFileName(result.SourcePath)}).");

            return new RunResult
            {
                Index = run,
                Result = result,
                Telemetry = telemetry,
                Samples = window,
                Screenshot = screenshot,
                NavigationMethod = navigator.LastMethod,
                Duration = stopwatch.Elapsed,
            };
        }
        finally
        {
            sampler?.Dispose();
            game.KillAll();
        }
    }

    // Оставляем только замеры за сам тест, без меню и загрузки.
    internal static List<TelemetrySample> TestWindow(IReadOnlyList<TelemetrySample> samples, DateTime resultWrittenUtc, double durationSeconds)
    {
        var start = resultWrittenUtc - TimeSpan.FromSeconds(durationSeconds);
        var window = samples.Where(s => s.TimeUtc >= start && s.TimeUtc <= resultWrittenUtc.AddSeconds(1)).ToList();
        return window.Count >= 3 ? window : samples.ToList();
    }

    // Если старт не поймали, проходим меню ещё раз. Лишние клики во время теста ни на что не влияют.
    private BenchmarkResult WaitForResult(ResultWatcher watcher, MenuNavigator navigator)
    {
        var deadline = DateTime.UtcNow + options.PassTimeout;

        Log.Info($"Окно бенчмарка открыто. Жду {options.MenuDelay.TotalSeconds:0} с, пока загрузится главное меню. Не трогайте мышь и клавиатуру.");
        var menuReadRate = MeasureMenuReadRate();

        var attempts = 0;
        var lastAttemptAt = DateTime.MinValue;
        ulong readAtAttempt = 0;
        int? attemptPid = null;
        DateTime? startedAt = null;
        DateTime? goneSince = null;
        var lastProgress = DateTime.UtcNow;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (watcher.TryGetNewResult() is { } result)
            {
                return result;
            }

            var now = DateTime.UtcNow;
            if (now > deadline)
            {
                throw new TimeoutException(
                    $"Нет результата за {options.PassTimeout.TotalMinutes:0} мин. Если меню не прошло, проверьте снимки в папке screens, "
                    + "координаты кнопок (--click-benchmark / --click-confirm) или увеличьте --menu-delay.");
            }

            using var process = GameController.FindRunning();
            if (process is null)
            {
                goneSince ??= now;
                if (now - goneSince > TimeSpan.FromSeconds(20))
                {
                    throw new GameExitedException("Процесс бенчмарка завершился, не записав результат (вылет?).");
                }

                GameController.Sleep(TimeSpan.FromSeconds(2), token);
                continue;
            }

            goneSince = null;
            if (startedAt is null && attempts > 0 && attemptPid == process.Id
                && GameController.ReadBytes(process) is { } read && read >= readAtAttempt)
            {
                var expectedBackground = menuReadRate * (now - lastAttemptAt).TotalSeconds;
                if (read - readAtAttempt > expectedBackground + LevelLoadReadThresholdBytes)
                {
                    startedAt = now;
                    Log.Ok("Тест запущен: идёт загрузка сцены. Прогон займёт около 3 минут.");
                }
            }

            if (startedAt is null && attempts < MaxStartAttempts && now - lastAttemptAt > StartRetryInterval)
            {
                var hwnd = GameWindow.Find(process.Id);
                if (hwnd != IntPtr.Zero)
                {
                    Log.Info($"Прохожу меню (попытка {attempts + 1}/{MaxStartAttempts})...");
                    readAtAttempt = GameController.ReadBytes(process) ?? 0;
                    attemptPid = process.Id;
                    lastAttemptAt = DateTime.UtcNow;
                    if (navigator.PerformStartSequence(hwnd, attempts + 1, token))
                    {
                        attempts++;
                    }
                    else
                    {
                        // ввод не отправляли, попытку не считаем, повторим через ~10 с
                        lastAttemptAt = DateTime.UtcNow - StartRetryInterval + TimeSpan.FromSeconds(10);
                    }

                    if (attempts == MaxStartAttempts)
                    {
                        Log.Warn("Не удалось подтвердить старт теста. Если тест не идёт, нажмите в бенчмарке "
                                 + "'Тест быстродействия' -> 'Подтвердить' вручную - инструмент дождётся результата.");
                    }
                }
            }
            else if (startedAt is { } started && now - started > StartConfirmationTimeout && attempts < MaxStartAttempts)
            {
                // видимо, это была не загрузка теста
                Log.Warn("Результата всё нет - повторяю проход по меню.");
                startedAt = null;
            }

            if (now - lastProgress > ProgressInterval)
            {
                lastProgress = now;
                var remaining = deadline - now;
                Log.Info(startedAt is { } s
                    ? $"Идёт тест... прошло {now - s:m\\:ss}, жду файл результата (таймаут через {remaining:m\\:ss})."
                    : $"Жду старта теста... (таймаут через {remaining:m\\:ss}).");
            }

            GameController.Sleep(TimeSpan.FromSeconds(2), token);
        }
    }

    private double MeasureMenuReadRate()
    {
        ulong? before;
        using (var process = GameController.FindRunning())
        {
            before = process is null ? null : GameController.ReadBytes(process);
        }

        var stopwatch = Stopwatch.StartNew();
        GameController.Sleep(options.MenuDelay, token);

        using var after = GameController.FindRunning();
        var afterBytes = after is null ? null : GameController.ReadBytes(after);
        if (before is null || afterBytes is null || afterBytes < before || stopwatch.Elapsed.TotalSeconds < 1)
        {
            return 0;
        }

        // меню в начале ещё догружается, поэтому ограничиваем сверху
        var rate = (afterBytes.Value - before.Value) / stopwatch.Elapsed.TotalSeconds;
        Log.Debug($"Фоновое чтение в меню: {rate / 1024 / 1024:0.0} МБ/с");
        return Math.Min(rate, MaxMenuReadRateBytesPerSecond);
    }

    private string? CaptureResultsScreen(string passDirectory, string suffix)
    {
        try
        {
            GameController.Sleep(TimeSpan.FromSeconds(2), token);
            using var process = GameController.FindRunning();
            var hwnd = process is null ? IntPtr.Zero : GameWindow.Find(process.Id);
            if (hwnd == IntPtr.Zero || !GameWindow.BringToForeground(hwnd))
            {
                return null;
            }

            var image = ScreenCapture.CaptureClient(hwnd);
            if (image is null || image.IsBlank())
            {
                Log.Debug("Экран результатов не захватился (пустой кадр) - снимок не сохранён.");
                return null;
            }

            var file = $"results{suffix}.png";
            PngWriter.Save(image, Path.Combine(passDirectory, file));
            return $"{Path.GetFileName(passDirectory)}/{file}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Debug($"Не удалось сохранить снимок экрана результатов: {ex.Message}");
            return null;
        }
    }
}
