# Alpha GitHub 云端构建（根目录直传修正版）

> 解压后上传本目录内部的全部内容。提交后会自动运行 `Build Alpha Windows App`，无需先寻找 Run workflow。

# α 原生 Windows Markdown 编辑器

这是原生 C# / WPF 工程，不使用 HTML、Electron 或 WebView。

## 最简单的生成方式

阅读 `CLOUD_BUILD_GUIDE.md`，使用 GitHub Actions 云端生成：

- `α.exe` 便携版
- `α-Markdown编辑器-Setup-x64.exe` 安装版

本机无需安装 Visual Studio、.NET SDK 或 Inno Setup。

## 主要功能

- Markdown 原生编辑与预览
- LaTeX 数学公式
- 代码语法高亮
- 双栏同步滚动
- 打开、保存与另存为
- PDF 打印输出
- 深色与浅色主题

## 技术组成

- .NET 8 / WPF
- AvalonEdit
- Markdig
- WpfMath

第三方组件说明见 `THIRD-PARTY-NOTICES.md`。
