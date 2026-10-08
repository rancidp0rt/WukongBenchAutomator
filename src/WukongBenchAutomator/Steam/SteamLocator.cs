using Microsoft.Win32;
using WukongBenchAutomator.Infrastructure;

namespace WukongBenchAutomator.Steam;

internal sealed record BenchmarkInstall(string RootDir, string? SteamRoot, string? BuildId)
{
    public string ConfigPath => Path.Combine(RootDir, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");

    public string? FindExecutable()
    {
        string[] candidates =
        [
            "b1.exe",
            "b1_benchmark.exe",
            Path.Combine("b1", "Binaries", "Win64", "b1-Win64-Shipping.exe"),
        ];

        return candidates.Select(c => Path.Combine(RootDir, c)).FirstOrDefault(File.Exists);
    }
}

internal static class SteamLocator
{
    public const int BenchmarkAppId = 3132990;

    public static string? FindSteamRoot()
    {
        var candidates = new[]
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
        };

        return candidates
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!.Replace('/', Path.DirectorySeparatorChar)))
            .FirstOrDefault(Directory.Exists);
    }

    public static IReadOnlyList<string> GetLibraryFolders(string steamRoot)
    {
        var folders = new List<string> { steamRoot };
        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            return folders;
        }

        try
        {
            var root = VdfNode.Parse(File.ReadAllText(vdfPath));
            var libraries = root["libraryfolders"] ?? root["LibraryFolders"];
            foreach (var (_, node) in libraries?.Children ?? new Dictionary<string, VdfNode>())
            {
                // Новый формат: "0" { "path" "D:\\SteamLibrary" ... }, старый: "1" "D:\\SteamLibrary"
                var path = node.Value ?? node.GetString("path");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    folders.Add(path);
                }
            }
        }
        catch (FormatException ex)
        {
            Log.Warn($"Не удалось разобрать {vdfPath}: {ex.Message}");
        }

        return folders
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static BenchmarkInstall? FindBenchmark(out string? steamRoot)
    {
        steamRoot = FindSteamRoot();
        if (steamRoot is null)
        {
            return null;
        }

        foreach (var library in GetLibraryFolders(steamRoot))
        {
            var steamApps = Path.Combine(library, "steamapps");
            var manifestPath = Path.Combine(steamApps, $"appmanifest_{BenchmarkAppId}.acf");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var state = VdfNode.Parse(File.ReadAllText(manifestPath))["AppState"];
                    var installDir = state?.GetString("installdir");
                    if (!string.IsNullOrWhiteSpace(installDir))
                    {
                        var dir = Path.Combine(steamApps, "common", installDir);
                        if (Directory.Exists(dir))
                        {
                            return new BenchmarkInstall(dir, steamRoot, state?.GetString("buildid"));
                        }
                    }
                }
                catch (FormatException ex)
                {
                    Log.Warn($"Не удалось разобрать {manifestPath}: {ex.Message}");
                }
            }

            // манифеста нет (например, папку скопировали руками)
            var common = Path.Combine(steamApps, "common");
            if (!Directory.Exists(common))
            {
                continue;
            }

            var guess = Directory.EnumerateDirectories(common)
                .Where(d => Path.GetFileName(d).Contains("Benchmark", StringComparison.OrdinalIgnoreCase)
                            && Path.GetFileName(d).Contains("Wukong", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(d => Directory.Exists(Path.Combine(d, "b1")));
            if (guess is not null)
            {
                return new BenchmarkInstall(guess, steamRoot, null);
            }
        }

        return null;
    }

    public static BenchmarkInstall FromDirectory(string dir)
    {
        var full = Path.GetFullPath(dir);
        if (!Directory.Exists(Path.Combine(full, "b1")))
        {
            throw new DirectoryNotFoundException(
                $"В папке '{full}' нет подпапки b1 - это не папка Black Myth: Wukong Benchmark Tool.");
        }

        return new BenchmarkInstall(full, FindSteamRoot(), null);
    }
}
