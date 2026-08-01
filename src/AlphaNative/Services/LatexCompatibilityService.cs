using System.Text.RegularExpressions;

namespace AlphaNative.Services;

/// <summary>
/// Provides conservative compatibility fallbacks for valid LaTeX commands that
/// are not recognized by the native XAML-Math parser used by the WPF preview.
/// The Markdown source is never modified; only the formula sent to the native
/// renderer is normalized, and only after parsing the original formula fails.
/// </summary>
internal static partial class LatexCompatibilityService
{
    private static readonly IReadOnlyDictionary<string, string> CommandAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [@"\longrightarrow"] = @"\rightarrow",
            [@"\longleftarrow"] = @"\leftarrow",
            [@"\longleftrightarrow"] = @"\leftrightarrow",
            [@"\Longrightarrow"] = @"\Rightarrow",
            [@"\Longleftarrow"] = @"\Leftarrow",
            [@"\Longleftrightarrow"] = @"\Leftrightarrow",
            [@"\longmapsto"] = @"\mapsto",
            [@"\implies"] = @"\Rightarrow",
            [@"\iff"] = @"\Leftrightarrow"
        };

    [GeneratedRegex(@"\\[A-Za-z]+", RegexOptions.CultureInvariant)]
    private static partial Regex CommandRegex();

    [GeneratedRegex(@"\\mathbb\s*\{(?<content>[^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex BlackboardGroupRegex();

    [GeneratedRegex(@"\\mathbb\s*(?<content>[A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex BlackboardSingleRegex();

    [GeneratedRegex(@"\\(?:boldsymbol|bm)\s*\{(?<content>[^{}]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex BoldSymbolGroupRegex();

    [GeneratedRegex(@"\\(?:boldsymbol|bm)\s*(?<content>\\[A-Za-z]+|[A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex BoldSymbolSingleRegex();

    [GeneratedRegex(
        @"\\begin\s*\{(?<environment>bmatrix|pmatrix|Bmatrix|vmatrix|Vmatrix)\}(?<content>.*?)\\end\s*\{\k<environment>\}",
        RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DelimitedMatrixRegex();

    /// <summary>
    /// Returns compatibility candidates from closest visual approximation to
    /// the most conservative parser-safe fallback.
    /// </summary>
    public static IEnumerable<string> GetFallbacks(string latex)
    {
        if (string.IsNullOrEmpty(latex)) yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal) { latex };
        var aliases = NormalizeCommandAliases(latex);
        if (seen.Add(aliases)) yield return aliases;

        var bold = ReplaceMathAlphabets(aliases, "mathbf");
        if (seen.Add(bold)) yield return bold;

        var boldWithMatrices = NormalizeDelimitedMatrices(bold);
        if (seen.Add(boldWithMatrices)) yield return boldWithMatrices;

        // \mathrm is known to be a conservative fallback for Latin symbols.
        var roman = ReplaceMathAlphabets(aliases, "mathrm");
        if (seen.Add(roman)) yield return roman;

        var romanWithMatrices = NormalizeDelimitedMatrices(roman);
        if (seen.Add(romanWithMatrices)) yield return romanWithMatrices;

        // Final fallback keeps the mathematical content while dropping only
        // unsupported alphabet styling commands.
        var plain = RemoveUnsupportedAlphabetStyling(aliases);
        var plainWithMatrices = NormalizeDelimitedMatrices(plain);
        if (seen.Add(plainWithMatrices)) yield return plainWithMatrices;
    }

    private static string NormalizeCommandAliases(string latex)
        => CommandRegex().Replace(latex, static match =>
            CommandAliases.TryGetValue(match.Value, out var replacement)
                ? replacement
                : match.Value);

    private static string ReplaceMathAlphabets(string latex, string fallbackCommand)
    {
        var result = BlackboardGroupRegex().Replace(latex, match =>
            $@"\{fallbackCommand}{{{match.Groups["content"].Value}}}");
        result = BlackboardSingleRegex().Replace(result, match =>
            $@"\{fallbackCommand}{{{match.Groups["content"].Value}}}");
        result = BoldSymbolGroupRegex().Replace(result, match =>
            $@"\{fallbackCommand}{{{match.Groups["content"].Value}}}");
        return BoldSymbolSingleRegex().Replace(result, match =>
            $@"\{fallbackCommand}{{{match.Groups["content"].Value}}}");
    }

    private static string RemoveUnsupportedAlphabetStyling(string latex)
    {
        var result = BlackboardGroupRegex().Replace(latex, "${content}");
        result = BlackboardSingleRegex().Replace(result, "${content}");
        result = BoldSymbolGroupRegex().Replace(result, "${content}");
        return BoldSymbolSingleRegex().Replace(result, "${content}");
    }

    private static string NormalizeDelimitedMatrices(string latex)
        => DelimitedMatrixRegex().Replace(latex, static match =>
        {
            var content = match.Groups["content"].Value;
            var matrix = $@"\begin{{matrix}}{content}\end{{matrix}}";
            return match.Groups["environment"].Value switch
            {
                "pmatrix" => @"\left(" + matrix + @"\right)",
                "Bmatrix" => @"\left\{" + matrix + @"\right\}",
                "vmatrix" => @"\left|" + matrix + @"\right|",
                "Vmatrix" => @"\left\|" + matrix + @"\right\|",
                _ => @"\left[" + matrix + @"\right]"
            };
        });
}
