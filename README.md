# α Markdown 编辑器 v1.2.14

原生 Windows WPF Markdown 编辑器，支持多文档标签、阅读导航、双向同步滚动、原生 LaTeX 预览、代码高亮、HTML 与 PDF 导出。

## v1.2.14

- 修复分栏同步时首行贴近标题栏、顶部视觉遮挡的问题。
- 编辑区与预览区使用相同顶部同步锚点。
- 原生预览支持独立成行的 `\[ ... \]` 块级公式。
- 增加 `\boldsymbol`、`\bm` 与常用矩阵环境的兼容回退。
- Markdown 源文件及 HTML 导出内容保持原样。

详见 [TOP_ALIGN_MATH_DELIMITER_UPDATE.md](TOP_ALIGN_MATH_DELIMITER_UPDATE.md)。

## 云端构建

将项目文件放在 GitHub 仓库根目录并提交，`.github/workflows/windows-build.yml` 会生成：

- `Alpha-portable-win-x64`：便携版 `α.exe`
- `Alpha-installer-win-x64`：Windows x64 安装程序

## 本地构建

在 Windows PowerShell 中运行：

```powershell
./scripts/build.ps1
```

仅生成便携版：

```powershell
./scripts/build-no-install.ps1
```
