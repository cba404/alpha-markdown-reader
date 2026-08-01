using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private readonly DispatcherTimer _statisticsTimer;
    private readonly DispatcherTimer _stateTimer;
    private readonly DispatcherTimer _editorScrollSyncTimer;
    private readonly DispatcherTimer _previewScrollTimer;
    private readonly SearchPanel _searchPanel;
    private readonly string[] _startupPaths;
    private readonly List<DocumentSession> _documents = new();
    private readonly List<NavigationHeading> _navigationHeadings = new();
    private readonly List<Button> _navigationButtons = new();
    private readonly Dictionary<DocumentSession, PreviewCacheEntry> _previewCache = new();
    private readonly LinkedList<DocumentSession> _previewCacheLru = new();

    private AppState _state = new();
    private DocumentSession? _activeDocument;
    private SortedDictionary<int, TextPointer> _sourceAnchors = new();
    private int[] _anchorLines = Array.Empty<int>();
    private int[] _navigationHeadingLines = Array.Empty<int>();
    private ScrollViewer? _editorScroll;
    private ScrollViewer? _previewScroll;
    private string? _currentPath;
    private bool _dirty;
    private bool _darkMode;
    private bool _suppressTextChanged;
    private bool _closingAccepted;
    private bool _scrollSyncQueued;
    private bool _previewUserOverride;
    private bool _syncingPreviewFromEditor;
    private bool _previewScrollHooked;
    private bool _syncingEditorFromPreview;
    private bool _previewRefreshPending;
    private bool _navigationRefreshPending = true;
    private bool _stateSavePending;
    private int _lastPreviewSyncedLine = -1;
    private int _lastPreviewAnchorIndex;
    private int _activeNavigationIndex = -1;
    private string _viewMode = "split";
    private int _untitledSequence = 1;
    private bool _navigationVisible = true;
    private string _wheelScrollMode = "gentle";

    private sealed record NavigationHeading(int Level, string Title, int Line);
    private sealed record PreviewCacheEntry(string? FilePath, bool DarkMode, RenderResult Result);

    private static readonly string SampleMarkdown = """
# α 原生 Markdown 编辑器

现在可以在同一个窗口中同时打开多个 Markdown 文件，并通过顶部标签页切换。

## 阅读导航

- 自动提取 H1–H6 标题生成目录
- 点击目录项跳转到编辑位置
- 滚动或移动光标时高亮当前章节
- `Ctrl+Shift+O` 显示或隐藏阅读导航
- “同步滚动”支持编辑区与预览区双向联动

## 多文档标签页

- “打开”支持一次选择多个 `.md`、`.markdown` 或 `.txt` 文件
- `Ctrl+Tab` / `Ctrl+Shift+Tab` 前后切换标签
- `Ctrl+W` 关闭当前标签
- 每个标签独立记录内容、保存状态、光标位置和滚动位置
- 拖入多个文件时会同时打开

## 常用 Markdown

- 标题、段落、引用、链接和图片
- ~~删除线~~、表格和任务列表
- [x] 已完成的任务
- [ ] 待完成的任务

> 左侧编辑 Markdown，右侧由 WPF `FlowDocument` 原生渲染，不使用浏览器或 HTML 页面。

| 功能 | 状态 |
| --- | :---: |
| 阅读导航 | ✅ |
| 多文件标签页 | ✅ |
| 原生 Markdown 预览 | ✅ |
| LaTeX 数学公式 | ✅ |
| 代码语法高亮 | ✅ |
| 双向同步滚动 | ✅ |
| HTML 单文件导出 | ✅ |
| Windows PDF 输出 | ✅ |

## LaTeX

行内公式：$E=mc^2$，以及 $e^{i\pi}+1=0$。

$$
\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}
$$

## 代码高亮

```javascript
function greet(name) {
  const message = `你好，${name}！`;
  console.log(message);
}
```

快捷键：**Ctrl+S** 保存、**Ctrl+O** 多选打开、**Ctrl+W** 关闭标签、**Ctrl+Tab** 切换标签。
""";

    public MainWindow(IEnumerable<string>? startupPaths = null)
    {
        _startupPaths = startupPaths?
            .Where(IsSupportedDocument)
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        InitializeComponent();

        _previewTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };

        _statisticsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(240)
        };
        _statisticsTimer.Tick += (_, _) =>
        {
            _statisticsTimer.Stop();
            UpdateStatistics();
        };

        // 滚动同步采用低频合并，不再运行 16ms 逐帧动画。
        // 这样可显著降低 FlowDocument 与 AvalonEdit 同时布局时的主线程压力。
        _editorScrollSyncTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(55)
        };
        _editorScrollSyncTimer.Tick += (_, _) =>
        {
            _editorScrollSyncTimer.Stop();
            ProcessEditorScroll();
        };

        _previewScrollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _previewScrollTimer.Tick += (_, _) =>
        {
            _previewScrollTimer.Stop();
            ProcessPreviewScroll();
        };

        _stateTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(8)
        };
        _stateTimer.Tick += (_, _) =>
        {
            if (_stateSavePending) SaveAppState();
        };
        _stateTimer.Start();

        _searchPanel = SearchPanel.Install(Editor.TextArea);
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 4;
        Editor.Options.EnableHyperlinks = false;
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => EditorScrolled();
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
            UpdateActiveNavigationItem(Editor.Document.GetLineByOffset(Editor.CaretOffset).LineNumber);

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadEditorHighlighting();
        _state = _stateService.Load();
        Width = Math.Max(MinWidth, _state.WindowWidth);
        Height = Math.Max(MinHeight, _state.WindowHeight);
        _darkMode = _state.DarkMode;
        _navigationVisible = _state.ReadingNavigationVisible;
        _wheelScrollMode = NormalizeWheelScrollMode(_state.WheelScrollMode);
        SyncScrollCheck.IsChecked = _state.SyncScroll;
        ApplyWheelScrollMode(_wheelScrollMode, showStatus: false);
        ApplyTheme(_darkMode, refresh: false);
        SetNavigationVisibility(_navigationVisible);

        var candidates = _startupPaths.Length > 0
            ? _startupPaths
            : (_state.OpenFiles ?? Array.Empty<string>())
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (candidates.Length == 0 && !string.IsNullOrWhiteSpace(_state.LastFile) && File.Exists(_state.LastFile))
        {
            candidates = new[] { _state.LastFile };
        }

        foreach (var path in candidates)
        {
            OpenFileInTab(path, activate: false);
        }

        if (_documents.Count > 0)
        {
            var preferred = !string.IsNullOrWhiteSpace(_state.LastFile)
                ? _documents.FirstOrDefault(d => string.Equals(d.FilePath, _state.LastFile, StringComparison.OrdinalIgnoreCase))
                : null;
            ActivateDocument(preferred ?? _documents[^1]);
        }
        else if (!string.IsNullOrWhiteSpace(_state.RecoveryText))
        {
            CreateDocument(_state.RecoveryText, dirty: true, untitledName: "恢复文档.md");
            SetStatus("已恢复上次未保存内容");
        }
        else
        {
            CreateDocument(SampleMarkdown, dirty: false, untitledName: NextUntitledName());
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _editorScroll = FindVisualChild<ScrollViewer>(Editor);
            _previewScroll = FindVisualChild<ScrollViewer>(PreviewViewer);
            AttachPreviewScrollTracking();
            QueueScrollSync();
            _stateSavePending = false;
        }));
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closingAccepted)
        {
            SaveAppState();
            return;
        }

        CaptureCurrentDocumentState();
        foreach (var document in _documents.ToList())
        {
            if (!ConfirmSaveDocument(document))
            {
                e.Cancel = true;
                return;
            }
        }

        _closingAccepted = true;
        SaveAppState();
        _previewTimer.Stop();
        _statisticsTimer.Stop();
        _editorScrollSyncTimer.Stop();
        _previewScrollTimer.Stop();
        _stateTimer.Stop();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!ctrl) return;

        switch (e.Key)
        {
            case Key.Tab:
                SwitchDocument(shift ? -1 : 1);
                e.Handled = true;
                break;
            case Key.W:
                CloseActiveDocument();
                e.Handled = true;
                break;
            case Key.S when shift:
                SaveAsDocument();
                e.Handled = true;
                break;
            case Key.S:
                SaveDocument();
                e.Handled = true;
                break;
            case Key.O when shift:
                ToggleNavigation();
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
        var becameDirty = !_dirty;
        _dirty = true;
        if (_activeDocument is not null)
        {
            _activeDocument.IsDirty = true;
            _activeDocument.CaretOffset = Editor.CaretOffset;
            InvalidatePreviewCache(_activeDocument);
        }

        _navigationRefreshPending = true;
        _stateSavePending = true;
        UpdateTitle();
        if (becameDirty) RefreshDocumentTabs();

        _statisticsTimer.Stop();
        _statisticsTimer.Start();
        SchedulePreviewRefresh();
    }

    private void SchedulePreviewRefresh(bool immediate = false)
    {
        _previewRefreshPending = true;
        if (_viewMode == "editor") return;

        var length = Editor.Document?.TextLength ?? 0;
        var delay = immediate ? 1 : length switch
        {
            < 30_000 => 280,
            < 100_000 => 430,
            < 300_000 => 650,
            _ => 900
        };
        _previewTimer.Interval = TimeSpan.FromMilliseconds(delay);
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void RefreshPreview()
    {
        _previewTimer.Stop();
        _previewRefreshPending = false;
        var text = Editor.Text ?? string.Empty;
        if (_navigationVisible && _navigationRefreshPending)
        {
            RefreshNavigation(text);
        }

        _previewUserOverride = false;
        _lastPreviewSyncedLine = -1;
        try
        {
            RenderResult result;
            if (_activeDocument is not null && TryGetCachedPreview(_activeDocument, out var cached))
            {
                result = cached;
            }
            else
            {
                result = _renderer.Render(text, _currentPath, CurrentTheme());
                if (_activeDocument is not null)
                {
                    StorePreviewCache(_activeDocument, result);
                }
            }

            ApplyRenderResult(result);
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

    private void ApplyRenderResult(RenderResult result)
    {
        PreviewViewer.Document = result.Document;
        _sourceAnchors = result.SourceAnchors;
        _anchorLines = _sourceAnchors.Keys.ToArray();
        _lastPreviewAnchorIndex = 0;
        CodeStatusText.Text = $"代码 {result.CodeBlocks}";
        FormulaStatusText.Text = result.FormulaErrors == 0 ? "公式正常" : $"公式错误 {result.FormulaErrors}";
        FormulaStatusText.Foreground = result.FormulaErrors == 0
            ? (Brush)FindResource("MutedBrush")
            : Brushes.IndianRed;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _previewScroll ??= FindVisualChild<ScrollViewer>(PreviewViewer);
            AttachPreviewScrollTracking();
            QueueScrollSync();
        }));
    }

    private bool TryGetCachedPreview(DocumentSession document, out RenderResult result)
    {
        if (_previewCache.TryGetValue(document, out var entry) &&
            string.Equals(entry.FilePath, _currentPath, StringComparison.OrdinalIgnoreCase) &&
            entry.DarkMode == _darkMode)
        {
            TouchPreviewCache(document);
            result = entry.Result;
            return true;
        }

        result = null!;
        return false;
    }

    private void StorePreviewCache(DocumentSession document, RenderResult result)
    {
        if ((Editor.Document?.TextLength ?? 0) > 250_000)
        {
            InvalidatePreviewCache(document);
            return;
        }
        _previewCache[document] = new PreviewCacheEntry(_currentPath, _darkMode, result);
        TouchPreviewCache(document);
        while (_previewCacheLru.Count > 4)
        {
            var oldest = _previewCacheLru.First!.Value;
            _previewCacheLru.RemoveFirst();
            _previewCache.Remove(oldest);
        }
    }

    private void TouchPreviewCache(DocumentSession document)
    {
        _previewCacheLru.Remove(document);
        _previewCacheLru.AddLast(document);
    }

    private void InvalidatePreviewCache(DocumentSession document)
    {
        _previewCache.Remove(document);
        _previewCacheLru.Remove(document);
    }

    private void EditorScrolled()
    {
        if (!_syncingEditorFromPreview && SyncScrollCheck.IsChecked == true && _viewMode == "split")
        {
            _previewUserOverride = false;
            _lastPreviewSyncedLine = -1;
            _scrollSyncQueued = true;
        }

        // 只安排一次后台处理，不在每个 ScrollOffsetChanged 事件中强制布局。
        if (!_editorScrollSyncTimer.IsEnabled) _editorScrollSyncTimer.Start();
    }

    private void ProcessEditorScroll()
    {
        var topLine = GetEditorTopLine();
        if (_activeDocument is not null) _activeDocument.TopLine = topLine;
        if (_navigationVisible) UpdateActiveNavigationItem(topLine);

        var shouldSync = _scrollSyncQueued;
        _scrollSyncQueued = false;
        if (shouldSync) SyncPreviewToEditor(topLine);
    }

    private void QueueScrollSync()
    {
        if (SyncScrollCheck.IsChecked != true || _viewMode != "split") return;
        _scrollSyncQueued = true;
        if (!_editorScrollSyncTimer.IsEnabled) _editorScrollSyncTimer.Start();
    }

    private void SyncPreviewToEditor(int topLine)
    {
        if (SyncScrollCheck.IsChecked != true || _viewMode != "split" ||
            _previewUserOverride || _previewScroll is null || _anchorLines.Length == 0) return;
        var index = Array.BinarySearch(_anchorLines, topLine);
        if (index < 0) index = ~index - 1;
        if (index < 0) index = 0;
        var anchorLine = _anchorLines[index];
        var pointer = _sourceAnchors[anchorLine];
        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty) return;

        var target = Math.Clamp(_previewScroll.VerticalOffset + rect.Top - 8, 0, _previewScroll.ScrollableHeight);
        ScrollPreviewDirect(target);
    }

    private int GetEditorTopLine()
    {
        try
        {
            Editor.TextArea.TextView.EnsureVisualLines();
            return Editor.TextArea.TextView.VisualLines.FirstOrDefault()?.FirstDocumentLine.LineNumber
                ?? Editor.Document.GetLineByOffset(Editor.CaretOffset).LineNumber;
        }
        catch
        {
            return Editor.Document.GetLineByOffset(Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength)).LineNumber;
        }
    }

    private void ScrollPreviewDirect(double targetOffset)
    {
        if (_previewScroll is null) return;
        var target = Math.Clamp(targetOffset, 0, _previewScroll.ScrollableHeight);
        if (Math.Abs(target - _previewScroll.VerticalOffset) < 1.5) return;

        _syncingPreviewFromEditor = true;
        _previewScroll.ScrollToVerticalOffset(target);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _syncingPreviewFromEditor = false;
        }));
    }

    private void Editor_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

        _editorScroll ??= FindVisualChild<ScrollViewer>(Editor);
        if (ScrollByWheel(_editorScroll, e.Delta))
        {
            e.Handled = true;
        }
    }

    private void PreviewViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

        BeginPreviewUserScroll();
        _previewScroll ??= FindVisualChild<ScrollViewer>(PreviewViewer);
        if (ScrollByWheel(_previewScroll, e.Delta))
        {
            e.Handled = true;
        }
    }

    private bool ScrollByWheel(ScrollViewer? scrollViewer, int delta)
    {
        if (scrollViewer is null || delta == 0) return false;
        var wheelSteps = delta / 120d;
        var pixelsPerStep = _wheelScrollMode switch
        {
            "fast" => 96d,
            "standard" => 66d,
            _ => 42d
        };
        var target = Math.Clamp(
            scrollViewer.VerticalOffset - wheelSteps * pixelsPerStep,
            0,
            scrollViewer.ScrollableHeight);
        if (Math.Abs(target - scrollViewer.VerticalOffset) < 0.25) return false;
        scrollViewer.ScrollToVerticalOffset(target);
        return true;
    }

    private void WheelSpeedMenu_Click(object sender, RoutedEventArgs e) => OpenButtonContextMenu(sender);

    private void WheelSpeedOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string mode }) return;
        ApplyWheelScrollMode(mode, showStatus: true);
    }

    private void ApplyWheelScrollMode(string mode, bool showStatus)
    {
        _wheelScrollMode = NormalizeWheelScrollMode(mode);
        if (WheelSpeedButton.ContextMenu is { } menu)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                item.IsChecked = item.Tag is string tag && tag == _wheelScrollMode;
            }
        }
        var label = _wheelScrollMode switch
        {
            "fast" => "快速",
            "standard" => "标准",
            _ => "舒缓"
        };
        WheelSpeedButton.Content = $"滚轮：{label} ▾";
        _stateSavePending = true;
        if (showStatus) SetStatus($"滚轮灵敏度已设为{label}");
    }

    private static string NormalizeWheelScrollMode(string? mode)
        => mode is "standard" or "fast" ? mode : "gentle";

    private void PreviewViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        => BeginPreviewUserScroll();

    private void PreviewViewer_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space)
        {
            BeginPreviewUserScroll();
        }
    }

    private void BeginPreviewUserScroll()
    {
        _previewUserOverride = true;
        _syncingPreviewFromEditor = false;
        _editorScrollSyncTimer.Stop();
        _scrollSyncQueued = false;
    }

    private void AttachPreviewScrollTracking()
    {
        if (_previewScroll is null || _previewScrollHooked) return;
        _previewScroll.ScrollChanged += PreviewScroll_ScrollChanged;
        _previewScrollHooked = true;
    }

    private void PreviewScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_viewMode == "editor" || _anchorLines.Length == 0 || _syncingPreviewFromEditor) return;
        if (!_previewScrollTimer.IsEnabled) _previewScrollTimer.Start();
    }

    private void ProcessPreviewScroll()
    {
        if (_viewMode == "editor" || _anchorLines.Length == 0 || _syncingPreviewFromEditor) return;

        var currentLine = GetPreviewTopSourceLine();
        if (_navigationVisible) UpdateActiveNavigationItem(currentLine);

        if (SyncScrollCheck.IsChecked != true || _viewMode != "split" || !_previewUserOverride) return;
        SyncEditorToPreview(currentLine);
    }

    private int GetPreviewTopSourceLine()
    {
        if (_anchorLines.Length == 0) return 1;
        var index = Math.Clamp(_lastPreviewAnchorIndex, 0, _anchorLines.Length - 1);
        var currentRect = _sourceAnchors[_anchorLines[index]].GetCharacterRect(LogicalDirection.Forward);
        if (currentRect.IsEmpty) return FindPreviewTopSourceLineFull();

        while (index > 0 && currentRect.Top > 90)
        {
            index--;
            currentRect = _sourceAnchors[_anchorLines[index]].GetCharacterRect(LogicalDirection.Forward);
            if (currentRect.IsEmpty) return FindPreviewTopSourceLineFull();
        }

        while (index + 1 < _anchorLines.Length)
        {
            var nextRect = _sourceAnchors[_anchorLines[index + 1]].GetCharacterRect(LogicalDirection.Forward);
            if (nextRect.IsEmpty || nextRect.Top > 90) break;
            index++;
        }

        _lastPreviewAnchorIndex = index;
        return _anchorLines[index];
    }

    private int FindPreviewTopSourceLineFull()
    {
        var currentIndex = 0;
        for (var index = 0; index < _anchorLines.Length; index++)
        {
            var rect = _sourceAnchors[_anchorLines[index]].GetCharacterRect(LogicalDirection.Forward);
            if (rect.IsEmpty) continue;
            if (rect.Top > 90) break;
            currentIndex = index;
        }
        _lastPreviewAnchorIndex = currentIndex;
        return _anchorLines[currentIndex];
    }

    private void SyncEditorToPreview(int sourceLine)
    {
        if (_syncingEditorFromPreview || sourceLine == _lastPreviewSyncedLine) return;
        if (Editor.Document is null || Editor.Document.LineCount == 0) return;

        var line = Math.Clamp(sourceLine, 1, Editor.Document.LineCount);
        var currentTopLine = GetEditorTopLine();
        if (Math.Abs(currentTopLine - line) <= 1)
        {
            _lastPreviewSyncedLine = line;
            return;
        }

        _lastPreviewSyncedLine = line;
        _syncingEditorFromPreview = true;
        Editor.ScrollToLine(line);
        if (_activeDocument is not null) _activeDocument.TopLine = line;

        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            _syncingEditorFromPreview = false;
        }));
    }

    private void ScrollPreviewToSourceLine(int sourceLine)
    {
        if (_previewScroll is null || _anchorLines.Length == 0)
        {
            QueueScrollSync();
            return;
        }

        var index = Array.BinarySearch(_anchorLines, sourceLine);
        if (index < 0) index = ~index - 1;
        if (index < 0) index = 0;
        var pointer = _sourceAnchors[_anchorLines[index]];
        var rect = pointer.GetCharacterRect(LogicalDirection.Forward);
        if (rect.IsEmpty) return;
        var target = Math.Clamp(_previewScroll.VerticalOffset + rect.Top - 8, 0, _previewScroll.ScrollableHeight);
        ScrollPreviewDirect(target);
    }

    private void Navigation_Click(object sender, RoutedEventArgs e) => ToggleNavigation();

    private void ToggleNavigation() => SetNavigationVisibility(!_navigationVisible);

    private void SetNavigationVisibility(bool visible)
    {
        _navigationVisible = visible;
        NavigationPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        NavigationSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        NavigationColumn.Width = visible ? new GridLength(230) : new GridLength(0);
        NavigationSplitterColumn.Width = visible ? new GridLength(5) : new GridLength(0);
        NavigationButton.Content = visible ? "隐藏导航" : "导航";
        NavigationButton.Style = visible
            ? (Style)FindResource("PrimaryButton")
            : (Style)FindResource(typeof(Button));
        _stateSavePending = true;

        if (visible)
        {
            if (_navigationRefreshPending) RefreshNavigation(Editor.Text ?? string.Empty, forceRebuild: true);
            else if (_navigationButtons.Count != _navigationHeadings.Count) RebuildNavigationButtons();
        }
        SetStatus(visible ? "阅读导航已显示" : "阅读导航已隐藏");
    }

    private void RefreshNavigation(string text, bool forceRebuild = false)
    {
        var headings = ExtractNavigationHeadings(text);
        var changed = headings.Count != _navigationHeadings.Count;
        if (!changed)
        {
            for (var index = 0; index < headings.Count; index++)
            {
                if (headings[index] != _navigationHeadings[index])
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            _navigationHeadings.Clear();
            _navigationHeadings.AddRange(headings);
            _navigationHeadingLines = _navigationHeadings.Select(h => h.Line).ToArray();
            _activeNavigationIndex = -1;
        }
        _navigationRefreshPending = false;

        if (_navigationVisible && (changed || forceRebuild || _navigationButtons.Count != _navigationHeadings.Count))
        {
            RebuildNavigationButtons();
        }

        NavigationCountText.Text = $"{_navigationHeadings.Count} 个标题";
        NavigationEmptyText.Visibility = _navigationHeadings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_navigationVisible) UpdateActiveNavigationItem(GetEditorTopLine(), force: true);
    }

    private static List<NavigationHeading> ExtractNavigationHeadings(string text)
    {
        var headings = new List<NavigationHeading>();
        using var reader = new StringReader(text);
        var lineNumber = 0;
        var inFence = false;
        char fenceCharacter = '\0';

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = line.AsSpan().TrimStart();
            if (trimmed.Length >= 3 && (trimmed[0] == '`' || trimmed[0] == '~'))
            {
                var candidate = trimmed[0];
                var count = 0;
                while (count < trimmed.Length && trimmed[count] == candidate) count++;
                if (count >= 3 && (!inFence || candidate == fenceCharacter))
                {
                    inFence = !inFence;
                    fenceCharacter = inFence ? candidate : '\0';
                    continue;
                }
            }

            if (inFence || !TryParseHeading(line, out var level, out var title)) continue;
            headings.Add(new NavigationHeading(level, title, lineNumber));
        }
        return headings;
    }

    private static bool TryParseHeading(string line, out int level, out string title)
    {
        level = 0;
        title = string.Empty;
        var index = 0;
        while (index < line.Length && index < 3 && line[index] == ' ') index++;
        while (index < line.Length && line[index] == '#' && level < 6)
        {
            level++;
            index++;
        }
        if (level == 0 || index >= line.Length || !char.IsWhiteSpace(line[index])) return false;

        while (index < line.Length && char.IsWhiteSpace(line[index])) index++;
        var value = line[index..].TrimEnd();
        var hashStart = value.Length;
        while (hashStart > 0 && value[hashStart - 1] == '#') hashStart--;
        if (hashStart < value.Length && hashStart > 0 && char.IsWhiteSpace(value[hashStart - 1]))
        {
            value = value[..(hashStart - 1)].TrimEnd();
        }
        if (value.Length == 0) return false;
        title = value;
        return true;
    }

    private void RebuildNavigationButtons()
    {
        NavigationItemsPanel.Children.Clear();
        _navigationButtons.Clear();
        foreach (var heading in _navigationHeadings)
        {
            var button = new Button
            {
                Content = heading.Title,
                Tag = heading,
                ToolTip = $"H{heading.Level} · 第 {heading.Line} 行\n{heading.Title}",
                Margin = new Thickness(4 + (heading.Level - 1) * 14, 1, 2, 1),
                Style = (Style)FindResource("NavigationItemButton")
            };
            button.Click += NavigationItem_Click;
            _navigationButtons.Add(button);
            NavigationItemsPanel.Children.Add(button);
        }
        _activeNavigationIndex = -1;
    }

    private void NavigationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NavigationHeading heading }) return;
        var line = Math.Clamp(heading.Line, 1, Math.Max(1, Editor.Document.LineCount));
        var documentLine = Editor.Document.GetLineByNumber(line);
        Editor.CaretOffset = Math.Clamp(documentLine.Offset, 0, Editor.Document.TextLength);
        Editor.ScrollToLine(line);
        Editor.Focus();
        _previewUserOverride = false;
        UpdateActiveNavigationItem(heading.Line, force: true);
        ScrollPreviewToSourceLine(heading.Line);
        SetStatus($"已跳转到：{heading.Title}");
    }

    private void UpdateActiveNavigationItem(int? requestedLine = null, bool force = false)
    {
        if (!_navigationVisible || _navigationHeadingLines.Length == 0 || _navigationButtons.Count == 0) return;
        var currentLine = requestedLine ?? GetEditorTopLine();
        var index = Array.BinarySearch(_navigationHeadingLines, currentLine);
        if (index < 0) index = ~index - 1;
        index = Math.Clamp(index, 0, _navigationHeadingLines.Length - 1);
        if (!force && index == _activeNavigationIndex) return;

        if (_activeNavigationIndex >= 0 && _activeNavigationIndex < _navigationButtons.Count)
        {
            _navigationButtons[_activeNavigationIndex].Style = (Style)FindResource("NavigationItemButton");
        }
        _activeNavigationIndex = index;
        var activeButton = _navigationButtons[index];
        activeButton.Style = (Style)FindResource("ActiveNavigationItemButton");
        if (force) activeButton.BringIntoView();
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewDocument();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenDocument();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveDocument();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAsDocument();
    private void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        CloseMenuPopups();
        ExportHtml();
    }
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        CloseMenuPopups();
        ExportPdf();
    }

    private void NewDocument()
    {
        CreateDocument(string.Empty, dirty: false, untitledName: NextUntitledName());
        Editor.Focus();
        SetStatus("已新建标签页");
    }

    private string NextUntitledName()
    {
        var name = _untitledSequence == 1 ? "未命名.md" : $"未命名 {_untitledSequence}.md";
        _untitledSequence++;
        return name;
    }

    private DocumentSession CreateDocument(string text, bool dirty, string untitledName, string? filePath = null, bool activate = true)
    {
        var document = new DocumentSession
        {
            FilePath = filePath,
            UntitledName = untitledName,
            Text = text,
            IsDirty = dirty,
            CaretOffset = 0,
            TopLine = 1
        };
        _documents.Add(document);
        _stateSavePending = true;
        RefreshDocumentTabs();
        if (activate) ActivateDocument(document);
        return document;
    }

    private void OpenDocument()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开一个或多个 Markdown 文件",
            Filter = "Markdown 文件 (*.md;*.markdown)|*.md;*.markdown|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        DocumentSession? last = null;
        foreach (var path in dialog.FileNames)
        {
            last = OpenFileInTab(path, activate: false) ?? last;
        }
        if (last is not null) ActivateDocument(last);
        SetStatus($"已打开 {dialog.FileNames.Length} 个文件");
    }

    private DocumentSession? OpenFileInTab(string path, bool activate = true)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var existing = _documents.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.FilePath) &&
                string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (activate) ActivateDocument(existing);
                return existing;
            }

            var text = File.ReadAllText(fullPath, Encoding.UTF8).TrimStart('\uFEFF');
            var document = CreateDocument(text, dirty: false, untitledName: Path.GetFileName(fullPath), filePath: fullPath, activate: activate);
            if (activate) SetStatus($"已打开 {Path.GetFileName(fullPath)}");
            return document;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "打开失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    private void ActivateDocument(DocumentSession document)
    {
        if (_activeDocument == document)
        {
            Editor.Focus();
            return;
        }

        CaptureCurrentDocumentState();
        _previewTimer.Stop();
        _editorScrollSyncTimer.Stop();
        _previewScrollTimer.Stop();
        _scrollSyncQueued = false;
        _previewUserOverride = false;
        _syncingPreviewFromEditor = false;
        _syncingEditorFromPreview = false;
        _lastPreviewSyncedLine = -1;
        _activeDocument = document;
        _currentPath = document.FilePath;
        _stateSavePending = true;
        _dirty = document.IsDirty;
        SetEditorText(document.Text, document.IsDirty);
        RefreshDocumentTabs();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            Editor.CaretOffset = Math.Clamp(document.CaretOffset, 0, Editor.Document.TextLength);
            Editor.ScrollToLine(Math.Clamp(document.TopLine, 1, Math.Max(1, Editor.Document.LineCount)));
            Editor.Focus();
            QueueScrollSync();
        }));
    }

    private void CaptureCurrentDocumentState(bool captureText = true)
    {
        if (_activeDocument is null) return;
        if (captureText) _activeDocument.Text = Editor.Text;
        _activeDocument.FilePath = _currentPath;
        _activeDocument.IsDirty = _dirty;
        _activeDocument.CaretOffset = Editor.CaretOffset;
        _activeDocument.TopLine = GetEditorTopLine();
    }

    private void RefreshDocumentTabs()
    {
        if (DocumentTabPanel is null) return;
        DocumentTabPanel.Children.Clear();
        FrameworkElement? activeElement = null;

        foreach (var document in _documents)
        {
            var container = new Grid
            {
                Tag = document,
                Margin = new Thickness(0, 4, 6, 4),
                MinWidth = 118,
                MaxWidth = 260
            };

            var tabButton = new Button
            {
                Content = document.TabTitle,
                Tag = document,
                ToolTip = document.FilePath ?? document.DisplayName,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Style = (Style)FindResource(document == _activeDocument ? "ActiveDocumentTabButton" : "DocumentTabButton")
            };
            tabButton.Click += DocumentTab_Click;

            var closeButton = new Button
            {
                Content = "×",
                Tag = document,
                ToolTip = $"关闭 {document.DisplayName}（Ctrl+W）",
                Style = (Style)FindResource("CloseDocumentTabButton")
            };
            closeButton.Click += CloseDocumentTab_Click;

            container.Children.Add(tabButton);
            container.Children.Add(closeButton);
            DocumentTabPanel.Children.Add(container);
            if (document == _activeDocument) activeElement = container;
        }

        DocumentCountText.Text = $"{_documents.Count} 个文档";
        activeElement?.BringIntoView();
    }

    private void DocumentTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DocumentSession document }) ActivateDocument(document);
    }

    private void CloseDocumentTab_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: DocumentSession document }) CloseDocument(document);
    }

    private void CloseActiveDocument()
    {
        if (_activeDocument is not null) CloseDocument(_activeDocument);
    }

    private void CloseDocument(DocumentSession document)
    {
        if (document == _activeDocument) CaptureCurrentDocumentState();
        if (!ConfirmSaveDocument(document)) return;

        var index = _documents.IndexOf(document);
        var wasActive = document == _activeDocument;
        _documents.Remove(document);
        InvalidatePreviewCache(document);
        _stateSavePending = true;

        if (_documents.Count == 0)
        {
            _activeDocument = null;
            CreateDocument(string.Empty, dirty: false, untitledName: NextUntitledName());
        }
        else if (wasActive)
        {
            ActivateDocument(_documents[Math.Min(index, _documents.Count - 1)]);
        }
        else
        {
            RefreshDocumentTabs();
        }
        SetStatus($"已关闭 {document.DisplayName}");
    }

    private void SwitchDocument(int direction)
    {
        if (_documents.Count < 2 || _activeDocument is null) return;
        var index = _documents.IndexOf(_activeDocument);
        var next = (index + direction + _documents.Count) % _documents.Count;
        ActivateDocument(_documents[next]);
    }

    private bool SaveDocument()
    {
        if (_activeDocument is null) return false;
        if (string.IsNullOrWhiteSpace(_currentPath)) return SaveAsDocument();
        try
        {
            File.WriteAllText(_currentPath, Editor.Text, new UTF8Encoding(false));
            _dirty = false;
            _activeDocument.Text = Editor.Text;
            _activeDocument.FilePath = _currentPath;
            _activeDocument.IsDirty = false;
            UpdateTitle();
            RefreshDocumentTabs();
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
        if (_activeDocument is null) return false;
        var dialog = new SaveFileDialog
        {
            Title = "保存 Markdown 文件",
            Filter = "Markdown 文件 (*.md)|*.md|Markdown 文件 (*.markdown)|*.markdown|文本文件 (*.txt)|*.txt",
            DefaultExt = ".md",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(_currentPath) ? _activeDocument.DisplayName : Path.GetFileName(_currentPath)
        };
        if (dialog.ShowDialog(this) != true) return false;

        var fullPath = Path.GetFullPath(dialog.FileName);
        var duplicate = _documents.FirstOrDefault(d => d != _activeDocument &&
            !string.IsNullOrWhiteSpace(d.FilePath) &&
            string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            MessageBox.Show(this, "该文件已经在另一个标签页中打开。", "无法另存为", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        _currentPath = fullPath;
        _activeDocument.FilePath = fullPath;
        InvalidatePreviewCache(_activeDocument);
        return SaveDocument();
    }

    private void ExportHtml()
    {
        var title = string.IsNullOrWhiteSpace(_currentPath) ? "α 文档" : Path.GetFileNameWithoutExtension(_currentPath);
        var dialog = new SaveFileDialog
        {
            Title = "导出为 HTML",
            Filter = "HTML 网页 (*.html)|*.html|HTML 网页 (*.htm)|*.htm",
            DefaultExt = ".html",
            AddExtension = true,
            FileName = title + ".html",
            InitialDirectory = string.IsNullOrWhiteSpace(_currentPath) ? null : Path.GetDirectoryName(_currentPath)
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var html = HtmlExportService.Create(Editor.Text, _currentPath, title, _darkMode);
            File.WriteAllText(dialog.FileName, html, new UTF8Encoding(false));
            SetStatus($"已导出 HTML：{Path.GetFileName(dialog.FileName)}");
            var result = MessageBox.Show(this, "HTML 已成功导出。是否立即使用默认浏览器打开？", "导出 HTML",
                MessageBoxButton.YesNo, MessageBoxImage.Information);
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

    private bool ConfirmSaveDocument(DocumentSession document)
    {
        if (document == _activeDocument) CaptureCurrentDocumentState();
        if (!document.IsDirty) return true;

        var result = MessageBox.Show(this, $"“{document.DisplayName}”尚未保存，是否先保存？", ProductName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.No) return true;

        if (document != _activeDocument) ActivateDocument(document);
        return SaveDocument();
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
        _navigationRefreshPending = true;
        UpdateTitle();
        UpdateStatistics();
        if (_viewMode == "editor")
        {
            _previewRefreshPending = true;
            if (_navigationVisible) RefreshNavigation(text, forceRebuild: true);
        }
        else
        {
            RefreshPreview();
        }
    }

    private void InsertMenu_Click(object sender, RoutedEventArgs e)
    {
        ExportPopup.IsOpen = false;
        InsertPopup.PlacementTarget = InsertMenuButton;
        InsertPopup.IsOpen = !InsertPopup.IsOpen;
    }

    private void ExportMenu_Click(object sender, RoutedEventArgs e)
    {
        InsertPopup.IsOpen = false;
        ExportPopup.PlacementTarget = ExportMenuButton;
        ExportPopup.IsOpen = !ExportPopup.IsOpen;
    }

    private void FormulaLibrary_Click(object sender, RoutedEventArgs e)
    {
        CloseMenuPopups();
        SetStatus("公式库预设将随下个版本继续扩充");
    }

    private void CloseMenuPopups()
    {
        InsertPopup.IsOpen = false;
        ExportPopup.IsOpen = false;
    }

    private static void OpenButtonContextMenu(object sender)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void Format_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string action }) return;
        CloseMenuPopups();
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

    private void Theme_Click(object sender, RoutedEventArgs e) => ApplyTheme(!_darkMode, refresh: true);

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
        SetBrush("PopupSurfaceBrush", dark ? "#171D28" : "#FFFFFF");
        SetBrush("PopupSurfaceAltBrush", dark ? "#202838" : "#F6F8FC");
        SetBrush("PopupDividerBrush", dark ? "#2B3443" : "#E7EBF3");
        ThemeButton.Content = dark ? "浅色" : "深色";
        _stateSavePending = true;
        Editor.Background = (Brush)FindResource("PanelBackground");
        Editor.Foreground = (Brush)FindResource("TextBrush");
        RefreshDocumentTabs();
        if (refresh)
        {
            if (_viewMode == "editor") _previewRefreshPending = true;
            else RefreshPreview();
        }
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
        if (sender is Button { Tag: string mode }) SetViewMode(mode);
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
                if (_previewRefreshPending) SchedulePreviewRefresh(immediate: true);
                break;
            default:
                _viewMode = "split";
                EditorPane.Visibility = Visibility.Visible;
                PreviewPane.Visibility = Visibility.Visible;
                MainSplitter.Visibility = Visibility.Visible;
                EditorColumn.Width = new GridLength(1, GridUnitType.Star);
                SplitterColumn.Width = new GridLength(5);
                PreviewColumn.Width = new GridLength(1, GridUnitType.Star);
                if (_previewRefreshPending) SchedulePreviewRefresh(immediate: true);
                else QueueScrollSync();
                break;
        }

        if (_viewMode != "split")
        {
            _scrollSyncQueued = false;
            _editorScrollSyncTimer.Stop();
            _previewScrollTimer.Stop();
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
            _lastPreviewSyncedLine = -1;
            QueueScrollSync();
            _stateSavePending = true;
            SetStatus("双向同步滚动已开启");
        }
        else
        {
            _syncingPreviewFromEditor = false;
            _syncingEditorFromPreview = false;
            _scrollSyncQueued = false;
            _editorScrollSyncTimer.Stop();
            _previewScrollTimer.Stop();
            _stateSavePending = true;
            SetStatus("同步滚动已关闭");
        }
    }

    private void UpdateStatistics()
    {
        var text = Editor.Text ?? string.Empty;
        var lines = 1;
        var words = 0;
        var inLatinWord = false;

        foreach (var ch in text)
        {
            if (ch == '\n') lines++;
            var isCjk = ch is >= '\u3400' and <= '\u4DBF' or >= '\u4E00' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';
            if (isCjk)
            {
                words++;
                inLatinWord = false;
                continue;
            }

            var isLatinWordCharacter = char.IsLetterOrDigit(ch) || (inLatinWord && ch is '\'' or '’' or '-');
            if (isLatinWordCharacter)
            {
                if (!inLatinWord && char.IsLetterOrDigit(ch)) words++;
                inLatinWord = true;
            }
            else
            {
                inLatinWord = false;
            }
        }

        LineCountText.Text = $"{lines} 行";
        WordCountText.Text = $"{words} 字词";
        CharacterCountText.Text = $"{text.Length} 字符";
    }

    private void UpdateTitle()
    {
        var fileName = _activeDocument?.DisplayName ?? "未命名.md";
        FileNameText.Text = fileName;
        Title = $"{(_dirty ? "● " : string.Empty)}{fileName} · {ProductName}";
    }

    private void SetStatus(string message)
    {
        if (StatusText is not null) StatusText.Text = message;
    }

    private void SaveAppState()
    {
        var needsRecoveryText = _activeDocument is { IsDirty: true, FilePath: null };
        CaptureCurrentDocumentState(captureText: needsRecoveryText);
        var state = new AppState
        {
            LastFile = _currentPath,
            OpenFiles = _documents.Where(d => !string.IsNullOrWhiteSpace(d.FilePath) && File.Exists(d.FilePath))
                .Select(d => d.FilePath!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RecoveryText = needsRecoveryText ? _activeDocument?.Text : null,
            DarkMode = _darkMode,
            SyncScroll = SyncScrollCheck.IsChecked == true,
            ReadingNavigationVisible = _navigationVisible,
            WheelScrollMode = _wheelScrollMode,
            WindowWidth = ActualWidth > 0 ? ActualWidth : Width,
            WindowHeight = ActualHeight > 0 ? ActualHeight : Height
        };
        _stateService.Save(state);
        _stateSavePending = false;
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
        var files = ((string[]?)e.Data.GetData(DataFormats.FileDrop) ?? Array.Empty<string>())
            .Where(IsSupportedDocument)
            .ToArray();
        if (files.Length == 0) return;

        DocumentSession? last = null;
        foreach (var file in files)
        {
            last = OpenFileInTab(file, activate: false) ?? last;
        }
        if (last is not null) ActivateDocument(last);
        SetStatus($"已拖入 {files.Length} 个文件");
    }

    private static bool IsSupportedDocument(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

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
