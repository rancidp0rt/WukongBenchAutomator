using System.Drawing;
using WukongBenchAutomator.Infrastructure;
using WukongBenchAutomator.Vision;
using static WukongBenchAutomator.Interop.NativeMethods;

namespace WukongBenchAutomator.Game;

// Ключа автозапуска теста у бенчмарка нет, поэтому жмём кнопки как пользователь.
// Сначала ищем кнопку по тексту (OCR), если не нашли - кликаем по координатам.
internal sealed class MenuNavigator(PointF benchmarkButton, PointF confirmButton, ScreenTextReader? ocr)
{
    public static readonly string[] BenchmarkKeywords = ["Тест быстродействия", "быстродейств", "быстроде", "Benchmark", "基准测试", "性能测试"];
    public static readonly string[] ConfirmKeywords = ["Подтвердить", "подтверд", "Confirm", "确认", "确定"];

    public string? LastMethod { get; private set; }

    public string? DiagnosticsDirectory { get; set; }

    // false - окно не стало активным и ввод не отправляли, иначе клики ушли бы в чужое окно.
    public bool PerformStartSequence(IntPtr hwnd, int attempt, CancellationToken token)
    {
        if (!Focus(hwnd))
        {
            return false;
        }

        return ocr is null
            ? FinishByCoordinates(hwnd, token, enterPressed: false, benchmarkClicked: false, usedText: false)
            : ByText(hwnd, attempt, token);
    }

    private bool ByText(IntPtr hwnd, int attempt, CancellationToken token)
    {
        var enterPressed = false;
        var benchmarkClicks = 0;
        var usedText = false;

        for (var step = 0; step < 6; step++)
        {
            token.ThrowIfCancellationRequested();
            if (!Focus(hwnd))
            {
                return false;
            }

            var image = ScreenCapture.CaptureClient(hwnd);
            if (image is null || image.IsBlank())
            {
                Log.Info("Снимок окна пустой (эксклюзивный полноэкранный режим) - нажимаю по координатам.");
                return FinishByCoordinates(hwnd, token, enterPressed, benchmarkClicks > 0, usedText);
            }

            var lines = ocr!.Read(image);
            var size = new Size(image.Width, image.Height);
            Log.Debug($"OCR, шаг {step}: {string.Join(" | ", lines.Select(l => l.Text))}");

            if (ScreenTextReader.FindButton(lines, ConfirmKeywords, confirmButton, size) is { } confirm)
            {
                ClickAt(hwnd, confirm, "'Подтвердить' (найдена по тексту)");
                LastMethod = "по тексту на экране (OCR)";
                GameController.Sleep(TimeSpan.FromSeconds(1), token);
                return true;
            }

            // диалог не появился - кликаем ещё раз
            if (benchmarkClicks < 2 && ScreenTextReader.FindButton(lines, BenchmarkKeywords, benchmarkButton, size) is { } benchmark)
            {
                ClickAt(hwnd, benchmark, "'Тест быстродействия' (найдена по тексту)");
                benchmarkClicks++;
                usedText = true;
                GameController.Sleep(TimeSpan.FromSeconds(2.5), token);
                continue;
            }

            if (!enterPressed)
            {
                InputSender.PressKey(VK_RETURN); // заставка 'нажмите любую кнопку'
                enterPressed = true;
                GameController.Sleep(TimeSpan.FromSeconds(3), token);
                continue;
            }

            SaveDiagnostics(image, attempt, step);
            Log.Info("Надписи кнопок не распознаны - нажимаю по координатам.");
            return FinishByCoordinates(hwnd, token, enterPressed, benchmarkClicks > 0, usedText);
        }

        return true;
    }

    private bool FinishByCoordinates(IntPtr hwnd, CancellationToken token, bool enterPressed, bool benchmarkClicked, bool usedText)
    {
        if (!enterPressed)
        {
            InputSender.PressKey(VK_RETURN);
            GameController.Sleep(TimeSpan.FromSeconds(3), token);
        }

        if (!benchmarkClicked)
        {
            if (!Focus(hwnd) || !ClickFraction(hwnd, benchmarkButton, "'Тест быстродействия' (по координатам)"))
            {
                return false;
            }

            GameController.Sleep(TimeSpan.FromSeconds(2.5), token);
        }

        if (!Focus(hwnd) || !ClickFraction(hwnd, confirmButton, "'Подтвердить' (по координатам)"))
        {
            return false;
        }

        LastMethod = usedText ? "частично по тексту (OCR), частично по координатам" : "по координатам";
        GameController.Sleep(TimeSpan.FromSeconds(1), token);
        return true;
    }

    private static bool ClickFraction(IntPtr hwnd, PointF fraction, string buttonName)
    {
        var rect = GameWindow.GetClientScreenRect(hwnd);
        return rect.Width > 0 && rect.Height > 0
               && ClickAt(hwnd, new PointF(rect.Width * fraction.X, rect.Height * fraction.Y), buttonName);
    }

    private static bool ClickAt(IntPtr hwnd, PointF clientPoint, string buttonName)
    {
        var rect = GameWindow.GetClientScreenRect(hwnd);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        var x = rect.Left + (int)Math.Round(clientPoint.X);
        var y = rect.Top + (int)Math.Round(clientPoint.Y);
        Log.Info($"Нажимаю {buttonName}");
        Log.Debug($"Клик ({x}, {y}), клиентская область окна {rect}");
        InputSender.Click(x, y);
        return true;
    }

    private void SaveDiagnostics(CapturedImage image, int attempt, int step)
    {
        if (DiagnosticsDirectory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(DiagnosticsDirectory);
            var path = Path.Combine(DiagnosticsDirectory, $"menu_attempt{attempt}_step{step}.png");
            PngWriter.Save(image, path);
            Log.Debug($"Снимок меню для диагностики: {path}");
        }
        catch (Exception ex)
        {
            Log.Debug($"Не удалось сохранить снимок меню: {ex.Message}");
        }
    }

    private static bool Focus(IntPtr hwnd)
    {
        if (GameWindow.BringToForeground(hwnd))
        {
            return true;
        }

        Log.Warn("Не удалось сделать окно бенчмарка активным - ввод не отправлен, повторю позже.");
        return false;
    }
}
