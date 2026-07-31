using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using AlphaNative.Models;
using AlphaNative.Services;

namespace AlphaNative;

public partial class MainWindow : Window
{
    private const string ProductName = "α";
    private readonly MarkdownDocumentRenderer _renderer = new();
    private readonly AppStateService _stateService = new();
    private readonly DispatcherTimer _previewTimer;
    private readonly DispatcherTimer _stateTimer;
    private readonly SearchPanel _searchPanel;
    private readonly string? _startupPath;

    private AppState _state = new();
    private SortedDictionary<int, TextPointer> _sourceAnchors = new();
    private int[] _anchorLines = Array.Empty<int>();
    private ScrollViewer? _previewScroll;
    private string? _currentPath;
    private bool _dirty;
    private bool _darkMode;
    private bool _suppressTextChanged;
    private bool _closingAccepted;
    private bool _scrollSyncQueued;
    private bool _previewUserOverride;
    private bool _previewAnimating;
    private double _previewTargetOffset;
    private string _viewMode = "split";

    private static readonly string SampleMarkdown = """
# α 原生 Markdown 编辑器

这是一个使用 **C#、WPF、Markdig、AvalonEdit 和 WPF-Math** 编写的原生 Windows 程序。

## 常用 Markdown

- 标题、段落、引用、链接和图片
- ~~删除线~~、表格和任务列表
- [x] 已完成的任务
- [ ] 待完成的任务

> 左侧编辑 Markdown，右侧由 WPF `FlowDocument` 原生渲染，不使用浏览器或 HTML 页面。

| 功能 | 状态 |
| --- | :---: |
| 原生 Markdown 预览 | ✅ |
| LaTeX 数学公式 | ✅ |
| 代码语法高亮 | ✅ |
| 双栏同步滚动 | ✅ |
| HTML 单文件导出 | ✅ |
| Windows PDF 输出 | ✅ |

## LaTeX

行内公式：$E=mc^2$，以及 $e^{i\pi}+1=0$。

$$
\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}
$$

$$
\begin{bmatrix}
1 & 2 \\
3 & 4
\end{bmatrix}
$$

## 代码高亮

```javascript
// JavaScript 代码块
function greet(name) {
  const message = `你好，${name}！`;
  console.log(message);
  return true;
}

greet("Markdown");
```

```python
# Python 代码块
def fibonacci(n: int) -> list[int]:
    values = [0, 1]
    while len(values) < n:
        values.append(values[-1] + values[-2])
    return values
```

快捷键：**Ctrl+S** 保存、**Ctrl+B** 粗体、**Ctrl+I** 斜体、**Ctrl+F** 查找、**Ctrl+Shift+H** 导出 HTML。
""";

    public MainWindow(string? startupPath = null)
    {
        _startupPath = startupPath;
        InitializeComponent();

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };

        _stateTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _stateTimer.Tick += (_, _) => SaveAppState();
        _stateTimer.Start();

        _searchPanel = SearchPanel.Install(Editor.TextArea);
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.EnableHyperlinks = false;
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => EditorScrolled();

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        CompositionTarget.Rendering += CompositionTarget_Rendering;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadEditorHighlighting();
        _state = _stateService.Load();
        Width = Math.Max(MinWidth, _state.WindowWidth);
        Height = Math.Max(MinHeight, _state.WindowHeight);
        _darkMode = _state.DarkMode;
        SyncScrollCheck.IsChecked = _state.SyncScroll;
        ApplyTheme(_darkMode, refresh: false);

        var candidate = _startupPath;
        if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(_state.LastFile) && File.Exists(_state.LastFile))
        {
            candidate = _state.LastFile;
        }

        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            LoadFile(candidate);
        }
        else if (!string.IsNullOrWhiteSpace(_state.RecoveryText))
        {
            SetEditorText(_state.RecoveryText, dirty: true);
            SetStatus("已恢复上次未保存内容");
        }
        else
        {
            SetEditorText(SampleMarkdown, dirty: false);
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _previewScroll = FindVisualChild<ScrollViewer>(PreviewViewer);
            QueueScrollSync();
        }));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingAccepted)
        {
            SaveAppState();
            return;
        }

        if (!ConfirmSaveIfDirty())
        {
            e.Cancel = true;
            return;
        }

        _closingAccepted = true;
        SaveAppState();
        CompositionTarget.Rendering -= CompositionTarget_Rendering;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl) return;

        switch (e.Key)
        {
            case Key.S when shift:
                SaveAsDocument();
                e.Handled = true;
                break;
            case Key.S:
                SaveDocument();
                e.Handled = true;
                break;
            case Key.O:
                OpenDocument();
                e.Handled = true;
                break;
            case Key.N:
                NewDocument();
                e.Handled = true;
                break;
            case Key.B:
                WrapSelection("**", "**", "粗体文本");
                e.Handled = true;
                break;
            case Key.I:
                WrapSelection("*", "*", "斜体文本");
                e.Handled = true;
                break;
            case Key.F:
                _searchPanel.Open();
                e.Handled = true;
                break;
            case Key.H when shift:
                ExportHtml();
                e.Handled = true;
                break;
            case Key.P when shift:
                ExportPdf();
                e.Handled = true;
                break;
        }
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (_suppressTextChanged) return;
        _dirty = true;
        UpdateTitle();
        UpdateStatistics();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RefreshPreview()
    {
        try
        {
            var result = _renderer.Render(Editor.Text, _currentPath, CurrentTheme());
            PreviewViewer.Document = result.Document;
            _sourceAnchors = result.SourceAnchors;
            _anchorLines = _sourceAnchors.Keys.ToArray();
            CodeStatusText.Text = $"代码 {result.CodeBlocks}";
            FormulaStatusText.Text = result.FormulaErrors == 0 ? "公式正常" : $"公式错误 {result.FormulaErrors}";
            FormulaStatusText.Foreground = result.FormulaErrors == 0
                ? (Brush)FindResource("MutedBrush")
                : Brushes.IndianRed;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _previewScroll ??= FindVisualChild<ScrollViewer>(PreviewViewer);
                QueueScrollSync();
            }));
        }
        catch (Exception ex)
        {
            var error = new FlowDocument(new Paragraph(new Run($"预览失败：{ex.Message}") { Foreground = Brushes.IndianRed }))
            {
                PagePadding = new Thickness(30),
                Background = CurrentTheme().PanelBackground,
                Foreground = CurrentTheme().Foreground
            };
            PreviewViewer.Document = error;
            SetStatus($"预览失败：{ex.Message}");
        }
    }

    private void EditorScrolled()
    {
        _previewUserOverride = false;
        QueueScrollSync();
    }

    private void QueueScrollSync()
    {
        if (SyncScrollCheck.IsChecked != true || _viewMode != "split" || _scrollSyncQueued) return;
        _scrollSyncQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _scrollSyncQueued = false;
            SyncPreviewToEditor();
        }));
    }

    private void SyncPreviewToEditor()
    {
        if (_previewUserOverride || _previewScroll is null || _anchorLines.Length == 0) return;

        int topLine;
        try
        {
            Editor.TextArea.TextView.EnsureVisualLines();
            topLine = Editor.TextArea.TextView.VisualLines.FirstOrDefault()?.FirstDocumentLine.LineNumber
                ?? Editor.Document.GetLineByOffset(Editor.CaretOffset).LineNumber;
        }
        catch
        {
            topLine = Editor.Document.GetLineByOffset(Editor.CaretOffset).LineNumber;
        }

        var index = Array.BinarySearch(_anchorLines, topLine);
        if (index < 0) index = ~index - 1;
        if (index < 0) index = 0;
        var anchorLine = _anchorLines[index];
        var pointer = _sourceAnchors[anchorLine];
        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty) return;

        var target = _previewScroll.VerticalOffset + rect.Top - 8;
        _previewTargetOffset = Math.Clamp(target, 0, _previewScroll.ScrollableHeight);
        _previewAnimating = true;
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (!_previewAnimating || _previewScroll is null) return;
        var current = _previewScroll.VerticalOffset;
        var delta = _previewTargetOffset - current;
        if (Math.Abs(delta) < 0.65)
        {
            _previewScroll.ScrollToVerticalOffset(_previewTargetOffset);
            _previewAnimating = false;
            return;
        }

        var factor = Math.Abs(delta) > _previewScroll.ViewportHeight ? 0.42 : 0.28;
        _previewScroll.ScrollToVerticalOffset(current + delta * factor);
    }

    private void PreviewViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _previewUserOverride = true;
        _previewAnimating = false;
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewDocument();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenDocument();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAsDocument();
    private void ExportHtml_Click(object sender, RoutedEventArgs e) => ExportHtml();
    private void ExportPdf_Click(object sender, RoutedEventArgs e) => ExportPdf();

    private void NewDocument()
    {
        if (!ConfirmSaveIfDirty()) return;
        _currentPath = null;
        SetEditorText(string.Empty, dirty: false);
        Editor.Focus();
        SetStatus("已新建文档");
    }

    private void OpenDocument()
    {
        if (!ConfirmSaveIfDirty()) return;
        var dialog = new OpenFileDialog
        {
            Title = "打开 Markdown 文件",
            Filter = "Markdown 文件 (*.md;*.markdown)|*.md;*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) LoadFile(dialog.FileName);
    }

    private void LoadFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            _currentPath = Path.GetFullPath(path);
            SetEditorText(text.TrimStart('\uFEFF'), dirty: false);
            SetStatus($"已打开 {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveDocument()
    {
        if (string.IsNullOrWhiteSpace(_currentPath)) return SaveAsDocument();
        try
        {
            File.WriteAllText(_currentPath, Editor.Text, new UTF8Encoding(false));
            _dirty = false;
            UpdateTitle();
            SetStatus($"已保存 {Path.GetFileName(_currentPath)}");
            SaveAppState();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool SaveAsDocument()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存 Markdown 文件",
            Filter = "Markdown 文件 (*.md)|*.md|Markdown 文件 (*.markdown)|*.markdown|文本文件 (*.txt)|*.txt",
            DefaultExt = ".md",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_currentPath) ? "未命名.md" : Path.GetFileName(_currentPath)
        };
        if (dialog.ShowDialog(this) != true) return false;
        _currentPath = dialog.FileName;
        return SaveDocument();
    }

    private void ExportHtml()
    {
        var title = string.IsNullOrWhiteSpace(_currentPath)
            ? "α 文档"
            : Path.GetFileNameWithoutExtension(_currentPath);
        var dialog = new SaveFileDialog
        {
            Title = "导出为 HTML",
            Filter = "HTML 网页 (*.html)|*.html|HTML 网页 (*.htm)|*.htm",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = title + ".html",
            InitialDirectory = string.IsNullOrWhiteSpace(_currentPath)
                ? null
                : Path.GetDirectoryName(_currentPath)
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var html = HtmlExportService.Create(Editor.Text, _currentPath, title, _darkMode);
            File.WriteAllText(dialog.FileName, html, new UTF8Encoding(false));
            SetStatus($"已导出 HTML：{Path.GetFileName(dialog.FileName)}");

            var result = MessageBox.Show(this,
                "HTML 已成功导出。是否立即使用默认浏览器打开？",
                "导出 HTML",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出 HTML 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportPdf()
    {
        try
        {
            var printable = _renderer.Render(Editor.Text, _currentPath, RendererTheme.Light, forPrint: true).Document;
            var title = string.IsNullOrWhiteSpace(_currentPath) ? "α 文档" : Path.GetFileNameWithoutExtension(_currentPath);
            if (PdfPrintService.PrintToPdf(printable, title, this)) SetStatus("PDF 打印任务已提交");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出 PDF 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ConfirmSaveIfDirty()
    {
        if (!_dirty) return true;
        var result = MessageBox.Show(this, "当前文档尚未保存，是否先保存？", ProductName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void SetEditorText(string text, bool dirty)
    {
        _suppressTextChanged = true;
        try
        {
            Editor.Text = text;
            Editor.CaretOffset = 0;
            Editor.ScrollToHome();
        }
        finally
        {
            _suppressTextChanged = false;
        }
        _dirty = dirty;
        UpdateTitle();
        UpdateStatistics();
        RefreshPreview();
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action }) return;
        switch (action)
        {
            case "h1": PrefixSelectedLines("# "); break;
            case "h2": PrefixSelectedLines("## "); break;
            case "h3": PrefixSelectedLines("### "); break;
            case "bold": WrapSelection("**", "**", "粗体文本"); break;
            case "italic": WrapSelection("*", "*", "斜体文本"); break;
            case "strike": WrapSelection("~~", "~~", "删除文本"); break;
            case "inlineCode": WrapSelection("`", "`", "code"); break;
            case "quote": PrefixSelectedLines("> "); break;
            case "ul": PrefixSelectedLines("- "); break;
            case "ol": PrefixSelectedLines(string.Empty, ordered: true); break;
            case "task": PrefixSelectedLines("- [ ] "); break;
            case "link": WrapSelection("[", "](https://example.com)", "链接文字"); break;
            case "image": WrapSelection("![", "](image.png)", "图片说明"); break;
            case "code": WrapSelection("```text\n", "\n```", "代码"); break;
            case "inlineMath": WrapSelection("$", "$", "E=mc^2"); break;
            case "blockMath": WrapSelection("$$\n", "\n$$", "\\int_0^1 x^2\\,dx"); break;
            case "table": InsertAtCaret("| 列一 | 列二 |\n| --- | --- |\n| 内容 | 内容 |\n"); break;
        }
        Editor.Focus();
    }

    private void WrapSelection(string before, string after, string placeholder)
    {
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        var selected = length > 0 ? Editor.SelectedText : placeholder;
        var replacement = before + selected + after;
        Editor.Document.Replace(start, length, replacement);
        Editor.Select(start + before.Length, selected.Length);
    }

    private void PrefixSelectedLines(string prefix, bool ordered = false)
    {
        var startOffset = Editor.SelectionStart;
        var endOffset = Math.Min(Editor.Document.TextLength, Editor.SelectionStart + Math.Max(0, Editor.SelectionLength));
        var startLine = Editor.Document.GetLineByOffset(startOffset);
        var endLine = Editor.Document.GetLineByOffset(endOffset);
        if (endOffset == endLine.Offset && endLine.LineNumber > startLine.LineNumber)
        {
            endLine = Editor.Document.GetLineByNumber(endLine.LineNumber - 1);
        }

        var blockStart = startLine.Offset;
        var blockEnd = endLine.EndOffset;
        var source = Editor.Document.GetText(blockStart, blockEnd - blockStart);
        var lines = Regex.Split(source, "\\r?\\n");
        var transformed = string.Join(Environment.NewLine, lines.Select((line, index) =>
            (ordered ? $"{index + 1}. " : prefix) + line));
        Editor.Document.Replace(blockStart, blockEnd - blockStart, transformed);
        Editor.Select(blockStart, transformed.Length);
    }

    private void InsertAtCaret(string text)
    {
        var start = Editor.SelectionStart;
        var length = Editor.SelectionLength;
        Editor.Document.Replace(start, length, text);
        Editor.CaretOffset = Math.Min(Editor.Document.TextLength, start + text.Length);
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        Editor.Focus();
        _searchPanel.Open();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ApplyTheme(!_darkMode, refresh: true);
    }

    private void ApplyTheme(bool dark, bool refresh)
    {
        _darkMode = dark;
        SetBrush("AppBackground", dark ? "#0F141D" : "#F4F6F8");
        SetBrush("PanelBackground", dark ? "#171D28" : "#FFFFFF");
        SetBrush("PanelAltBackground", dark ? "#111722" : "#F8FAFC");
        SetBrush("TextBrush", dark ? "#E9EDF5" : "#172033");
        SetBrush("MutedBrush", dark ? "#9AA4B5" : "#667085");
        SetBrush("BorderBrush", dark ? "#2B3443" : "#DCE2EA");
        SetBrush("AccentBrush", dark ? "#8AA4FF" : "#3659E3");
        SetBrush("AccentSoftBrush", dark ? "#242F54" : "#E9EDFF");
        SetBrush("CodeBackground", dark ? "#111722" : "#F6F8FA");
        ThemeButton.Content = dark ? "浅色" : "深色";
        Editor.Background = (Brush)FindResource("PanelBackground");
        Editor.Foreground = (Brush)FindResource("TextBrush");
        if (refresh) RefreshPreview();
    }

    private void SetBrush(string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }

    private RendererTheme CurrentTheme() => _darkMode ? RendererTheme.Dark : RendererTheme.Light;

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode }) return;
        SetViewMode(mode);
    }

    private void SetViewMode(string mode)
    {
        _viewMode = mode;
        switch (mode)
        {
            case "editor":
                EditorPane.Visibility = Visibility.Visible;
                PreviewPane.Visibility = Visibility.Collapsed;
                MainSplitter.Visibility = Visibility.Collapsed;
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(0);
                break;
            case "preview":
                EditorPane.Visibility = Visibility.Collapsed;
                PreviewPane.Visibility = Visibility.Visible;
                MainSplitter.Visibility = Visibility.Collapsed;
                EditorColumn.Width = new GridLength(0);
                SplitterColumn.Width = new GridLength(0);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                break;
            default:
                _viewMode = "split";
                EditorPane.Visibility = Visibility.Visible;
                PreviewPane.Visibility = Visibility.Visible;
                MainSplitter.Visibility = Visibility.Visible;
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(5);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                QueueScrollSync();
                break;
        }
        UpdateViewButtons();
    }

    private void UpdateViewButtons()
    {
        var normal = (Style)FindResource(typeof(Button));
        var primary = (Style)FindResource("PrimaryButton");
        EditorViewButton.Style = _viewMode == "editor" ? primary : normal;
        SplitViewButton.Style = _viewMode == "split" ? primary : normal;
        PreviewViewButton.Style = _viewMode == "preview" ? primary : normal;
    }

    private void SyncScroll_Changed(object sender, RoutedEventArgs e)
    {
        if (SyncScrollCheck.IsChecked == true)
        {
            _previewUserOverride = false;
            QueueScrollSync();
        }
        else
        {
            _previewAnimating = false;
        }
    }

    private void UpdateStatistics()
    {
        var text = Editor.Text ?? string.Empty;
        var lines = Math.Max(1, text.Count(ch => ch == '\n') + 1);
        var latinWords = Regex.Matches(text, @"[A-Za-z0-9]+(?:['’-][A-Za-z0-9]+)*").Count;
        var cjk = Regex.Matches(text, @"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]").Count;
        LineCountText.Text = $"{lines} 行";
        WordCountText.Text = $"{latinWords + cjk} 字词";
        CharacterCountText.Text = $"{text.Length} 字符";
    }

    private void UpdateTitle()
    {
        var fileName = string.IsNullOrWhiteSpace(_currentPath) ? "未命名.md" : Path.GetFileName(_currentPath);
        FileNameText.Text = fileName;
        Title = $"{(_dirty ? "● " : string.Empty)}{fileName} · {ProductName}";
    }

    private void SetStatus(string message)
    {
        StatusText.Text = message;
    }

    private void SaveAppState()
    {
        var state = new AppState
        {
            LastFile = _currentPath,
            RecoveryText = _dirty ? Editor.Text : null,
            DarkMode = _darkMode,
            SyncScroll = SyncScrollCheck.IsChecked == true,
            WindowWidth = ActualWidth > 0 ? ActualWidth : Width,
            WindowHeight = ActualHeight > 0 ? ActualHeight : Height
        };
        _stateService.Save(state);
    }

    private void LoadEditorHighlighting()
    {
        try
        {
            var uri = new Uri("Resources/Markdown.xshd", UriKind.Relative);
            var info = Application.GetResourceStream(uri);
            if (info?.Stream is null) return;
            using var reader = XmlReader.Create(info.Stream);
            Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch
        {
            // Editor stays fully functional even when the optional XSHD resource fails to load.
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        var file = files?.FirstOrDefault(path =>
            path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        if (file is null || !ConfirmSaveIfDirty()) return;
        LoadFile(file);
    }

    private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
