using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace DTM.Data.Terminal;

/// <summary>
/// 256-Farben-ANSI-Palette. 16 benannte Farben (Standard 30-37, Bright 90-97)
/// plus 240 weitere Indizes für SGR 38;5;N / 48;5;N.
/// Farbwerte angelehnt an Windows Terminal "Campbell"-Schema, gut lesbar auf
/// schwarzem Hintergrund.
/// </summary>
public static class AnsiPalette
{
    // 0..7  - Standard (SGR 30-37)
    // 0=black 1=red 2=green 3=yellow 4=blue 5=magenta 6=cyan 7=white
    public static readonly IBrush[] Standard =
    {
        Brush(12, 12, 12),       // black
        Brush(197, 15, 31),      // red
        Brush(19, 161, 14),      // green
        Brush(193, 156, 0),      // yellow
        Brush(0, 55, 218),       // blue
        Brush(136, 23, 152),     // magenta
        Brush(58, 150, 221),     // cyan
        Brush(204, 204, 204)     // white
    };

    // 90..97 - Bright (SGR 90-97), heller Pendant
    public static readonly IBrush[] Bright =
    {
        Brush(118, 118, 118),    // bright black (grau)
        Brush(231, 72, 86),
        Brush(22, 198, 12),
        Brush(249, 241, 165),
        Brush(59, 120, 255),
        Brush(180, 0, 158),
        Brush(97, 214, 214),
        Brush(242, 242, 242)
    };

    /// <summary>
    /// Liefert die Farbe für einen 256-Index aus SGR 38;5;N / 48;5;N.
    /// 0-15 = Standard/Bright; 16-231 = 6x6x6-RGB-Cube; 232-255 = Graustufen.
    /// </summary>
    public static IBrush Color256(int idx)
    {
        if (idx < 0) idx = 0;
        if (idx > 255) idx = 255;
        if (idx < 8) return Standard[idx];
        if (idx < 16) return Bright[idx - 8];
        if (idx < 232)
        {
            int n = idx - 16;
            int r = n / 36;
            int g = (n / 6) % 6;
            int b = n % 6;
            return Brush(Step(r), Step(g), Step(b));
        }
        // 232..255 → 24 Graustufen
        byte v = (byte)(8 + (idx - 232) * 10);
        return Brush(v, v, v);
    }

    // Standard-Mapping 0..5 → 0,95,135,175,215,255 (xterm-Konvention).
    private static byte Step(int n) => n switch
    {
        0 => 0,
        1 => 95,
        2 => 135,
        3 => 175,
        4 => 215,
        _ => 255
    };

    private static IBrush Brush(byte r, byte g, byte b)
    {
        // ImmutableSolidColorBrush erbt NICHT von Animatable und hat damit keine
        // UI-Thread-Affinität. Wichtig, weil die Palette beim ANSI-Parsing auch
        // auf Background-Threads (z.B. SSH-Read-Loop) berührt wird. Ein normaler
        // SolidColorBrush würde dort 'Call from invalid thread' werfen.
        return new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
    }
}
