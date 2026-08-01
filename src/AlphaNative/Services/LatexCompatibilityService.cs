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

    public static string Normalize(string latex)
    {
        if (string.IsNullOrEmpty(latex)) return latex;

        return CommandRegex().Replace(latex, static match =>
            CommandAliases.TryGetValue(match.Value, out var replacement)
                ? replacement
                : match.Value);
    }
}
