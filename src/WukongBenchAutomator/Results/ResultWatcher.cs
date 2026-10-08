using WukongBenchAutomator.Infrastructure;

namespace WukongBenchAutomator.Results;

// Результат лежит в %TEMP%\b1\BenchMarkHistory\Tool\<unix-time>. Считаем файл готовым,
// когда размер перестал меняться и JSON парсится целиком.
internal sealed class ResultWatcher
{
    private readonly IReadOnlyList<string> _directories;
    private readonly Dictionary<string, long> _lastSizes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DateTime> _snapshot = new(StringComparer.OrdinalIgnoreCase);

    public ResultWatcher(IReadOnlyList<string>? directories = null) =>
        _directories = directories ?? DefaultDirectories();

    public IReadOnlyList<string> Directories => _directories;

    public static IReadOnlyList<string> DefaultDirectories()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
            {
                Path.Combine(Path.GetTempPath(), "b1", "BenchMarkHistory"),
                Path.Combine(localAppData, "Temp", "b1", "BenchMarkHistory"),
            }
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void TakeSnapshot()
    {
        _snapshot = EnumerateFiles().ToDictionary(f => f.FullName, f => f.LastWriteTimeUtc, StringComparer.OrdinalIgnoreCase);
        _lastSizes.Clear();
    }

    public BenchmarkResult? TryGetNewResult()
    {
        foreach (var file in EnumerateFiles().OrderByDescending(f => f.LastWriteTimeUtc))
        {
            if (_snapshot.TryGetValue(file.FullName, out var knownWriteTime) && knownWriteTime == file.LastWriteTimeUtc)
            {
                continue;
            }

            var size = file.Length;
            if (size == 0 || !_lastSizes.TryGetValue(file.FullName, out var previousSize) || previousSize != size)
            {
                _lastSizes[file.FullName] = size;
                continue;
            }

            try
            {
                return ResultParser.Parse(file.FullName);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                Log.Debug($"Файл {file.FullName} пока не разбирается: {ex.Message}");
            }
        }

        return null;
    }

    private IEnumerable<FileInfo> EnumerateFiles() =>
        _directories
            .Where(Directory.Exists)
            .SelectMany(d =>
            {
                try
                {
                    return new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Array.Empty<FileInfo>();
                }
            });
}
