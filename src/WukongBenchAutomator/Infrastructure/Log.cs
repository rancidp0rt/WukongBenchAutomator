using System.Text;

namespace WukongBenchAutomator.Infrastructure;

internal static class Log
{
    private static readonly object Sync = new();
    private static StreamWriter? _file;

    public static void AttachFile(string path)
    {
        lock (Sync)
        {
            _file?.Dispose();
            _file = new StreamWriter(path, append: true, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    public static void DetachFile()
    {
        lock (Sync)
        {
            _file?.Dispose();
            _file = null;
        }
    }

    public static void Step(string message) => Write("STEP", message, ConsoleColor.Cyan);
    public static void Info(string message) => Write("INFO", message, ConsoleColor.Gray);
    public static void Ok(string message) => Write(" OK ", message, ConsoleColor.Green);
    public static void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    public static void Error(string message) => Write("ERR ", message, ConsoleColor.Red);

    public static void Debug(string message)
    {
        lock (Sync)
        {
            _file?.WriteLine($"{DateTime.Now:HH:mm:ss} [DBG ] {message}");
        }
    }

    private static void Write(string level, string message, ConsoleColor color)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        lock (Sync)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(line);
            Console.ForegroundColor = previous;
            _file?.WriteLine(line);
        }
    }
}
