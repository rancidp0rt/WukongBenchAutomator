using System.Text.RegularExpressions;

namespace WukongBenchAutomator.Config;

// Формат: UISettingData=(("ScreenMode", "1"),("ImageQuality", "720"),...). Порядок пар сохраняем.
internal sealed partial class UiSettingData
{
    private readonly List<KeyValuePair<string, string>> _entries;

    private UiSettingData(List<KeyValuePair<string, string>> entries) => _entries = entries;

    public IReadOnlyList<KeyValuePair<string, string>> Entries => _entries;

    public static UiSettingData Parse(string raw)
    {
        var entries = PairRegex().Matches(raw)
            .Select(m => new KeyValuePair<string, string>(m.Groups["key"].Value, m.Groups["value"].Value))
            .ToList();

        // формат поменялся: лучше упасть, чем затереть настройки
        if (entries.Count == 0 && raw.Trim().Trim('(', ')').Trim().Length > 0)
        {
            throw new FormatException($"Не удалось разобрать UISettingData: {raw}");
        }

        return new UiSettingData(entries);
    }

    public bool Contains(string key) => IndexOf(key) >= 0;

    public string? this[string key]
    {
        get
        {
            var index = IndexOf(key);
            return index >= 0 ? _entries[index].Value : null;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var index = IndexOf(key);
            if (index >= 0)
            {
                _entries[index] = new KeyValuePair<string, string>(_entries[index].Key, value);
            }
            else
            {
                _entries.Add(new KeyValuePair<string, string>(key, value));
            }
        }
    }

    public int? GetInt(string key) => int.TryParse(this[key], out var n) ? n : null;

    public string Serialize() =>
        "(" + string.Join(",", _entries.Select(e => $"(\"{e.Key}\", \"{e.Value}\")")) + ")";

    private int IndexOf(string key) =>
        _entries.FindIndex(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("""\(\s*"(?<key>[^"]*)"\s*,\s*"(?<value>[^"]*)"\s*\)""")]
    private static partial Regex PairRegex();
}
