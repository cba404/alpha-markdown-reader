using System;
using System.IO;

namespace AlphaNative.Models;

public sealed class DocumentSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public string? FilePath { get; set; }
    public string UntitledName { get; set; } = "未命名.md";
    public string Text { get; set; } = string.Empty;
    public bool IsDirty { get; set; }
    public int CaretOffset { get; set; }
    public int TopLine { get; set; } = 1;

    public string DisplayName => string.IsNullOrWhiteSpace(FilePath)
        ? UntitledName
        : Path.GetFileName(FilePath);

    public string TabTitle => IsDirty ? $"● {DisplayName}" : DisplayName;
}
