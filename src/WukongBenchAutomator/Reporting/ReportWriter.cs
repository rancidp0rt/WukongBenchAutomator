using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using WukongBenchAutomator.Infrastructure;

namespace WukongBenchAutomator.Reporting;

internal static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void WriteAll(SessionReport report, string outputDirectory)
    {
        report.OutputDirectory = outputDirectory;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            File.WriteAllText(Path.Combine(outputDirectory, "report.md"), MarkdownReport.Build(report), utf8);
            File.WriteAllText(Path.Combine(outputDirectory, "report.html"), HtmlReport.Build(report), utf8);
            File.WriteAllText(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, JsonOptions), utf8);
        }
        catch (Exception ex)
        {
            Log.Error($"Не удалось записать файлы отчёта: {ex.Message}");
        }

        ConsoleReport.Print(report);
    }
}
