using System.Windows;
using System.Windows.Media;
using FloatingDeskAssistant.Configuration;
using Brush = System.Windows.Media.Brush;

namespace FloatingDeskAssistant.UI.Themes;

internal sealed record FusionColors(Brush Surface, Brush Panel, Brush Text, Brush Secondary, Brush Accent, Brush Border, Brush Selected)
{
    public static FusionColors For(FusionStyle style) => style switch
    {
        FusionStyle.Light => new(B("#FFFFFF"), B("#F3F3F3"), B("#141414"), B("#303030"), B("#004A70"), B("#666666"), B("#D9EAF4")),
        FusionStyle.Amber => new(B("#202020"), B("#303030"), B("#FFE49C"), B("#F4DFAE"), B("#8DE9FF"), B("#B1A17B"), B("#454039")),
        _ => new(B("#202020"), B("#303030"), B("#FAFAFA"), B("#E0E0E0"), B("#8DE9FF"), B("#989898"), B("#414D55"))
    };

    public void Apply(Window window)
    {
        foreach (var (key, brush) in new (string, Brush)[]
        {
            ("BattleSurface", Surface), ("BattlePanel", Panel), ("BattleText", Text), ("BattleSecondary", Secondary),
            ("BattleAccent", Accent), ("BattleBorder", Border), ("BattleHover", Selected), ("BattleSelected", Selected),
            ("BattleGrip", Border), ("BattleComposer", Panel), ("BattleSend", Selected), ("BattleSendText", Text),
            ("LightText", Text), ("LightSecondaryText", Secondary), ("LightBorder", Border), ("LightSurface", Panel),
            ("LightHover", Selected), ("LightPressed", Selected), ("LightFocus", Accent), ("LightDisabledText", Secondary),
            ("LightDisabledSurface", Panel), ("LightSelection", Selected)
        }) window.Resources[key] = brush;
    }

    public static void Clear(Window window)
    {
        foreach (var key in new[] { "BattleSurface", "BattlePanel", "BattleText", "BattleSecondary", "BattleAccent", "BattleBorder",
            "BattleHover", "BattleSelected", "BattleGrip", "BattleComposer", "BattleSend", "BattleSendText", "LightText", "LightSecondaryText",
            "LightBorder", "LightSurface", "LightHover", "LightPressed", "LightFocus", "LightDisabledText", "LightDisabledSurface", "LightSelection" })
            window.Resources.Remove(key);
    }

    private static Brush B(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }
}
