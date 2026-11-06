using System.Windows;
using System.Windows.Media;
using IslamicNashat.Core;

namespace IslamicNashat.UI;

/// <summary>The Android app's emerald-and-gold tokens (presentation/theme/Nashat.kt), per widget theme.</summary>
public sealed record Palette(Color Background, Color Text, Color Muted, Color Accent, Color Gold, Color Row, Color Border, Color Warning)
{
    public static Palette For(WidgetTheme theme) => theme switch
    {
        WidgetTheme.Dark => new(C("#121B18"), C("#F3F1EA"), C("#A8B4AE"), C("#3DBE8B"), C("#E3C567"), C("#1D2B26"), C("#2C3D36"), C("#E8A04A")),
        WidgetTheme.Light => new(C("#FBF8EF"), C("#13201B"), C("#4C5A54"), C("#17694A"), C("#8E6E14"), C("#DCEDE3"), C("#E3DDCC"), C("#B0661A")),
        WidgetTheme.Glass => new(C("#8C0B1210"), C("#FFFFFF"), C("#E2E8E5"), C("#5FE0A8"), C("#F2D77E"), C("#26FFFFFF"), C("#40FFFFFF"), C("#FFB86B")),
        _ => new(C("#0F4A35"), C("#FBF8EF"), C("#B9D6C8"), C("#7FE0B4"), C("#E3C567"), C("#1B5E46"), C("#2E7058"), C("#F2B45A")),
    };

    public SolidColorBrush Brush(Color c) => Freeze(new SolidColorBrush(c));

    public SolidColorBrush BackgroundBrush(double opacity)
    {
        var c = Background;
        // Glass keeps its own low alpha; the others scale with the opacity setting.
        var alpha = c.A < 255 ? c.A : (byte)(255 * opacity);
        return Brush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

static class UiText
{
    public static FlowDirection Flow => Strings.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
}
