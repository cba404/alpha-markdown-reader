using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;
using AlphaNative.Models;

namespace AlphaNative.Services;

public static class SyntaxHighlighter
{
    private sealed record Palette(
        Brush Keyword,
        Brush String,
        Brush Comment,
        Brush Number,
        Brush Type,
        Brush Function,
        Brush Plain);

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["js"] = "javascript", ["jsx"] = "javascript", ["mjs"] = "javascript",
        ["ts"] = "typescript", ["tsx"] = "typescript",
        ["py"] = "python", ["rb"] = "ruby", ["rs"] = "rust",
        ["sh"] = "bash", ["shell"] = "bash", ["zsh"] = "bash",
        ["html"] = "xml", ["htm"] = "xml", ["svg"] = "xml",
        ["yml"] = "yaml", ["md"] = "markdown", ["c++"] = "cpp", ["cxx"] = "cpp",
        ["cs"] = "csharp", ["text"] = "plaintext", ["txt"] = "plaintext"
    };

    public static string NormalizeLanguage(string? language)
    {
        var value = (language ?? string.Empty).Trim().ToLowerInvariant();
        return Aliases.TryGetValue(value, out var normalized) ? normalized : value;
    }

    public static string DisplayName(string? language)
    {
        var value = NormalizeLanguage(language);
        return value switch
        {
            "javascript" => "JavaScript",
            "typescript" => "TypeScript",
            "python" => "Python",
            "csharp" => "C#",
            "cpp" => "C++",
            "xml" => "HTML/XML",
            "css" => "CSS",
            "json" => "JSON",
            "yaml" => "YAML",
            "sql" => "SQL",
            "bash" => "Bash",
            "java" => "Java",
            "go" => "Go",
            "rust" => "Rust",
            "kotlin" => "Kotlin",
            "swift" => "Swift",
            "php" => "PHP",
            "ruby" => "Ruby",
            "markdown" => "Markdown",
            "plaintext" or "" => "Text",
            _ => value.Length == 0 ? "Text" : char.ToUpperInvariant(value[0]) + value[1..]
        };
    }

    public static string HighlightHtml(string code, string? language)
    {
        var normalized = NormalizeLanguage(language);
        var regex = CreateRegex(normalized);
        if (regex is null) return WebUtility.HtmlEncode(code);

        var builder = new StringBuilder(code.Length + 128);
        var offset = 0;
        foreach (Match match in regex.Matches(code))
        {
            if (match.Index > offset)
            {
                builder.Append(WebUtility.HtmlEncode(code[offset..match.Index]));
            }

            var cssClass = match.Groups["comment"].Success ? "tok-comment"
                : match.Groups["string"].Success ? "tok-string"
                : match.Groups["keyword"].Success ? "tok-keyword"
                : match.Groups["number"].Success ? "tok-number"
                : match.Groups["type"].Success ? "tok-type"
                : match.Groups["function"].Success ? "tok-function"
                : string.Empty;

            if (cssClass.Length == 0)
            {
                builder.Append(WebUtility.HtmlEncode(match.Value));
            }
            else
            {
                builder.Append("<span class=\"").Append(cssClass).Append("\">")
                    .Append(WebUtility.HtmlEncode(match.Value)).Append("</span>");
            }
            offset = match.Index + match.Length;
        }

        if (offset < code.Length)
        {
            builder.Append(WebUtility.HtmlEncode(code[offset..]));
        }
        return builder.ToString();
    }

    public static void Highlight(string code, string? language, InlineCollection target, RendererTheme theme)
    {
        ArgumentNullException.ThrowIfNull(target);

        var palette = CreatePalette(theme.IsDark);
        var normalized = NormalizeLanguage(language);
        var regex = CreateRegex(normalized);
        if (regex is null)
        {
            target.Add(new Run(code) { Foreground = palette.Plain });
            return;
        }

        var offset = 0;
        foreach (Match match in regex.Matches(code))
        {
            if (match.Index > offset)
            {
                target.Add(new Run(code[offset..match.Index]) { Foreground = palette.Plain });
            }

            var brush = match.Groups["comment"].Success ? palette.Comment
                : match.Groups["string"].Success ? palette.String
                : match.Groups["keyword"].Success ? palette.Keyword
                : match.Groups["number"].Success ? palette.Number
                : match.Groups["type"].Success ? palette.Type
                : match.Groups["function"].Success ? palette.Function
                : palette.Plain;

            target.Add(new Run(match.Value)
            {
                Foreground = brush,
                FontStyle = match.Groups["comment"].Success
                    ? System.Windows.FontStyles.Italic
                    : System.Windows.FontStyles.Normal,
                FontWeight = match.Groups["keyword"].Success
                    ? System.Windows.FontWeights.SemiBold
                    : System.Windows.FontWeights.Normal
            });
            offset = match.Index + match.Length;
        }

        if (offset < code.Length)
        {
            target.Add(new Run(code[offset..]) { Foreground = palette.Plain });
        }
    }

    private static Regex? CreateRegex(string language)
    {
        if (language is "plaintext" or "") return null;

        var keywords = language switch
        {
            "javascript" or "typescript" => "break|case|catch|class|const|continue|debugger|default|delete|do|else|export|extends|finally|for|from|function|get|if|import|in|instanceof|let|new|of|return|set|static|super|switch|this|throw|try|typeof|var|void|while|with|yield|async|await|interface|implements|private|protected|public|readonly|enum|namespace|type",
            "python" => "and|as|assert|async|await|break|class|continue|def|del|elif|else|except|finally|for|from|global|if|import|in|is|lambda|nonlocal|not|or|pass|raise|return|try|while|with|yield|match|case",
            "csharp" => "abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|record|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|virtual|void|volatile|while|async|await|var|dynamic",
            "java" or "kotlin" => "abstract|assert|boolean|break|byte|case|catch|char|class|const|continue|default|do|double|else|enum|extends|final|finally|float|for|if|implements|import|instanceof|int|interface|long|native|new|null|package|private|protected|public|return|short|static|strictfp|super|switch|synchronized|this|throw|throws|transient|try|void|volatile|while|fun|val|var|when|object|data|sealed|override|suspend",
            "c" or "cpp" => "alignas|alignof|asm|auto|bool|break|case|catch|char|class|const|constexpr|continue|default|delete|do|double|else|enum|explicit|export|extern|false|float|for|friend|goto|if|inline|int|long|mutable|namespace|new|nullptr|operator|private|protected|public|register|reinterpret_cast|return|short|signed|sizeof|static|struct|switch|template|this|throw|true|try|typedef|typename|union|unsigned|using|virtual|void|volatile|while",
            "go" => "break|default|func|interface|select|case|defer|go|map|struct|chan|else|goto|package|switch|const|fallthrough|if|range|type|continue|for|import|return|var",
            "rust" => "as|async|await|break|const|continue|crate|dyn|else|enum|extern|false|fn|for|if|impl|in|let|loop|match|mod|move|mut|pub|ref|return|self|Self|static|struct|super|trait|true|type|unsafe|use|where|while",
            "php" => "abstract|and|array|as|break|callable|case|catch|class|clone|const|continue|declare|default|do|echo|else|elseif|empty|enddeclare|endfor|endforeach|endif|endswitch|endwhile|eval|exit|extends|final|finally|fn|for|foreach|function|global|goto|if|implements|include|instanceof|interface|isset|list|match|namespace|new|null|or|print|private|protected|public|readonly|require|return|static|switch|throw|trait|try|unset|use|var|while|xor|yield",
            "sql" => "add|all|alter|and|any|as|asc|backup|between|by|case|check|column|constraint|create|database|default|delete|desc|distinct|drop|exec|exists|foreign|from|full|group|having|in|index|inner|insert|into|is|join|key|left|like|limit|not|null|or|order|outer|primary|procedure|right|rownum|select|set|table|top|truncate|union|unique|update|values|view|where",
            "bash" => "case|do|done|elif|else|esac|export|fi|for|function|if|in|local|readonly|return|select|then|time|until|while|declare|set|unset",
            _ => string.Empty
        };

        var commentPattern = language switch
        {
            "python" or "bash" or "ruby" or "yaml" => @"\#.*?$",
            "sql" => @"--.*?$|/\*[\s\S]*?\*/",
            "xml" => @"<!--[\s\S]*?-->",
            "css" => @"/\*[\s\S]*?\*/",
            "json" => @"(?!)",
            _ => @"//.*?$|/\*[\s\S]*?\*/"
        };

        var stringPattern = language switch
        {
            "python" => "'''[\\s\\S]*?'''|\"\"\"[\\s\\S]*?\"\"\"|'(?:\\\\.|[^'\\\\])*'|\"(?:\\\\.|[^\"\\\\])*\"",
            "javascript" or "typescript" => "`(?:\\\\.|[^`\\\\])*`|'(?:\\\\.|[^'\\\\])*'|\"(?:\\\\.|[^\"\\\\])*\"",
            "xml" => "\"[^\"]*\"|'[^']*'",
            _ => "@?\"(?:\"\"|\\\\.|[^\"])*\"|'(?:\\\\.|[^'\\\\])*'"
        };

        var keywordPattern = string.IsNullOrEmpty(keywords) ? @"(?!)" : $@"\b(?:{keywords})\b";
        var typePattern = language switch
        {
            "typescript" or "csharp" or "java" or "kotlin" or "c" or "cpp" or "go" or "rust" => @"\b[A-Z][A-Za-z0-9_]*\b",
            "xml" => @"</?[A-Za-z][A-Za-z0-9:_-]*|[A-Za-z_:][-A-Za-z0-9_:.]*(?=\s*=)",
            "css" => @"[#.]?[A-Za-z_-][A-Za-z0-9_-]*(?=\s*\{)|[A-Za-z-]+(?=\s*:)",
            "json" => "\"(?:\\\\.|[^\"\\\\])*\"(?=\\s*:)",
            _ => @"(?!)"
        };
        const string functionPattern = @"\b[A-Za-z_$][A-Za-z0-9_$]*(?=\s*\()";
        const string numberPattern = @"\b(?:0x[0-9a-fA-F]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)\b|\b(?:true|false|null|None|True|False)\b";

        var combined = $@"(?<comment>{commentPattern})|(?<string>{stringPattern})|(?<keyword>{keywordPattern})|(?<number>{numberPattern})|(?<type>{typePattern})|(?<function>{functionPattern})";
        return new Regex(
            combined,
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));
    }

    private static Palette CreatePalette(bool dark)
    {
        return dark
            ? new Palette(B("#FF7B72"), B("#A5D6FF"), B("#8B949E"), B("#79C0FF"), B("#FFA657"), B("#D2A8FF"), B("#E6EDF3"))
            : new Palette(B("#CF222E"), B("#0A3069"), B("#6E7781"), B("#0550AE"), B("#953800"), B("#8250DF"), B("#24292F"));
    }

    private static SolidColorBrush B(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
