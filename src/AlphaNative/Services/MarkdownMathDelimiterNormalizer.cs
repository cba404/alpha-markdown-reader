using System.Text;

namespace AlphaNative.Services;

/// <summary>
/// Converts common TeX display delimiters to the delimiters understood by
/// Markdig's mathematics extension. The conversion is preview-only and keeps
/// the original line count, so navigation and synchronized scrolling continue
/// to use the source document's line numbers.
/// </summary>
internal static class MarkdownMathDelimiterNormalizer
{
    public static string NormalizeForPreview(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) ||
            (!markdown.Contains(@"\[", StringComparison.Ordinal) &&
             !markdown.Contains(@"\]", StringComparison.Ordinal)))
        {
            return markdown;
        }

        var output = new StringBuilder(markdown.Length);
        var position = 0;
        var inFence = false;
        var fenceCharacter = '\0';
        var fenceLength = 0;

        while (position < markdown.Length)
        {
            var newlineIndex = markdown.IndexOf('\n', position);
            var hasNewline = newlineIndex >= 0;
            var segmentEnd = hasNewline ? newlineIndex : markdown.Length;
            var rawLine = markdown.AsSpan(position, segmentEnd - position);
            var hasCarriageReturn = rawLine.Length > 0 && rawLine[^1] == '\r';
            var line = hasCarriageReturn ? rawLine[..^1] : rawLine;
            var lineText = line.ToString();
            var trimmed = lineText.Trim();

            if (TryReadFence(trimmed, out var currentFenceCharacter, out var currentFenceLength))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceCharacter = currentFenceCharacter;
                    fenceLength = currentFenceLength;
                }
                else if (currentFenceCharacter == fenceCharacter && currentFenceLength >= fenceLength)
                {
                    inFence = false;
                    fenceCharacter = '\0';
                    fenceLength = 0;
                }
            }

            if (!inFence && (trimmed == @"\[" || trimmed == @"\]"))
            {
                var leadingLength = lineText.Length - lineText.TrimStart().Length;
                output.Append(lineText.AsSpan(0, leadingLength));
                output.Append("$$");
            }
            else
            {
                output.Append(line);
            }

            if (hasCarriageReturn) output.Append('\r');
            if (hasNewline) output.Append('\n');
            position = hasNewline ? newlineIndex + 1 : markdown.Length;
        }

        return output.ToString();
    }

    private static bool TryReadFence(string trimmedLine, out char fenceCharacter, out int fenceLength)
    {
        fenceCharacter = '\0';
        fenceLength = 0;
        if (trimmedLine.Length < 3 || trimmedLine[0] is not ('`' or '~')) return false;

        fenceCharacter = trimmedLine[0];
        while (fenceLength < trimmedLine.Length && trimmedLine[fenceLength] == fenceCharacter)
        {
            fenceLength++;
        }
        return fenceLength >= 3;
    }
}
