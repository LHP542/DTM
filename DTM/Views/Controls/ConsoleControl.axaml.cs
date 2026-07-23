using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DTM.Data.Terminal;
using NLog;
using SystemFile = System.IO.File;

namespace DTM.Views.Controls;

/// <summary>
/// PowerShell-Konsole im UI. Hält eine in-process <see cref="PowerShellTerminalSession"/>,
/// zeigt deren Output farbig über die <see cref="AnsiConsole"/> an und schickt
/// getippte Befehle (bzw. Antworten auf Read-Host-Prompts) an die Session.
/// (Der frühere SSH-Modus wurde entfernt — alle Oracle-Befehle laufen über die
/// FOC-SQL-Modulfunktionen, die ihr eigenes SSH-Remoting kapseln.)
/// </summary>
public partial class ConsoleControl : UserControl
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    public static readonly StyledProperty<string> InitialScriptProperty =
        AvaloniaProperty.Register<ConsoleControl, string>(nameof(InitialScript), defaultValue: string.Empty);

    public string InitialScript
    {
        get => GetValue(InitialScriptProperty);
        set => SetValue(InitialScriptProperty, value);
    }

    private ITerminalSession? _session;
    private string? _startedSignature;
    private bool _windowCloseRegistered;

    public ConsoleControl()
    {
        InitializeComponent();
    }

    private string CurrentSignature() => $"pwsh|{InitialScript.GetHashCode()}";

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!_windowCloseRegistered && TopLevel.GetTopLevel(this) is Window w)
        {
            _windowCloseRegistered = true;
            w.Closed += (_, _) => Stop();
        }

        if (_session is null || !_session.IsRunning || _startedSignature != CurrentSignature())
        {
            Stop();
            Start();
        }
    }

    public void Start()
    {
        if (_session is { IsRunning: true }) return;

        try
        {
            string? init = string.IsNullOrWhiteSpace(InitialScript) ? null : InitialScript;
            _session = new PowerShellTerminalSession(init);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Konnte PowerShell-Session nicht erstellen.");
            Append($"[Fehler beim Erstellen der Session: {ex.Message}]", "ERR", appendNewline: true);
            return;
        }

        WireUp(_session);
        // Session am Bus registrieren, damit Aktionen (Backup etc.) live im Tab erscheinen.
        TerminalBus.RegisterPowerShellSession(_session);

        _startedSignature = CurrentSignature();
        _ = _session.StartAsync();
    }

    private void WireUp(ITerminalSession session)
    {
        session.OutputReceived += (_, text) => Append(text, "OUT", appendNewline: false);
        session.ErrorReceived  += (_, text) => Append(text, "ERR", appendNewline: false);
        session.Notice         += (_, text) => Append(text, "NTC", appendNewline: true);
        session.SessionEnded   += (_, _)    => Append("Session beendet", "NTC", appendNewline: true);
    }

    public void Stop()
    {
        if (_session is null) return;
        // Bus-Registrierung zuerst lösen, damit kein Job nach Dispose die tote Session nutzt.
        TerminalBus.UnregisterPowerShellSession(_session);
        try { _session.Dispose(); }
        catch { /* swallow */ }
        _session = null;
        _startedSignature = null;
    }

    public void SendCommand(string cmd)
    {
        if (_session is null || !_session.IsRunning)
        {
            Append("Keine aktive Session", "ERR", appendNewline: true);
            return;
        }

        // Befehl lokal als Echo anzeigen. Wenn die Session gerade auf einen
        // Read-Host-Prompt wartet, interpretiert sie die Eingabe als Antwort.
        Append($"> {cmd}", "ECHO", appendNewline: true);
        _ = _session.SendCommandAsync(cmd);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;

        string cmd = InputBox.Text ?? string.Empty;
        InputBox.Text = string.Empty;
        SendCommand(cmd);
    }

    /// <summary>
    /// Routet Stream-Output an die <see cref="AnsiConsole"/>. OUT wird ANSI-geparst
    /// (Farben), Meta-Streams (Error/Notice/Echo) bekommen feste Farben.
    /// Zusätzlich Diagnose-Log nach %TEMP%/dtm-console.log.
    /// </summary>
    private void Append(string? text, string kind, bool appendNewline = false)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            string logPath = Path.Combine(Path.GetTempPath(), "dtm-console.log");
            SystemFile.AppendAllText(logPath,
                $"{DateTime.Now:HH:mm:ss.fff} [{kind}] {text.Replace("\r", "\\r").Replace("\n", "\\n")}{Environment.NewLine}");
        }
        catch { /* swallow */ }

        string display = appendNewline ? text + "\n" : text;
        switch (kind)
        {
            case "ERR":
                Output.AppendLine(display, Style(0xCD, 0x5C, 0x5C, bold: false));
                break;
            case "NTC":
                Output.AppendLine(display, Style(0x87, 0xCE, 0xFA, bold: false));
                break;
            case "ECHO":
                Output.AppendLine(display, Style(0x90, 0xEE, 0x90, bold: true));
                break;
            default: // OUT
                Output.Append(display);
                break;
        }
    }

    private static AnsiStyle Style(byte r, byte g, byte b, bool bold) =>
        new(Foreground: new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.FromRgb(r, g, b)),
            Background: null, Bold: bold, Italic: false, Underline: false);
}
