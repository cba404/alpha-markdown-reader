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

    /// <summary>
    /// Returns compatibility candidates from closest visual approximation to
    /// the most conservative parser-safe fallback.
    /// </summary>
    public static IEnumerable<string> GetFallbacks(string latex)
    {
        if (string.IsNullOrEmpty(latex)) yield break;

        var aliases = NormalizeCommandAliases(latex);
        var bold = ReplaceBlackboardBold(aliases, "mathbf");
        if (!string.Equals(bold, latex, StringComparison.Ordinal))
        {
            yield return bold;
        }

        // \mathrm is known to be supported by the native parser and therefore
        // serves as a final visual fallback when \mathbf is unavailable.
        var roman = ReplaceBlackboardBold(aliases, "mathrm");
        if (!string.Equals(roman, latex, StringComparison.Ordinal) &&
            !string.Equals(roman, bold, StringComparison.Ordinal))
        {
            yield return roman;
        }
    }

    private static string NormalizeCommandAliases(string latex)
        => CommandRegex().Replace(latex, static match =>
            CommandAliases.TryGetValue(match.Value, out var replacement)
                ? replacement
                : match.Value);

    private static string ReplaceBlackboardBold(string latex, string fallbackCommand)
    {
        var grouped = BlackboardGroupRegex().Replace(latex, match =>
            $@"\{fallbackCommand}{{{match.Groups["content"].Value}}}");

        return BlackboardSingleRegex().Replace(grouped, match =>
            $@"\{fallbackCommand}{{{match.Groups["content"].Value}}}");
    }
}
