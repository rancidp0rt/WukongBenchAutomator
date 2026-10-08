using System.Globalization;
using System.Text.RegularExpressions;

namespace WukongBenchAutomator.Cli;

internal readonly record struct Resolution(int Width, int Height)
{
    public bool Is16By9 => Width * 9 == Height * 16;

    public override string ToString() => $"{Width}x{Height}";

    public static bool TryParse(string? text, out Resolution resolution)
    {
        resolution = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = Regex.Match(text, @"(\d{3,5})\s*[x×X*]\s*(\d{3,5})");
        if (!match.Success)
        {
            return false;
        }

        resolution = new Resolution(
            int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
        return true;
    }
}
