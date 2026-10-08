using System.Diagnostics;
using WukongBenchAutomator.Cli;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.Steam;
using static WukongBenchAutomator.Interop.NativeMethods;

namespace WukongBenchAutomator.Game;

internal sealed class GameController(BenchmarkInstall install, LaunchMode mode)
{
    // b1.exe и Steam только запускают этот процесс.
    public const string ShippingProcessName = "b1-Win64-Shipping";

    private static readonly string[] LauncherProcessNames = ["b1", "b1_benchmark"];

    public void Launch()
    {
        if (mode == LaunchMode.Steam)
        {
            var steamExe = install.SteamRoot is null ? null : Path.Combine(install.SteamRoot, "steam.exe");
            if (steamExe is not null && File.Exists(steamExe))
            {
                Log.Info($"Запуск через Steam: steam.exe -applaunch {SteamLocator.BenchmarkAppId}");
                Process.Start(new ProcessStartInfo(steamExe, $"-applaunch {SteamLocator.BenchmarkAppId}")
                {
                    UseShellExecute = false,
                })?.Dispose();
                return;
            }

            Log.Info($"steam.exe не найден, запуск через steam://rungameid/{SteamLocator.BenchmarkAppId}");
            Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamLocator.BenchmarkAppId}")
            {
                UseShellExecute = true,
            })?.Dispose();
            return;
        }

        var exe = install.FindExecutable()
                  ?? throw new FileNotFoundException($"Не найден исполняемый файл бенчмарка в {install.RootDir}");
        Log.Info($"Прямой запуск: {exe}");
        Process.Start(new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
        })?.Dispose();
    }

    public static Process? FindRunning()
    {
        var processes = Process.GetProcessesByName(ShippingProcessName);
        foreach (var extra in processes.Skip(1))
        {
            extra.Dispose();
        }

        return processes.FirstOrDefault();
    }

    public static bool IsRunning()
    {
        using var process = FindRunning();
        return process is not null;
    }

    // Только Kill: при обычном выходе игра сохранит свои настройки поверх наших.
    public int KillAll()
    {
        var killed = 0;
        foreach (var process in Process.GetProcessesByName(ShippingProcessName))
        {
            using (process)
            {
                killed += TryKill(process) ? 1 : 0;
            }
        }

        foreach (var name in LauncherProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (IsInsideInstall(process))
                    {
                        killed += TryKill(process) ? 1 : 0;
                    }
                }
            }
        }

        return killed;
    }

    public static Process WaitForProcess(TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            var process = FindRunning();
            if (process is not null)
            {
                return process;
            }

            Sleep(TimeSpan.FromSeconds(1), token);
        }

        throw new TimeoutException(
            $"За {timeout.TotalMinutes:0} мин не появился процесс {ShippingProcessName}. "
            + "Проверьте, что Steam запущен и вы вошли в аккаунт.");
    }

    public static IntPtr WaitForWindow(Process process, TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            process.Refresh();
            if (process.HasExited)
            {
                throw new GameExitedException("Процесс бенчмарка завершился, не открыв окно.");
            }

            var hwnd = GameWindow.Find(process.Id);
            if (hwnd != IntPtr.Zero)
            {
                return hwnd;
            }

            Sleep(TimeSpan.FromSeconds(1), token);
        }

        throw new TimeoutException($"За {timeout.TotalMinutes:0} мин не появилось окно бенчмарка.");
    }

    // WM_CLOSE нужен только при первом запуске, чтобы игра сама создала конфиг.
    public static bool CloseGracefully(Process process, IntPtr hwnd, TimeSpan timeout)
    {
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        return process.WaitForExit(timeout);
    }

    public static ulong? ReadBytes(Process process)
    {
        try
        {
            return GetProcessIoCounters(process.Handle, out var counters) ? counters.ReadTransferCount : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Sleep(TimeSpan duration, CancellationToken token)
    {
        if (token.WaitHandle.WaitOne(duration))
        {
            token.ThrowIfCancellationRequested();
        }
    }

    private bool IsInsideInstall(Process process)
    {
        try
        {
            return process.MainModule?.FileName?.StartsWith(install.RootDir, StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(15_000);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"Не удалось завершить {process.ProcessName}: {ex.Message}");
            return false;
        }
    }
}
