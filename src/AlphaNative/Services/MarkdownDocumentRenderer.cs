using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using AlphaNative.Models;
using WpfMath.Controls;
using WpfMath.Parsers;
using XamlMath.Exceptions;
using MdBlock = Markdig.Syntax.Block;
using MdTable = Markdig.Extensions.Tables.Table;
using WpfBlock = System.Windows.Documents.Block;
using WpfList = System.Windows.Documents.List;
using WpfTable = System.Windows.Documents.Table;

namespace AlphaNative.Services;

public sealed class MarkdownDocumentRenderer
{
    private const int MaxImageCacheEntries = 24;
    private const int MaxFormulaCacheEntries = 160;
    private static readonly FontFamily UiFont = new("Segoe UI, Microsoft YaHei UI");
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");

    private readonly MarkdownPipeline _pipeline;
    private readonly SortedDictionary<int, WpfBlock> _anchorBlocks = new();
    private readonly Dictionary<string, BitmapSource> _imageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _imageCacheOrder = new();
    private readonly Dictionary<string, string?> _formulaValidationCache = new(StringComparer.Ordinal);
    private RendererTheme _theme = RendererTheme.Light;
    private string? _baseDirectory;
    private bool _forPrint;
    private int _formulaErrors;
    private int _codeBlocks;

    public MarkdownDocumentRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseMathematics()
            .Build();
    }

    public RenderResult Render(string markdown, string? filePath, RendererTheme theme, bool forPrint = false)
    {
        _anchorBlocks.Clear();
        _theme = theme;
        _forPrint = forPrint;
        _formulaErrors = 0;
        _codeBlocks = 0;
        _baseDirectory = string.IsNullOrWhiteSpace(filePath) ? null : Path.GetDirectoryName(filePath);

        var flow = new FlowDocument
        {
            FontFamily = UiFont,
            FontSize = 16,
            Foreground = theme.Foreground,
            Background = theme.PanelBackground,
            PagePadding = new Thickness(forPrint ? 48 : 32, forPrint ? 44 : 26, forPrint ? 48 : 32, forPrint ? 56 : 80),
            LineHeight = double.NaN,
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity
        };

        var document = Markdig.Markdown.Parse(markdown ?? string.Empty, _pipeline);
        foreach (var block in document)
        {
            RenderBlock(block, flow.Blocks);
        }

        if (flow.Blocks.Count == 0)
        {
            flow.Blocks.Add(new Paragraph(new Run("开始输入 Markdown……") { Foreground = theme.Muted })
            {
                Margin = new Thickness(0, 20, 0, 0)
            });
        }

        var anchors = new SortedDictionary<int, TextPointer>();
        foreach (var (line, renderedBlock) in _anchorBlocks)
        {
            anchors[line] = renderedBlock.ContentStart;
        }

        return new RenderResult(flow, anchors, _formulaErrors, _codeBlocks);
    }

    private void RenderBlock(MdBlock block, BlockCollection target)
    {
        switch (block)
        {
            case HeadingBlock heading:
                AddBlock(target, RenderHeading(heading), SourceLine(block));
                break;
            case ParagraphBlock paragraph:
                AddBlock(target, RenderParagraph(paragraph), SourceLine(block));
                break;
            case QuoteBlock quote:
                AddBlock(target, RenderQuote(quote), SourceLine(block));
                break;
            case ListBlock list:
                AddBlock(target, RenderList(list), SourceLine(block));
                break;
            case MdTable table:
                AddBlock(target, RenderTable(table), SourceLine(block));
                break;
            case MathBlock math:
                AddBlock(target, RenderMathBlock(math), SourceLine(block));
                break;
            case FencedCodeBlock fenced:
                AddBlock(target, RenderCodeBlock(fenced.Lines.ToString(), fenced.Info?.ToString() ?? string.Empty), SourceLine(block));
                break;
            case CodeBlock code:
                AddBlock(target, RenderCodeBlock(code.Lines.ToString(), string.Empty), SourceLine(block));
                break;
            case ThematicBreakBlock:
                AddBlock(target, RenderRule(), SourceLine(block));
                break;
            case HtmlBlock html:
                AddBlock(target, RenderRawText(html.Lines.ToString()), SourceLine(block));
                break;
            case ContainerBlock container:
            {
                var section = new Section { Margin = new Thickness(0) };
                foreach (var child in container)
                {
                    RenderBlock(child, section.Blocks);
                }
                AddBlock(target, section, SourceLine(block));
                break;
            }
            case LeafBlock leaf:
                AddBlock(target, RenderRawText(leaf.Lines.ToString()), SourceLine(block));
                break;
        }
    }

    private Paragraph RenderHeading(HeadingBlock heading)
    {
        var sizes = new[] { 0d, 31d, 25d, 21d, 18d, 16d, 15d };
        var paragraph = new Paragraph
        {
            FontSize = sizes[Math.Clamp(heading.Level, 1, 6)],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, heading.Level <= 2 ? 22 : 16, 0, 8),
            KeepWithNext = true
        };
        AppendContainer(heading.Inline, paragraph.Inlines);
        if (heading.Level <= 2)
        {
            paragraph.BorderBrush = _theme.Border;
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.Padding = new Thickness(0, 0, 0, 7);
        }
        return paragraph;
    }

    private Paragraph RenderParagraph(ParagraphBlock paragraphBlock)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 4, 0, 9),
            LineHeight = 27
        };
        AppendContainer(paragraphBlock.Inline, paragraph.Inlines);
        return paragraph;
    }

    private Section RenderQuote(QuoteBlock quote)
    {
        var section = new Section
        {
            Margin = new Thickness(0, 10, 0, 12),
            Padding = new Thickness(14, 8, 14, 8),
            BorderBrush = _theme.Accent,
            BorderThickness = new Thickness(4, 0, 0, 0),
            Background = _theme.AccentSoft,
            Foreground = _theme.Muted
        };
        foreach (var child in quote)
        {
            RenderBlock(child, section.Blocks);
        }
        return section;
    }

    private WpfList RenderList(ListBlock listBlock)
    {
        var list = new WpfList
        {
            MarkerStyle = listBlock.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(18, 5, 0, 10),
            Padding = new Thickness(6, 0, 0, 0)
        };

        foreach (var itemBlock in listBlock.OfType<ListItemBlock>())
        {
            var item = new ListItem { Margin = new Thickness(0, 2, 0, 2) };
            foreach (var child in itemBlock)
            {
                RenderBlock(child, item.Blocks);
            }
            list.ListItems.Add(item);
        }
        return list;
    }

    private WpfTable RenderTable(MdTable table)
    {
        var result = new WpfTable
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 10, 0, 14),
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1)
        };

        var rows = table.OfType<Markdig.Extensions.Tables.TableRow>().ToList();
        var columnCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count());
        for (var i = 0; i < columnCount; i++)
        {
            result.Columns.Add(new TableColumn());
        }

        var rowGroup = new TableRowGroup();
        result.RowGroups.Add(rowGroup);
        foreach (var sourceRow in rows)
        {
            var row = new System.Windows.Documents.TableRow
            {
                Background = sourceRow.IsHeader ? _theme.CodeBackground : Brushes.Transparent,
                FontWeight = sourceRow.IsHeader ? FontWeights.SemiBold : FontWeights.Normal
            };
            rowGroup.Rows.Add(row);

            foreach (var sourceCell in sourceRow.OfType<Markdig.Extensions.Tables.TableCell>())
            {
                var cell = new System.Windows.Documents.TableCell
                {
                    Padding = new Thickness(9, 6, 9, 6),
                    BorderBrush = _theme.Border,
                    BorderThickness = new Thickness(0, 0, 1, 1)
                };
                foreach (var child in sourceCell)
                {
                    RenderBlock(child, cell.Blocks);
                }
                if (cell.Blocks.Count == 0)
                {
                    cell.Blocks.Add(new Paragraph());
                }
                row.Cells.Add(cell);
            }
        }
        return result;
    }

    private WpfBlock RenderMathBlock(MathBlock math)
    {
        var latex = math.Lines.ToString().Trim();
        var validationError = ValidateFormula(latex);
        if (validationError is not null)
        {
            _formulaErrors++;
            return ErrorBlock($"公式错误：{validationError}\n{latex}");
        }

        try
        {
            var formula = new FormulaControl
            {
                Formula = latex,
                FontSize = 21,
                Foreground = _theme.Foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(8)
            };
            return new BlockUIContainer(new Border
            {
                Child = formula,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 8, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Stretch
            });
        }
        catch (Exception ex)
        {
            _formulaErrors++;
            return ErrorBlock($"公式错误：{ex.Message}\n{latex}");
        }
    }

    private WpfBlock RenderCodeBlock(string code, string language)
    {
        _codeBlocks++;
        var normalized = SyntaxHighlighter.NormalizeLanguage(language.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault());
        var codeText = new TextBlock
        {
            FontFamily = CodeFont,
            FontSize = 13.5,
            TextWrapping = _forPrint ? TextWrapping.Wrap : TextWrapping.NoWrap,
            Padding = new Thickness(14, 12, 14, 14)
        };
        SyntaxHighlighter.Highlight(code.TrimEnd('\r', '\n'), normalized, codeText.Inlines, _theme);

        var header = new DockPanel
        {
            LastChildFill = true,
            Background = _theme.PanelBackground,
            Margin = new Thickness(0)
        };
        header.Children.Add(new TextBlock
        {
            Text = SyntaxHighlighter.DisplayName(normalized),
            Foreground = _theme.Muted,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Margin = new Thickness(11, 7, 8, 7)
        });

        if (!_forPrint)
        {
            var copy = new Button
            {
                Content = "复制代码",
                FontSize = 11,
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Right,
                Tag = code
            };
            DockPanel.SetDock(copy, Dock.Right);
            copy.Click += (_, _) => Clipboard.SetText(code);
            header.Children.Insert(0, copy);
        }

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_forPrint
            ? codeText
            : new ScrollViewer
            {
                Content = codeText,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
            });

        return new BlockUIContainer(new Border
        {
            Child = stack,
            Background = _theme.CodeBackground,
            BorderBrush = _theme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 9, 0, 13),
            ClipToBounds = true
        });
    }

    private WpfBlock RenderRule()
    {
        return new BlockUIContainer(new Border
        {
            Height = 1,
            Background = _theme.Border,
            Margin = new Thickness(0, 18, 0, 18)
        });
    }

    private Paragraph RenderRawText(string text)
    {
        return new Paragraph(new Run(text)
        {
            FontFamily = CodeFont,
            Foreground = _theme.Muted
        })
        {
            Margin = new Thickness(0, 5, 0, 9)
        };
    }

    private WpfBlock ErrorBlock(string message)
    {
        return new BlockUIContainer(new Border
        {
            BorderBrush = Brushes.IndianRed,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(28, 205, 64, 64)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 10),
            Child = new TextBlock
            {
                Text = message,
                Foreground = Brushes.IndianRed,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = CodeFont
            }
        });
    }

    private void AppendContainer(ContainerInline? container, InlineCollection target)
    {
        if (container is null) return;
        var current = container.FirstChild;
        while (current is not null)
        {
            AppendInline(current, target);
            current = current.NextSibling;
        }
    }

    private void AppendInline(Markdig.Syntax.Inlines.Inline inline, InlineCollection target)
    {
        switch (inline)
        {
            case LiteralInline literal:
                target.Add(new Run(literal.Content.ToString()));
                break;
            case CodeInline code:
                target.Add(new Run(code.Content)
                {
                    FontFamily = CodeFont,
                    Background = _theme.CodeBackground,
                    Foreground = _theme.Foreground
                });
                break;
            case MathInline math:
                AppendInlineMath(math.Content.ToString(), target);
                break;
            case TaskList task:
                target.Add(new InlineUIContainer(new CheckBox
                {
                    IsChecked = task.Checked,
                    IsHitTestVisible = false,
                    Focusable = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                })
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
                break;
            case LineBreakInline lineBreak:
                target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                break;
            case EmphasisInline emphasis:
            {
                var span = new Span();
                if (emphasis.DelimiterChar == '~')
                {
                    span.TextDecorations = TextDecorations.Strikethrough;
                }
                else if (emphasis.DelimiterCount >= 2)
                {
                    span.FontWeight = FontWeights.Bold;
                }
                else
                {
                    span.FontStyle = FontStyles.Italic;
                }
                target.Add(span);
                AppendContainer(emphasis, span.Inlines);
                break;
            }
            case LinkInline link when link.IsImage:
                AppendImage(link, target);
                break;
            case LinkInline link:
            {
                var hyperlink = new Hyperlink { Foreground = _theme.Accent };
                AppendContainer(link, hyperlink.Inlines);
                var url = link.Url ?? string.Empty;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    hyperlink.NavigateUri = uri;
                    hyperlink.RequestNavigate += (_, args) =>
                    {
                        try { Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true }); }
                        catch { /* External launch failure is non-fatal. */ }
                        args.Handled = true;
                    };
                }
                target.Add(hyperlink);
                break;
            }
            case HtmlInline html:
                target.Add(new Run(html.Tag) { Foreground = _theme.Muted });
                break;
            case ContainerInline container:
            {
                var span = new Span();
                target.Add(span);
                AppendContainer(container, span.Inlines);
                break;
            }
        }
    }

    private void AppendInlineMath(string latex, InlineCollection target)
    {
        var validationError = ValidateFormula(latex);
        if (validationError is not null)
        {
            _formulaErrors++;
            target.Add(new Run($"[公式错误：{validationError}]")
            {
                Foreground = Brushes.IndianRed,
                FontFamily = CodeFont
            });
            return;
        }

        try
        {
            var control = new FormulaControl
            {
                Formula = latex,
                FontSize = 17,
                Foreground = _theme.Foreground,
                Margin = new Thickness(2, 0, 2, 0)
            };
            target.Add(new InlineUIContainer(control)
            {
                BaselineAlignment = BaselineAlignment.Center
            });
        }
        catch (Exception ex)
        {
            _formulaErrors++;
            target.Add(new Run($"[公式错误：{ex.Message}]")
            {
                Foreground = Brushes.IndianRed,
                FontFamily = CodeFont
            });
        }
    }

    private string? ValidateFormula(string latex)
    {
        if (_formulaValidationCache.TryGetValue(latex, out var cached)) return cached;

        string? error = null;
        try
        {
            WpfTeXFormulaParser.Instance.Parse(latex);
        }
        catch (TexException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (_formulaValidationCache.Count >= MaxFormulaCacheEntries)
        {
            _formulaValidationCache.Clear();
        }
        _formulaValidationCache[latex] = error;
        return error;
    }

    private void AppendImage(LinkInline link, InlineCollection target)
    {
        var alt = PlainText(link);
        var url = link.Url ?? string.Empty;
        try
        {
            Uri uri;
            if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
            {
                uri = absolute;
            }
            else if (_baseDirectory is not null)
            {
                uri = new Uri(Path.GetFullPath(Path.Combine(_baseDirectory, url)));
            }
            else
            {
                target.Add(new Run($"[图片：{alt}]") { Foreground = _theme.Muted });
                return;
            }

            var bitmap = GetCachedBitmap(uri);

            target.Add(new InlineUIContainer(new Image
            {
                Source = bitmap,
                MaxWidth = 760,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(3)
            }));
        }
        catch
        {
            target.Add(new Run($"[图片无法加载：{alt}]") { Foreground = _theme.Muted });
        }
    }

    private BitmapSource GetCachedBitmap(Uri uri)
    {
        var key = uri.IsFile && File.Exists(uri.LocalPath)
            ? $"{uri.AbsoluteUri}|{File.GetLastWriteTimeUtc(uri.LocalPath).Ticks}"
            : uri.AbsoluteUri;
        if (_imageCache.TryGetValue(key, out var cached)) return cached;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = uri;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.EndInit();
        bitmap.Freeze();

        _imageCache[key] = bitmap;
        _imageCacheOrder.Enqueue(key);
        while (_imageCacheOrder.Count > MaxImageCacheEntries)
        {
            _imageCache.Remove(_imageCacheOrder.Dequeue());
        }
        return bitmap;
    }

    private static string PlainText(ContainerInline container)
    {
        var parts = new List<string>();
        var current = container.FirstChild;
        while (current is not null)
        {
            if (current is LiteralInline literal) parts.Add(literal.Content.ToString());
            else if (current is CodeInline code) parts.Add(code.Content);
            else if (current is ContainerInline nested) parts.Add(PlainText(nested));
            current = current.NextSibling;
        }
        return string.Concat(parts);
    }

    private void AddBlock(BlockCollection target, WpfBlock block, int sourceLine)
    {
        target.Add(block);
        if (!_anchorBlocks.ContainsKey(sourceLine))
        {
            _anchorBlocks[sourceLine] = block;
        }
    }

    private static int SourceLine(MdBlock block) => Math.Max(1, block.Line + 1);
}
