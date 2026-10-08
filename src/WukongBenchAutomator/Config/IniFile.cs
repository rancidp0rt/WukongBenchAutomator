using System.Text;

namespace WukongBenchAutomator.Config;

// Правим построчно, чтобы не трогать остальное содержимое, кодировку и переводы строк.
internal sealed class IniFile
{
    private readonly List<string> _lines;

    private IniFile(List<string> lines, Encoding encoding, bool writeBom, string newLine, bool endsWithNewLine)
    {
        _lines = lines;
        Encoding = encoding;
        WriteBom = writeBom;
        NewLine = newLine;
        EndsWithNewLine = endsWithNewLine;
    }

    public Encoding Encoding { get; }
    public bool WriteBom { get; }
    public string NewLine { get; }
    public bool EndsWithNewLine { get; }

    public IEnumerable<string> Sections => _lines
        .Select(TryGetSectionName)
        .Where(name => name is not null)
        .Select(name => name!);

    public static IniFile Load(string path) => FromBytes(File.ReadAllBytes(path));

    public static IniFile FromBytes(byte[] bytes)
    {
        // UE пишет ini то в UTF-8, то в UTF-16 LE с BOM
        Encoding encoding;
        var bomLength = 0;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            bomLength = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
            bomLength = 2;
        }
        else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            bomLength = 3;
        }
        else
        {
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        var text = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        return Parse(text, encoding, writeBom: bomLength > 0);
    }

    public static IniFile Parse(string text) => Parse(text, new UTF8Encoding(false), writeBom: false);

    private static IniFile Parse(string text, Encoding encoding, bool writeBom)
    {
        var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : "\r\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var endsWithNewLine = lines.Count > 1 && lines[^1].Length == 0;
        if (endsWithNewLine)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new IniFile(lines, encoding, writeBom, newLine, endsWithNewLine);
    }

    public bool HasSection(string section) => FindSection(section) >= 0;

    public string? Get(string section, string key)
    {
        var index = FindKey(section, key);
        if (index < 0)
        {
            return null;
        }

        var line = _lines[index];
        return line[(line.IndexOf('=') + 1)..];
    }

    public void Set(string section, string key, string value)
    {
        var index = FindKey(section, key);
        if (index >= 0)
        {
            var line = _lines[index];
            _lines[index] = line[..(line.IndexOf('=') + 1)] + value;
            return;
        }

        var header = FindSection(section);
        if (header < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0)
            {
                _lines.Add(string.Empty);
            }

            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return;
        }

        // после последней непустой строки секции
        var insertAt = header + 1;
        for (var i = header + 1; i < _lines.Count && TryGetSectionName(_lines[i]) is null; i++)
        {
            if (_lines[i].Trim().Length > 0)
            {
                insertAt = i + 1;
            }
        }

        _lines.Insert(insertAt, $"{key}={value}");
    }

    public string? FindSectionContainingKey(string key)
    {
        string? current = null;
        foreach (var line in _lines)
        {
            var name = TryGetSectionName(line);
            if (name is not null)
            {
                current = name;
                continue;
            }

            if (current is not null && KeyOf(line) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }
        }

        return null;
    }

    public string ToText()
    {
        var text = string.Join(NewLine, _lines);
        return EndsWithNewLine ? text + NewLine : text;
    }

    public byte[] ToBytes()
    {
        var body = Encoding.GetBytes(ToText());
        if (!WriteBom)
        {
            return body;
        }

        var preamble = Encoding.GetPreamble();
        return [.. preamble, .. body];
    }

    public void Save(string path)
    {
        // через временный файл, чтобы не оставить полузаписанный конфиг
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, ToBytes());
        File.Move(temp, path, overwrite: true);
    }

    private int FindSection(string section)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (string.Equals(TryGetSectionName(_lines[i]), section, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindKey(string section, string key)
    {
        var header = FindSection(section);
        if (header < 0)
        {
            return -1;
        }

        for (var i = header + 1; i < _lines.Count && TryGetSectionName(_lines[i]) is null; i++)
        {
            if (KeyOf(_lines[i]) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? TryGetSectionName(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 2 && trimmed[0] == '[' && trimmed[^1] == ']' ? trimmed[1..^1].Trim() : null;
    }

    private static string? KeyOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] is ';' or '#')
        {
            return null;
        }

        var eq = trimmed.IndexOf('=');
        return eq > 0 ? trimmed[..eq].Trim() : null;
    }
}
