using System.Windows.Media;

namespace AlphaNative.Models;

public sealed record RendererTheme(
    Brush Foreground,
    Brush Muted,
    Brush Border,
    Brush Accent,
    Brush AccentSoft,
    Brush CodeBackground,
    Brush PanelBackground,
    bool IsDark)
{
    public static RendererTheme Light { get; } = new(
        BrushFrom("#172033"), BrushFrom("#667085"), BrushFrom("#DCE2EA"),
        BrushFrom("#3659E3"), BrushFrom("#E9EDFF"), BrushFrom("#F6F8FA"),
        Brushes.White, false);

    public static RendererTheme Dark { get; } = new(
        BrushFrom("#E9EDF5"), BrushFrom("#9AA4B5"), BrushFrom("#2B3443"),
        BrushFrom("#8AA4FF"), BrushFrom("#242F54"), BrushFrom("#111722"),
        BrushFrom("#171D28"), true);

    private static SolidColorBrush BrushFrom(string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        return brush;
    }
}
