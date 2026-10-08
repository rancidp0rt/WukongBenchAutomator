using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using WukongBenchAutomator.Infrastructure;

namespace WukongBenchAutomator.Vision;

internal sealed record TextLine(string Text, RectangleF Bounds);

internal sealed class ScreenTextReader
{
    private static readonly string[] PreferredLanguages = ["ru", "en", "zh"];

    // лишние буквы помимо ключа: опечатки OCR, соседнее слово
    private const int MaxExtraCharacters = 12;

    private readonly IReadOnlyList<OcrEngine> _engines;

    private ScreenTextReader(IReadOnlyList<OcrEngine> engines, IReadOnlyList<string> languages)
    {
        _engines = engines;
        Languages = languages;
    }

    public IReadOnlyList<string> Languages { get; }

    public static ScreenTextReader? TryCreate()
    {
        try
        {
            var languages = OcrEngine.AvailableRecognizerLanguages
                .OrderBy(l => Rank(l.LanguageTag))
                .Take(3)
                .ToList();
            var engines = languages
                .Select(OcrEngine.TryCreateFromLanguage)
                .Where(e => e is not null)
                .ToList();
            return engines.Count == 0 ? null : new ScreenTextReader(engines, languages.Select(l => l.LanguageTag).ToList());
        }
        catch (Exception ex)
        {
            Log.Debug($"OCR Windows недоступен: {ex.Message}");
            return null;
        }
    }

    public (IReadOnlyList<TextLine> Lines, Size Size) ReadFile(string path)
    {
        using var stream = File.OpenRead(path).AsRandomAccessStream();
        var decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
        using var bitmap = decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask().GetAwaiter().GetResult();
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyToBuffer(pixels.AsBuffer());
        var lines = Read(new CapturedImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels));
        return (lines, new Size(bitmap.PixelWidth, bitmap.PixelHeight));
    }

    public IReadOnlyList<TextLine> Read(CapturedImage source)
    {
        // На старых сборках Windows 10 OCR принимает не больше 2600 px по стороне.
        var factor = Math.Max(1, (int)Math.Ceiling(Math.Max(source.Width, source.Height) / (double)OcrEngine.MaxImageDimension));
        var image = factor == 1 ? source : source.Downscale(factor);

        var lines = new List<TextLine>();
        var buffer = CryptographicBuffer.CreateFromByteArray(image.Bgra);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer, BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);

        foreach (var engine in _engines)
        {
            try
            {
                var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
                foreach (var line in result.Lines)
                {
                    var words = line.Words.Select(w => w.BoundingRect).ToList();
                    if (words.Count == 0)
                    {
                        continue;
                    }

                    var left = words.Min(r => r.X);
                    var top = words.Min(r => r.Y);
                    var right = words.Max(r => r.X + r.Width);
                    var bottom = words.Max(r => r.Y + r.Height);
                    lines.Add(new TextLine(line.Text, new RectangleF(
                        (float)left * factor, (float)top * factor, (float)(right - left) * factor, (float)(bottom - top) * factor)));
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"OCR ({engine.RecognizerLanguage.LanguageTag}) не сработал: {ex.Message}");
            }
        }

        return lines;
    }

    // Из подходящих строк берём ближайшую к ожидаемому месту и покороче, чтобы не кликнуть по заголовку.
    public static PointF? FindButton(IReadOnlyList<TextLine> lines, IReadOnlyList<string> keywords, PointF expectedFraction, Size imageSize)
    {
        var expected = new PointF(expectedFraction.X * imageSize.Width, expectedFraction.Y * imageSize.Height);
        var diagonal = Math.Sqrt((double)imageSize.Width * imageSize.Width + (double)imageSize.Height * imageSize.Height);
        PointF? best = null;
        var bestScore = double.MaxValue;

        foreach (var line in lines)
        {
            var text = Normalize(line.Text);
            foreach (var keyword in keywords)
            {
                var key = Normalize(keyword);
                if (key.Length == 0 || text.Length - key.Length > MaxExtraCharacters)
                {
                    continue; // длинная строка - заголовок, а не надпись на кнопке
                }

                // OCR теряет буквы у края кнопки: 'одтвердить'
                var edits = ApproximateSubstringDistance(key, text);
                if (edits > AllowedEdits(key))
                {
                    continue;
                }

                var center = new PointF(line.Bounds.X + line.Bounds.Width / 2, line.Bounds.Y + line.Bounds.Height / 2);
                var distance = Math.Sqrt(Math.Pow(center.X - expected.X, 2) + Math.Pow(center.Y - expected.Y, 2)) / diagonal;
                if (distance > 0.45)
                {
                    continue;
                }

                var score = distance + 0.02 * Math.Max(0, text.Length - key.Length) + 0.05 * edits;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = center;
                }
            }
        }

        return best;
    }

    internal static int AllowedEdits(string key) => key.Length >= 10 ? 2 : key.Length >= 6 ? 1 : 0;

    // Редакционное расстояние до лучшей подстроки (алгоритм Селлерса).
    internal static int ApproximateSubstringDistance(string key, string text)
    {
        var previous = new int[text.Length + 1]; // строка i-1: начало совпадения в тексте бесплатно
        var current = new int[text.Length + 1];
        for (var i = 1; i <= key.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= text.Length; j++)
            {
                var substitution = previous[j - 1] + (key[i - 1] == text[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return key.Length == 0 ? 0 : previous.Min();
    }

    internal static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static int Rank(string tag)
    {
        var index = Array.FindIndex(PreferredLanguages, p => tag.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? PreferredLanguages.Length : index;
    }
}
