using Avalonia.Media;

namespace DTM.Data.Terminal;

/// <summary>
/// Stilbeschreibung für einen Text-Span. Brushes/Bool-Flags spiegeln SGR-Codes wider.
/// </summary>
public readonly record struct AnsiStyle(
    IBrush? Foreground,
    IBrush? Background,
    bool Bold,
    bool Italic,
    bool Underline)
{
    public static AnsiStyle Default => new(null, null, false, false, false);
}

/// <summary>Ein Stück Text mit zugehörigem Stil.</summary>
public readonly record struct AnsiSpan(string Text, AnsiStyle Style);

/// <summary>
/// Statefuller ANSI/CSI/OSC-Parser. Ein einziger Parser kann inkrementell mit
/// Streaming-Chunks gefüttert werden (Output ist nicht zeilenweise garantiert).
/// Liefert pro <see cref="Feed"/>-Aufruf eine Sequenz fertiger Spans + den
/// aktuellen Stil-State, der zwischen den Aufrufen erhalten bleibt.
///
/// Verwaltete Sequenzen:
///   ESC [ … m          → SGR-Farbe/Stil (Reset 0, 1=bold, 3=italic, 4=underline,
///                                        30-37 fg, 90-97 bright fg, 40-47 bg,
///                                        100-107 bright bg, 38;5;N indexed fg,
///                                        48;5;N indexed bg, 38;2;R;G;B 24bit,
///                                        48;2;R;G;B 24bit)
///   ESC [ … (sonst)    → andere CSI-Sequenz → verworfen
///   ESC ] … BEL/ST     → OSC (Title etc.) → verworfen
///   ESC P/X/^/_ … ST   → DCS/PM/APC/SOS → verworfen
///   ESC c, ESC = , ESC > etc. → einzeichige nach ESC → verworfen
///   \r, \b, BEL        → verworfen (\b/\r kommen in Login-Banner manchmal vor)
///   \n                 → bleibt im Text (Caller splittet selber zeilenweise)
///   \t                 → bleibt
///   alles andere       → durchgereicht
/// </summary>
public sealed class AnsiParser
{
    // Statemachine-States.
    private enum State { Normal, Escape, Csi, Osc, OscEsc, DcsLike, DcsLikeEsc }

    private State _state = State.Normal;
    private readonly System.Text.StringBuilder _textBuf = new();
    private readonly System.Text.StringBuilder _seqBuf  = new();
    private AnsiStyle _currentStyle = AnsiStyle.Default;

    /// <summary>Liefert den Spanno-Output für einen Chunk Text.</summary>
    public IEnumerable<AnsiSpan> Feed(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) yield break;

        foreach (char c in chunk)
        {
            switch (_state)
            {
                case State.Normal:
                    if (c == '\x1b') { FlushTextSpan(out AnsiSpan? s1); if (s1 is { } v1) yield return v1; _state = State.Escape; }
                    else if (c == '\a' || c == '\b') { /* drop BEL/BS */ }
                    else if (c == '\r') { /* drop carriage return; we use \n as line break */ }
                    else { _textBuf.Append(c); }
                    break;

                case State.Escape:
                    if (c == '[') { _state = State.Csi; _seqBuf.Clear(); }
                    else if (c == ']') { _state = State.Osc; _seqBuf.Clear(); }
                    else if (c == 'P' || c == 'X' || c == '^' || c == '_') { _state = State.DcsLike; _seqBuf.Clear(); }
                    else { /* one-char escapes like ESC c, ESC =, ESC > … dropped */ _state = State.Normal; }
                    break;

                case State.Csi:
                    // CSI: parameter bytes 0x30-0x3F, intermediate 0x20-0x2F, final 0x40-0x7E
                    if (c >= '\x40' && c <= '\x7e')
                    {
                        if (c == 'm') ApplySgr(_seqBuf.ToString());
                        // andere CSI-Befehle (cursor movement, screen clear etc.) verwerfen wir,
                        // weil TextBox-basiertes Output-Modell kein Cursor-Positioning kennt.
                        _state = State.Normal;
                        _seqBuf.Clear();
                    }
                    else
                    {
                        _seqBuf.Append(c);
                    }
                    break;

                case State.Osc:
                    if (c == '\a') { _state = State.Normal; _seqBuf.Clear(); }     // OSC + BEL terminator
                    else if (c == '\x1b') { _state = State.OscEsc; }                 // ESC \ = ST terminator
                    else { _seqBuf.Append(c); }
                    break;

                case State.OscEsc:
                    // Egal was kommt: OSC ist hier zu Ende.
                    _state = State.Normal; _seqBuf.Clear();
                    break;

                case State.DcsLike:
                    if (c == '\x1b') _state = State.DcsLikeEsc;
                    else _seqBuf.Append(c);
                    break;

                case State.DcsLikeEsc:
                    _state = State.Normal; _seqBuf.Clear();
                    break;
            }
        }

        FlushTextSpan(out AnsiSpan? trail);
        if (trail is { } t) yield return t;
    }

    private void FlushTextSpan(out AnsiSpan? span)
    {
        if (_textBuf.Length == 0) { span = null; return; }
        span = new AnsiSpan(_textBuf.ToString(), _currentStyle);
        _textBuf.Clear();
    }

    private void ApplySgr(string parameters)
    {
        // Leer oder "0" = Reset
        if (parameters.Length == 0) { _currentStyle = AnsiStyle.Default; return; }

        // SGR-Parameter sind ';'-separiert. Indexed-Farben sind mehrteilig
        // (38;5;N oder 38;2;R;G;B). Wir gehen mit einem Cursor durch.
        string[] parts = parameters.Split(';');
        int i = 0;
        while (i < parts.Length)
        {
            if (!int.TryParse(parts[i], out int code)) { i++; continue; }
            switch (code)
            {
                case 0: _currentStyle = AnsiStyle.Default; i++; break;
                case 1: _currentStyle = _currentStyle with { Bold = true }; i++; break;
                case 3: _currentStyle = _currentStyle with { Italic = true }; i++; break;
                case 4: _currentStyle = _currentStyle with { Underline = true }; i++; break;
                case 22: _currentStyle = _currentStyle with { Bold = false }; i++; break;
                case 23: _currentStyle = _currentStyle with { Italic = false }; i++; break;
                case 24: _currentStyle = _currentStyle with { Underline = false }; i++; break;
                case 39: _currentStyle = _currentStyle with { Foreground = null }; i++; break;
                case 49: _currentStyle = _currentStyle with { Background = null }; i++; break;
                case >= 30 and <= 37:
                    _currentStyle = _currentStyle with { Foreground = AnsiPalette.Standard[code - 30] };
                    i++; break;
                case >= 90 and <= 97:
                    _currentStyle = _currentStyle with { Foreground = AnsiPalette.Bright[code - 90] };
                    i++; break;
                case >= 40 and <= 47:
                    _currentStyle = _currentStyle with { Background = AnsiPalette.Standard[code - 40] };
                    i++; break;
                case >= 100 and <= 107:
                    _currentStyle = _currentStyle with { Background = AnsiPalette.Bright[code - 100] };
                    i++; break;
                case 38:
                case 48:
                {
                    // Extended: 38;5;N (256-color) oder 38;2;R;G;B (truecolor)
                    bool isFg = (code == 38);
                    if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out int mode))
                    {
                        if (mode == 5 && i + 2 < parts.Length && int.TryParse(parts[i + 2], out int idx))
                        {
                            IBrush b = AnsiPalette.Color256(idx);
                            _currentStyle = isFg
                                ? _currentStyle with { Foreground = b }
                                : _currentStyle with { Background = b };
                            i += 3; break;
                        }
                        if (mode == 2 && i + 4 < parts.Length
                            && int.TryParse(parts[i + 2], out int r)
                            && int.TryParse(parts[i + 3], out int g)
                            && int.TryParse(parts[i + 4], out int b24))
                        {
                            IBrush b = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b24));
                            _currentStyle = isFg
                                ? _currentStyle with { Foreground = b }
                                : _currentStyle with { Background = b };
                            i += 5; break;
                        }
                    }
                    i++; break;
                }
                default: i++; break;
            }
        }
    }
}
