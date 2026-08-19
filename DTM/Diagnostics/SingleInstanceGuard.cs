using System.IO.Pipes;
using NLog;
using SystemFile = System.IO.File;

namespace DTM.Diagnostics;

/// <summary>
/// Verhindert Zweitstarts (Kroste-Skill-Standard, Pflicht fuer Tray-Apps —
/// Referenz: Klemmbrett). Ohne Guard laufen bei DTM zwei Prozesse mit je
/// einem eigenen PowerShell-Runspace, zwei Tray-Icons konkurrieren um
/// dieselbe Position, und beide schreiben auf dieselbe
/// <c>connections.json</c> — der letzte Save gewinnt und verwirft die
/// Aenderungen des anderen.
///
/// Erwartetes Verhalten: der zweite Start holt die bestehende Instanz in den
/// Vordergrund und beendet sich selbst.
///
/// Umsetzung ueber eine Named Pipe, die .NET auf Linux/macOS als
/// Unix-Domain-Socket unter <c>/tmp/CoreFxPipe_&lt;name&gt;</c> abbildet —
/// damit cross-platform ohne Extracode. Der Pipe-Name enthaelt den
/// Benutzernamen, sonst blockieren sich verschiedene Benutzer auf einem
/// Terminalserver gegenseitig.
///
/// <b>Stale-Socket-Recovery:</b> DTM beendet sich in manchen Pfaden per
/// <c>Process.Kill()</c> (PowerShell-SDK-Finalizer-Haenger, siehe
/// <c>UpdateService</c>) — dabei laeuft kein <see cref="Dispose"/>. Windows
/// raeumt Named Pipes selbst auf; auf Linux bleibt die Socket-Datei liegen
/// und wuerde jeden weiteren Start dauerhaft blockieren. Deshalb wird bei
/// belegter Pipe geprueft, ob sich wirklich jemand verbinden laesst — wenn
/// nicht, ist der Socket verwaist und wird entfernt.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    /// <summary>Aktivierungsbyte, das der Zweitstart an die Erstinstanz schickt.</summary>
    private const byte ActivationByte = (byte)'A';

    private readonly string _pipeName;
    private NamedPipeServerStream? _server;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    /// <summary>
    /// Feuert, wenn ein Zweitstart die Aktivierung anfordert. Wird auf einem
    /// ThreadPool-Thread ausgeloest — der Abonnent muss selbst auf den
    /// UI-Thread dispatchen.
    /// </summary>
    public event EventHandler? ActivationRequested;

    public SingleInstanceGuard(string appName = "DTM")
    {
        _pipeName = $"{appName}.SingleInstance.{Sanitize(Environment.UserName)}";
    }

    /// <summary>
    /// Versucht, die Rolle der Erstinstanz zu belegen. <c>true</c> = wir sind
    /// die Erstinstanz und lauschen ab jetzt auf Aktivierungswuensche.
    /// <c>false</c> = es laeuft bereits eine Instanz.
    /// </summary>
    public bool TryClaim()
    {
        if (TryCreateServer()) return true;

        // Belegt — aber lebt da wirklich jemand? Auf Windows ja (das OS
        // raeumt Pipes beim Prozessende auf), auf Linux kann die Socket-Datei
        // von einem gekillten Vorgaenger stammen.
        if (!OperatingSystem.IsWindows() && !CanReachPrimary())
        {
            _logger.Warn("Verwaister Single-Instance-Socket erkannt — wird entfernt.");
            TryRemoveStaleSocket();
            if (TryCreateServer()) return true;
        }

        _logger.Info("DTM laeuft bereits — dieser Start meldet sich bei der bestehenden Instanz ab.");
        return false;
    }

    /// <summary>
    /// Bittet die laufende Erstinstanz, ihr Fenster zu zeigen. Nur aufrufen,
    /// wenn <see cref="TryClaim"/> <c>false</c> geliefert hat.
    /// </summary>
    public void NotifyPrimary()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(500);
            client.WriteByte(ActivationByte);
            client.Flush();
            _logger.Info("Bestehende Instanz zur Anzeige aufgefordert.");
        }
        catch (Exception ex)
        {
            // Nicht kritisch: der Zweitstart beendet sich ohnehin. Im
            // schlimmsten Fall bleibt das Fenster der Erstinstanz versteckt.
            _logger.Warn(ex, "Bestehende Instanz konnte nicht benachrichtigt werden.");
        }
    }

    private bool TryCreateServer()
    {
        try
        {
            _server = new NamedPipeServerStream(
                _pipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenAsync(_cts.Token));
            return true;
        }
        catch (IOException)
        {
            // Pipe ist belegt — erwarteter Fall beim Zweitstart.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Prueft, ob hinter der belegten Pipe eine lebende Instanz sitzt.</summary>
    private bool CanReachPrimary()
    {
        try
        {
            using var probe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            probe.Connect(300);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryRemoveStaleSocket()
    {
        try
        {
            string socket = Path.Combine("/tmp", "CoreFxPipe_" + _pipeName);
            if (SystemFile.Exists(socket)) SystemFile.Delete(socket);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Verwaister Single-Instance-Socket konnte nicht entfernt werden.");
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _server is not null)
        {
            try
            {
                await _server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                byte[] buffer = new byte[1];
                int read = await _server.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 1 && buffer[0] == ActivationByte)
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Fehler im Single-Instance-Listener.");
            }
            finally
            {
                try
                {
                    if (_server is { IsConnected: true }) _server.Disconnect();
                }
                catch (ObjectDisposedException) { /* Dispose lief parallel */ }
            }
        }
    }

    /// <summary>Ersetzt alles, was in einem Pipe-/Dateinamen stoeren koennte.</summary>
    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "default";
        Span<char> buffer = stackalloc char[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            buffer[i] = char.IsLetterOrDigit(c) ? c : '_';
        }
        return new string(buffer);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _cts?.Dispose();
        _cts = null;

        try { _server?.Dispose(); } catch (ObjectDisposedException) { }
        _server = null;
    }
}
