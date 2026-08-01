using System.Text;

namespace AlphaNative.Services;

/// <summary>
/// Converts common TeX display and inline delimiters to the delimiters understood
/// by Markdig's mathematics extension. The conversion is preview-only and keeps
/// the original line count, so navigation and synchronized scrolling continue
/// to use the source document's line numbers.
/// </summary>
internal static class MarkdownMathDelimiterNormalizer
{
    public static string NormalizeForPreview(string markdown)
    {
        if (string.IsNullOrEmpty(markdown) ||
            (!markdown.Contains(@"\[", StringComparison.Ordinal) &&
             !markdown.Contains(@"\]", StringComparison.Ordinal) &&
             !markdown.Contains(@"\(", StringComparison.Ordinal) &&
             !markdown.Contains(@"\)", StringComparison.Ordinal)))
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

            var isFenceLine = TryReadFence(trimmed, out var currentFenceCharacter, out var currentFenceLength);
            if (isFenceLine)
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

            if (!inFence && !isFenceLine && (trimmed == @"\[" || trimmed == @"\]"))
            {
                var leadingLength = lineText.Length - lineText.TrimStart().Length;
                output.Append(lineText.AsSpan(0, leadingLength));
                output.Append("$$");
            }
            else if (!inFence && !isFenceLine)
            {
                output.Append(NormalizeInlineDelimiters(lineText));
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

    /// <summary>
    /// Converts paired \(...\) delimiters on a normal Markdown line to $...$.
    /// Inline code spans are copied verbatim, and unpaired delimiters are left
    /// unchanged so ordinary text cannot accidentally become a formula.
    /// </summary>
    private static string NormalizeInlineDelimiters(string line)
    {
        if (!line.Contains(@"\(", StringComparison.Ordinal) ||
            !line.Contains(@"\)", StringComparison.Ordinal))
        {
            return line;
        }

        StringBuilder? output = null;
        var copyStart = 0;
        var index = 0;

        while (index < line.Length)
        {
            if (line[index] == '`')
            {
                index = SkipCodeSpan(line, index);
                continue;
            }

            if (!IsDelimiterAt(line, index, '('))
            {
                index++;
                continue;
            }

            var closeIndex = FindInlineMathClose(line, index + 2);
            if (closeIndex < 0)
            {
                index += 2;
                continue;
            }

            output ??= new StringBuilder(line.Length);
            output.Append(line.AsSpan(copyStart, index - copyStart));
            output.Append('$');
            output.Append(line.AsSpan(index + 2, closeIndex - index - 2));
            output.Append('$');

            index = closeIndex + 2;
            copyStart = index;
        }

        if (output is null) return line;
        output.Append(line.AsSpan(copyStart));
        return output.ToString();
    }

    private static int FindInlineMathClose(string line, int startIndex)
    {
        var index = startIndex;
        while (index < line.Length)
        {
            if (line[index] == '`')
            {
                index = SkipCodeSpan(line, index);
                continue;
            }

            if (IsDelimiterAt(line, index, ')')) return index;
            index++;
        }
        return -1;
    }

    private static int SkipCodeSpan(string line, int openingIndex)
    {
        var tickCount = 1;
        while (openingIndex + tickCount < line.Length && line[openingIndex + tickCount] == '`')
        {
            tickCount++;
        }

        var searchIndex = openingIndex + tickCount;
        while (searchIndex < line.Length)
        {
            if (line[searchIndex] != '`')
            {
                searchIndex++;
                continue;
            }

            var closingCount = 1;
            while (searchIndex + closingCount < line.Length && line[searchIndex + closingCount] == '`')
            {
                closingCount++;
            }

            if (closingCount == tickCount) return searchIndex + closingCount;
            searchIndex += closingCount;
        }

        return openingIndex + tickCount;
    }

    private static bool IsDelimiterAt(string line, int index, char delimiter)
    {
        if (index < 0 || index + 1 >= line.Length ||
            line[index] != '\\' || line[index + 1] != delimiter)
        {
            return false;
        }

        var backslashCount = 1;
        for (var previous = index - 1; previous >= 0 && line[previous] == '\\'; previous--)
        {
            backslashCount++;
        }

        return backslashCount % 2 == 1;
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
