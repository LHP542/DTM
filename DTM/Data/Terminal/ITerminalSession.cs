namespace DTM.Data.Terminal;

/// <summary>
/// Abstraktion über eine interaktive Terminal-Session (aktuell die in-process
/// PowerShell-Session via Microsoft.PowerShell.SDK). Ersetzt das frühere
/// Process.Start-mit-Pipes-Modell. Als Interface gehalten, damit das
/// ConsoleControl und der TerminalBus nicht direkt an die konkrete
/// Session-Klasse koppeln.
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>Normaler Output (stdout-Äquivalent).</summary>
    event EventHandler<string>? OutputReceived;

    /// <summary>Fehler-Output (stderr-Äquivalent).</summary>
    event EventHandler<string>? ErrorReceived;

    /// <summary>Informelle Meldungen vom Wrapper (Verbindungs-Status etc.).</summary>
    event EventHandler<string>? Notice;

    /// <summary>Wird gefeuert, wenn die Session endet (regulär oder durch Fehler).</summary>
    event EventHandler? SessionEnded;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sendet einen Befehl an die Session. Läuft lokal im Runspace; ein laufender
    /// Befehl, der gerade auf einen Read-Host-Prompt wartet, erhält die Eingabe
    /// stattdessen als Prompt-Antwort.
    /// </summary>
    /// <param name="command">Der auszuführende Befehl/Skript.</param>
    /// <param name="cancellationToken">Abbruch-Token.</param>
    Task SendCommandAsync(string command, CancellationToken cancellationToken = default);

    Task StopAsync();
}
