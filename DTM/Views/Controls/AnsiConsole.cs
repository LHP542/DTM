using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DTM.Data.Terminal;

namespace DTM.Views.Controls;

/// <summary>
/// Schlankes Konsolen-Anzeige-Control mit voller ANSI-Farb-Unterstützung.
///
/// Designentscheidungen:
///  - Eine Zeile = ein <see cref="TextBlock"/> mit eigenen <see cref="Inlines"/>.
///    Damit umgehen wir den bekannten <c>SelectableTextBlock</c>-Inlines-Bug
///    in Avalonia 11 (#16820 und Folgeprobleme).
///  - <see cref="StackPanel"/> in einem <see cref="ScrollViewer"/> stapelt die
///    Zeilen vertikal. Auto-Scroll an's Ende, wenn der User unten ist.
///  - Selektion: pro Zeile (TextBlock kann nicht mehrzeilig selektieren).
///    Strg+C über das ContextMenu kopiert die letzten N Zeilen als Plain Text.
///  - Buffer-Limit: 5000 Zeilen, ältere werden abgeschnitten. Vermeidet
///    Memory-Bloat bei tail-artigen Backup-Outputs.
/// </summary>
public sealed class AnsiConsole : UserControl
{
    private const int MaxLines = 5000;

    // Default-Farben für nicht-ANSI Text
    private static readonly IBrush DefaultForeground =
        new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private readonly ScrollViewer _scroll;
    private readonly StackPanel _lines;
    private readonly AnsiParser _parser = new();
    private readonly object _parserLock = new();

    // Wir bauen die aktuelle Zeile in diesem Builder auf, bis ein '\n' kommt.
    // Streaming-Output (z.B. SSH) liefert oft halbe Zeilen.
    private TextBlock? _currentLine;
    private bool _autoScroll = true;

    // Konsolen-Hintergrundton, abgestimmt auf das Dark-Theme (statt reinem Schwarz).
    private static readonly IBrush ConsoleBg =
        new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(0x0A, 0x0E, 0x12));

    public AnsiConsole()
    {
        _lines = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Background = ConsoleBg
        };
        _scroll = new ScrollViewer
        {
            Background = ConsoleBg,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _lines
        };

        // Wenn der User scrollt, deaktivieren wir Auto-Scroll. Wenn er zurück
        // ans Ende scrollt, wieder aktivieren. Das ist die etablierte Konsolen-UX.
        _scroll.ScrollChanged += (_, _) =>
        {
            double bottom = _scroll.Extent.Height - _scroll.Viewport.Height;
            // Threshold: 4px Toleranz für Fließkomma-Rundung.
            _autoScroll = (_scroll.Offset.Y >= bottom - 4);
        };

        // Strg+C kopiert den gesamten Output.
        ContextMenu = BuildContextMenu();

        Background = ConsoleBg;
        Content = _scroll;
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        var copyAll = new MenuItem { Header = "Alles kopieren" };
        copyAll.Click += async (_, _) => await CopyAllToClipboardAsync();
        var clear = new MenuItem { Header = "Leeren" };
        clear.Click += (_, _) => Clear();
        menu.ItemsSource = new[] { copyAll, clear };
        return menu;
    }

    private async Task CopyAllToClipboardAsync()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in _lines.Children)
        {
            if (child is TextBlock tb)
            {
                foreach (var inline in tb.Inlines ?? Enumerable.Empty<Inline>())
                {
                    if (inline is Run r) sb.Append(r.Text);
                }
                sb.AppendLine();
            }
        }
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is { } clip)
        {
            // Avalonia 12: SetTextAsync wurde aus IClipboard entfernt; stattdessen
            // DataTransfer + DataTransferItem.CreateText über SetDataAsync setzen.
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(sb.ToString()));
            await clip.SetDataAsync(data).ConfigureAwait(false);
        }
    }

    /// <summary>Räumt den Buffer komplett auf.</summary>
    public void Clear()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lines.Children.Clear();
            _currentLine = null;
        });
    }

    /// <summary>
    /// Fügt Streaming-Output ein. Der Text kann mitten in einer Zeile, einer
    /// ANSI-Sequenz oder einem UTF-8-Codepoint enden; State bleibt erhalten.
    /// </summary>
    public void Append(string text, AnsiStyle? styleOverride = null)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Parsen kann auf dem Background-Thread laufen, aber UI-Updates müssen
        // auf den UI-Thread. Ergebnisse vorab sammeln, dann posten.
        // Der Parser ist statefull und NICHT thread-safe — daher das Feed unter
        // Lock, falls SSH-Read-Loop und Backup-Notice gleichzeitig anklopfen.
        List<AnsiSpan> spans;
        lock (_parserLock)
            spans = _parser.Feed(text).ToList();

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var span in spans)
            {
                AnsiStyle style = styleOverride ?? span.Style;
                AppendSpanInline(span.Text, style);
            }
            if (_autoScroll) _scroll.ScrollToEnd();

            // Buffer-Limit durchsetzen.
            if (_lines.Children.Count > MaxLines)
            {
                int toRemove = _lines.Children.Count - MaxLines;
                for (int i = 0; i < toRemove; i++)
                    _lines.Children.RemoveAt(0);
            }
        });
    }

    /// <summary>
    /// Fügt eine ganze Zeile mit fixem Stil hinzu (z.B. NTC, ERR, ECHO).
    /// Macht keinen ANSI-Parse, behandelt den Text als opaken String.
    /// Newlines splittet sie sauber in mehrere Zeilen.
    /// </summary>
    public void AppendLine(string text, AnsiStyle style)
    {
        if (string.IsNullOrEmpty(text)) return;
        Dispatcher.UIThread.Post(() =>
        {
            string[] parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    AppendSpanInline(parts[i], style);
                if (i < parts.Length - 1)
                    BreakLine();
            }
            BreakLine();
            if (_autoScroll) _scroll.ScrollToEnd();
        });
    }

    // -- private rendering -----------------------------------------------------

    private void AppendSpanInline(string text, AnsiStyle style)
    {
        // Text kann '\n' enthalten (im Span aus dem Parser). Zeilenweise verarbeiten.
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                if (i > start) AppendRunToCurrentLine(text.Substring(start, i - start), style);
                BreakLine();
                start = i + 1;
            }
        }
        if (start < text.Length)
            AppendRunToCurrentLine(text.Substring(start), style);
    }

    private void EnsureCurrentLine()
    {
        if (_currentLine is not null) return;
        _currentLine = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, Monospace"),
            FontSize = 12,
            Foreground = DefaultForeground,
            TextWrapping = TextWrapping.NoWrap,
            // Inlines-Collection wird beim ersten Add automatisch initialisiert.
        };
        _lines.Children.Add(_currentLine);
    }

    private void AppendRunToCurrentLine(string text, AnsiStyle style)
    {
        EnsureCurrentLine();
        var run = new Run(text);
        if (style.Foreground is not null) run.Foreground = style.Foreground;
        if (style.Background is not null) run.Background = style.Background;
        if (style.Bold)      run.FontWeight = FontWeight.Bold;
        if (style.Italic)    run.FontStyle  = FontStyle.Italic;
        if (style.Underline) run.TextDecorations = TextDecorations.Underline;
        _currentLine!.Inlines!.Add(run);
    }

    private void BreakLine()
    {
        // Aktuelle Zeile abschließen, nächste startet leer.
        _currentLine = null;
    }
}
