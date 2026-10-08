using System.Text;

namespace WukongBenchAutomator.Steam;

internal sealed class VdfNode
{
    private readonly Dictionary<string, VdfNode> _children = new(StringComparer.OrdinalIgnoreCase);

    public string? Value { get; private init; }

    public IReadOnlyDictionary<string, VdfNode> Children => _children;

    public VdfNode? this[string key] => _children.GetValueOrDefault(key);

    public string? GetString(string key) => this[key]?.Value;

    public static VdfNode Parse(string text)
    {
        var tokens = Tokenize(text);
        var pos = 0;
        var root = new VdfNode();
        ParseBody(root, tokens, ref pos, nested: false);
        return root;
    }

    private static void ParseBody(VdfNode target, List<Token> tokens, ref int pos, bool nested)
    {
        while (pos < tokens.Count)
        {
            var token = tokens[pos++];
            if (token.Kind == TokenKind.Close)
            {
                if (!nested)
                {
                    throw new FormatException("VDF: лишняя '}'");
                }

                return;
            }

            if (token.Kind != TokenKind.String)
            {
                throw new FormatException("VDF: ожидался ключ");
            }

            if (pos >= tokens.Count)
            {
                throw new FormatException($"VDF: нет значения для ключа '{token.Text}'");
            }

            var value = tokens[pos++];
            switch (value.Kind)
            {
                case TokenKind.String:
                    target._children[token.Text] = new VdfNode { Value = value.Text };
                    break;
                case TokenKind.Open:
                    var child = new VdfNode();
                    ParseBody(child, tokens, ref pos, nested: true);
                    target._children[token.Text] = child;
                    break;
                default:
                    throw new FormatException($"VDF: неожиданная '}}' после ключа '{token.Text}'");
            }
        }

        if (nested)
        {
            throw new FormatException("VDF: не закрыта '{'");
        }
    }

    private enum TokenKind
    {
        String,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '{')
            {
                tokens.Add(new Token(TokenKind.Open, "{"));
                i++;
            }
            else if (c == '}')
            {
                tokens.Add(new Token(TokenKind.Close, "}"));
                i++;
            }
            else if (c == '"')
            {
                tokens.Add(new Token(TokenKind.String, ReadQuoted(text, ref i)));
            }
            else
            {
                var start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.String, text[start..i]));
            }
        }

        return tokens;
    }

    private static string ReadQuoted(string text, ref int i)
    {
        var sb = new StringBuilder();
        i++; // открывающая кавычка
        while (i < text.Length)
        {
            var c = text[i++];
            if (c == '"')
            {
                return sb.ToString();
            }

            if (c == '\\' && i < text.Length)
            {
                var escaped = text[i++];
                sb.Append(escaped switch
                {
                    'n' => '\n',
                    't' => '\t',
                    _ => escaped, // \\ и \"
                });
                continue;
            }

            sb.Append(c);
        }

        throw new FormatException("VDF: не закрыта кавычка");
    }
}
