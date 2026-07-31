using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Mathematics;

namespace AlphaNative.Services;

/// <summary>
/// 将 Markdown 导出为可独立打开的单文件 HTML。
/// 页面布局、代码高亮与交互脚本均内嵌；本地图片会尽量转为 data URI。
/// LaTeX 由 MathJax 渲染，网络不可用时仍会显示原始公式文本。
/// </summary>
public static partial class HtmlExportService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseMathematics()
        .DisableHtml()
        .Build();

    public static string Create(
        string markdown,
        string? sourceFilePath,
        string title,
        bool darkMode)
    {
        var body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        body = NormalizeMathMarkup(body);
        body = HighlightCodeBlocks(body);
        body = EmbedLocalImages(body, sourceFilePath);

        var encodedTitle = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "α 文档" : title);
        var initialTheme = darkMode ? "dark" : "light";
        var generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        return $$"""
<!doctype html>
<html lang="zh-CN" data-theme="{{initialTheme}}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="generator" content="α Markdown 编辑器">
  <title>{{encodedTitle}}</title>
  <script>
    window.MathJax = {
      tex: {
        inlineMath: [['$', '$'], ['\\(', '\\)']],
        displayMath: [['$$', '$$'], ['\\[', '\\]']],
        processEscapes: true
      },
      svg: { fontCache: 'global' },
      options: { skipHtmlTags: ['script', 'noscript', 'style', 'textarea', 'pre', 'code'] }
    };
  </script>
  <script defer src="https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-svg.js"></script>
  <style>
    :root {
      color-scheme: light;
      --page:#f4f6f8; --paper:#fff; --text:#172033; --muted:#667085;
      --border:#dce2ea; --accent:#3659e3; --accent-soft:#e9edff;
      --code:#f6f8fa; --shadow:0 18px 55px rgba(30,42,70,.12);
      --kw:#cf222e; --str:#0a3069; --com:#6e7781; --num:#0550ae;
      --type:#953800; --fn:#8250df;
    }
    html[data-theme="dark"] {
      color-scheme: dark;
      --page:#0f141d; --paper:#171d28; --text:#e9edf5; --muted:#9aa4b5;
      --border:#2b3443; --accent:#8aa4ff; --accent-soft:#242f54;
      --code:#111722; --shadow:0 18px 60px rgba(0,0,0,.32);
      --kw:#ff7b72; --str:#a5d6ff; --com:#8b949e; --num:#79c0ff;
      --type:#ffa657; --fn:#d2a8ff;
    }
    * { box-sizing:border-box; }
    html { scroll-behavior:smooth; }
    body {
      margin:0; background:var(--page); color:var(--text);
      font:16px/1.75 Inter,system-ui,-apple-system,"Segoe UI","PingFang SC","Microsoft YaHei",sans-serif;
    }
    .document-toolbar {
      position:sticky; top:0; z-index:20; display:flex; align-items:center;
      justify-content:space-between; gap:14px; min-height:52px; padding:8px 18px;
      border-bottom:1px solid var(--border); background:color-mix(in srgb,var(--paper) 92%,transparent);
      backdrop-filter:blur(12px);
    }
    .brand { display:flex; align-items:center; gap:10px; min-width:0; }
    .brand-mark {
      display:grid; place-items:center; width:32px; height:32px; border-radius:9px;
      background:var(--accent); color:#fff; font-size:20px; font-weight:750;
    }
    .brand-title { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; font-weight:650; }
    .toolbar-actions { display:flex; gap:7px; flex:0 0 auto; }
    button {
      min-height:34px; padding:0 11px; border:1px solid var(--border); border-radius:8px;
      background:var(--paper); color:var(--text); cursor:pointer; font:inherit;
    }
    button:hover { border-color:var(--accent); background:var(--accent-soft); }
    main {
      width:min(940px,calc(100% - 28px)); margin:24px auto 64px; padding:clamp(28px,5vw,64px);
      border:1px solid var(--border); border-radius:15px; background:var(--paper); box-shadow:var(--shadow);
    }
    article > :first-child { margin-top:0; }
    article > :last-child { margin-bottom:0; }
    h1,h2,h3,h4,h5,h6 { line-height:1.32; scroll-margin-top:72px; }
    h1 { margin:0 0 .75em; padding-bottom:.34em; border-bottom:1px solid var(--border); font-size:2.08em; }
    h2 { margin:1.65em 0 .7em; padding-bottom:.28em; border-bottom:1px solid var(--border); font-size:1.55em; }
    h3 { margin:1.45em 0 .55em; font-size:1.27em; }
    p { margin:.82em 0; }
    a { color:var(--accent); text-underline-offset:3px; }
    img { display:block; max-width:100%; height:auto; margin:1.2em auto; border-radius:9px; }
    blockquote {
      margin:1.15em 0; padding:.5em 1em; border-left:4px solid var(--accent);
      border-radius:0 8px 8px 0; background:var(--accent-soft); color:var(--muted);
    }
    ul,ol { padding-left:1.65em; }
    li + li { margin-top:.22em; }
    hr { margin:2em 0; border:0; border-top:1px solid var(--border); }
    table { display:block; width:100%; overflow:auto; border-collapse:collapse; margin:1.2em 0; }
    th,td { padding:8px 11px; border:1px solid var(--border); text-align:left; }
    th { background:var(--code); font-weight:650; }
    code {
      font-family:"Cascadia Mono",Consolas,"SFMono-Regular",monospace;
      font-size:.91em;
    }
    :not(pre)>code { padding:.15em .38em; border:1px solid var(--border); border-radius:5px; background:var(--code); }
    .code-wrap { position:relative; margin:1.25em 0; }
    .code-head {
      display:flex; align-items:center; justify-content:space-between; height:36px; padding:0 9px 0 13px;
      border:1px solid var(--border); border-bottom:0; border-radius:10px 10px 0 0;
      background:var(--paper); color:var(--muted); font-size:12px;
    }
    .code-language { font-weight:700; letter-spacing:.04em; text-transform:uppercase; }
    .copy-code { min-height:27px; padding:0 8px; font-size:12px; background:transparent; }
    pre {
      margin:0; padding:16px; overflow:auto; border:1px solid var(--border);
      border-radius:0 0 10px 10px; background:var(--code); line-height:1.62;
      tab-size:4; white-space:pre;
    }
    pre code { display:block; color:var(--text); }
    .tok-keyword { color:var(--kw); font-weight:650; }
    .tok-string { color:var(--str); }
    .tok-comment { color:var(--com); font-style:italic; }
    .tok-number { color:var(--num); }
    .tok-type { color:var(--type); }
    .tok-function { color:var(--fn); }
    .math-inline { cursor:text; }
    .math-block { margin:1.2em 0; overflow-x:auto; text-align:center; }
    .export-note { margin-top:42px; padding-top:14px; border-top:1px solid var(--border); color:var(--muted); font-size:12px; }
    .task-list-item { list-style:none; }
    input[type="checkbox"] { accent-color:var(--accent); }
    @media (max-width:620px) {
      .document-toolbar { padding:7px 10px; }
      .brand-title { max-width:42vw; }
      main { width:100%; margin:0; padding:24px 18px 52px; border:0; border-radius:0; box-shadow:none; }
      .toolbar-actions button span { display:none; }
    }
    @media print {
      @page { size:A4; margin:16mm 15mm 18mm; }
      body { background:#fff; color:#000; }
      .document-toolbar { display:none !important; }
      main { width:auto; margin:0; padding:0; border:0; box-shadow:none; background:#fff; }
      h1,h2,h3 { break-after:avoid; }
      pre,blockquote,table,img,.math-block { break-inside:avoid; }
      a { color:#000; text-decoration:none; }
      .copy-code { display:none; }
      .export-note { display:none; }
    }
  </style>
</head>
<body>
  <header class="document-toolbar">
    <div class="brand">
      <span class="brand-mark">α</span>
      <span class="brand-title">{{encodedTitle}}</span>
    </div>
    <div class="toolbar-actions">
      <button type="button" id="themeButton" title="切换明暗主题">◐ <span>主题</span></button>
      <button type="button" onclick="window.print()" title="打印或保存为 PDF">⎙ <span>打印</span></button>
    </div>
  </header>
  <main>
    <article id="document">{{body}}</article>
    <div class="export-note">由 α Markdown 编辑器导出 · {{generatedAt}}</div>
  </main>
  <script>
    (() => {
      'use strict';
      document.querySelectorAll('.copy-code').forEach(button => {
        button.addEventListener('click', async () => {
          const source = button.closest('.code-wrap')?.querySelector('code')?.textContent || '';
          try { await navigator.clipboard.writeText(source); button.textContent='已复制'; }
          catch { button.textContent='复制失败'; }
          setTimeout(()=>button.textContent='复制代码',1200);
        });
      });
      const themeButton = document.getElementById('themeButton');
      themeButton.addEventListener('click', () => {
        const root = document.documentElement;
        root.dataset.theme = root.dataset.theme === 'dark' ? 'light' : 'dark';
      });
    })();
  </script>
</body>
</html>
""";
    }

    private static string HighlightCodeBlocks(string html)
    {
        return CodeBlockRegex().Replace(html, match =>
        {
            var language = SyntaxHighlighter.NormalizeLanguage(match.Groups[1].Value);
            var display = WebUtility.HtmlEncode(SyntaxHighlighter.DisplayName(language));
            var code = WebUtility.HtmlDecode(match.Groups[2].Value);
            var highlighted = SyntaxHighlighter.HighlightHtml(code, language);
            var className = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(language) ? "plaintext" : language);
            return $"<div class=\"code-wrap\"><div class=\"code-head\"><span class=\"code-language\">{display}</span><button type=\"button\" class=\"copy-code\">复制代码</button></div><pre><code class=\"language-{className}\">{highlighted}</code></pre></div>";
        });
    }

    private static string NormalizeMathMarkup(string html)
    {
        // Markdig Mathematics 输出 class="math"；补回 MathJax 可识别的分隔符。
        html = MathDivRegex().Replace(html, match =>
            $"<div class=\"math-block\">\\[{match.Groups[1].Value.Trim()}\\]</div>");
        html = MathSpanRegex().Replace(html, match =>
            $"<span class=\"math-inline\">\\({match.Groups[1].Value.Trim()}\\)</span>");
        return html;
    }

    private static string EmbedLocalImages(string html, string? sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath)) return html;
        var baseDirectory = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrWhiteSpace(baseDirectory)) return html;

        return ImageSourceRegex().Replace(html, match =>
        {
            var rawSource = WebUtility.HtmlDecode(match.Groups[2].Value);
            if (rawSource.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                Uri.TryCreate(rawSource, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                return match.Value;
            }

            try
            {
                var path = rawSource;
                if (Uri.TryCreate(rawSource, UriKind.Absolute, out var fileUri) && fileUri.IsFile)
                {
                    path = fileUri.LocalPath;
                }
                else if (!Path.IsPathRooted(path))
                {
                    path = Path.GetFullPath(Path.Combine(baseDirectory, path.Replace('/', Path.DirectorySeparatorChar)));
                }

                if (!File.Exists(path)) return match.Value;
                var bytes = File.ReadAllBytes(path);
                // 防止意外把超大文件嵌入 HTML。
                if (bytes.Length > 20 * 1024 * 1024) return match.Value;
                var mime = MimeType(path);
                var data = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                return $"{match.Groups[1].Value}{data}{match.Groups[3].Value}";
            }
            catch
            {
                return match.Value;
            }
        });
    }

    private static string MimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    [GeneratedRegex("""<pre><code(?:\s+class="language-([^"]*)")?>([\s\S]*?)</code></pre>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex("<div\\s+class=\"math\"[^>]*>([\\s\\S]*?)</div>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MathDivRegex();

    [GeneratedRegex("<span\\s+class=\"math\"[^>]*>([\\s\\S]*?)</span>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MathSpanRegex();

    [GeneratedRegex("(<img\\b[^>]*?\\bsrc\\s*=\\s*[\"'])([^\"']+)([\"'][^>]*>)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageSourceRegex();
}
