using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace AlphaNative.Services;

/// <summary>
/// Gives visible Markdown heading lines the same font sizes used by the native preview.
/// The editor remains plain text, so Markdown syntax characters stay editable.
/// </summary>
public sealed class MarkdownEditorTypographyTransformer : DocumentColorizingTransformer
{
    private static readonly FontFamily UiFont = new("Segoe UI, Microsoft YaHei UI");
    private static readonly Typeface HeadingTypeface = new(
        UiFont,
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private static readonly double[] HeadingSizes = { 0d, 31d, 25d, 21d, 18d, 16d, 15d };

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.IsDeleted || line.Length == 0) return;

        var text = CurrentContext.Document.GetText(line);
        var leadingWhitespace = text.Length - text.TrimStart().Length;
        var level = GetHeadingLevel(text.AsSpan(leadingWhitespace));
        if (level == 0) return;

        var startOffset = line.Offset + leadingWhitespace;
        var fontSize = HeadingSizes[level];
        ChangeLinePart(startOffset, line.EndOffset, element =>
        {
            element.TextRunProperties.SetTypeface(HeadingTypeface);
            element.TextRunProperties.SetFontRenderingEmSize(fontSize);
            element.TextRunProperties.SetFontHintingEmSize(fontSize);
        });
    }

    private static int GetHeadingLevel(ReadOnlySpan<char> text)
    {
        var level = 0;
        while (level < text.Length && level < 6 && text[level] == '#') level++;
        if (level == 0 || level >= text.Length || !char.IsWhiteSpace(text[level])) return 0;
        return level;
    }
}
