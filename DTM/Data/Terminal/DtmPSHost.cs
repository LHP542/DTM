using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Text;

namespace DTM.Data.Terminal;

/// <summary>
/// Custom PSHost für den in-process-Runspace. Der Default-Host wirft bei
/// Read-Host / Confirm / PromptForChoice "host does not support user
/// interaction". Dieser Host delegiert:
///  - Output (Write*) an Callbacks, die das ConsoleControl anzeigen
///  - Eingaben (Prompt/ReadLine/PromptForChoice) an eine blockierende Queue,
///    die das ConsoleControl aus seiner Input-Box füttert.
///
/// Damit funktionieren ALLE interaktiven Modul-Funktionen (Restore-Snapshot,
/// Remove-Snapshot, Confirm-Action, etc.).
/// </summary>
public sealed class DtmPSHost : PSHost
{
    private readonly DtmPSHostUI _ui;

    public DtmPSHost(DtmPSHostUI ui) => _ui = ui;

    public override CultureInfo CurrentCulture => CultureInfo.CurrentCulture;
    public override CultureInfo CurrentUICulture => CultureInfo.CurrentUICulture;
    public override Guid InstanceId { get; } = Guid.NewGuid();
    public override string Name => "DTM-PowerShellHost";
    public override PSHostUserInterface UI => _ui;
    public override Version Version => new(1, 0);

    public override void EnterNestedPrompt() { }
    public override void ExitNestedPrompt() { }
    public override void NotifyBeginApplication() { }
    public override void NotifyEndApplication() { }
    public override void SetShouldExit(int exitCode) { }
}

/// <summary>
/// UI-Teil des DTM-Hosts. Output geht an <see cref="OnOutput"/>/<see cref="OnError"/>;
/// Eingaben werden über <see cref="ProvideInput"/> aus der Console-Input-Box
/// eingespeist. <see cref="ReadLine"/>/<see cref="Prompt"/> blockieren, bis
/// eine Eingabe vorliegt oder die Session abgebaut wird.
/// </summary>
public sealed class DtmPSHostUI : PSHostUserInterface
{
    /// <summary>Normaler Host-Output (Write-Host etc. läuft aber separat über Streams).</summary>
    public Action<string>? OnOutput { get; set; }
    public Action<string>? OnError { get; set; }
    /// <summary>Wird vor dem Warten auf Eingabe gerufen, um den Prompt anzuzeigen.</summary>
    public Action<string>? OnPrompt { get; set; }

    private readonly object _gate = new();
    private readonly Queue<string> _pending = new();
    private bool _disposed;

    public override PSHostRawUserInterface RawUI => _raw;
    private readonly DtmPSHostRawUI _raw = new();

    /// <summary>Liefert eine vom User getippte Zeile in die wartende Read-Operation.</summary>
    public void ProvideInput(string line)
    {
        lock (_gate)
        {
            _pending.Enqueue(line);
            System.Threading.Monitor.PulseAll(_gate);
        }
    }

    /// <summary>Bricht alle wartenden Reads ab (z.B. beim Session-Stop).</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _disposed = true;
            System.Threading.Monitor.PulseAll(_gate);
        }
    }

    private string WaitForLine()
    {
        lock (_gate)
        {
            while (_pending.Count == 0 && !_disposed)
                System.Threading.Monitor.Wait(_gate);
            if (_disposed) return string.Empty;
            return _pending.Dequeue();
        }
    }

    public override string ReadLine()
    {
        return WaitForLine();
    }

    public override SecureString ReadLineAsSecureString()
    {
        string line = WaitForLine();
        var ss = new SecureString();
        foreach (char c in line) ss.AppendChar(c);
        ss.MakeReadOnly();
        return ss;
    }

    public override void Write(string value) => OnOutput?.Invoke(value);
    public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        => OnOutput?.Invoke(value);
    public override void WriteLine(string value) => OnOutput?.Invoke(value + "\n");
    public override void WriteErrorLine(string value) => OnError?.Invoke(value + "\n");
    public override void WriteDebugLine(string message) => OnOutput?.Invoke("DEBUG: " + message + "\n");
    public override void WriteVerboseLine(string message) => OnOutput?.Invoke("VERBOSE: " + message + "\n");
    public override void WriteWarningLine(string message) => OnOutput?.Invoke("WARNING: " + message + "\n");
    public override void WriteProgress(long sourceId, ProgressRecord record) { /* nicht angezeigt */ }

    /// <summary>
    /// Read-Host nutzt diese Methode. Jedes Feld wird als Prompt angezeigt,
    /// dann auf eine Eingabezeile gewartet.
    /// </summary>
    public override Dictionary<string, PSObject> Prompt(
        string caption, string message, Collection<FieldDescription> descriptions)
    {
        var result = new Dictionary<string, PSObject>();
        if (!string.IsNullOrEmpty(caption)) OnPrompt?.Invoke(caption);
        if (!string.IsNullOrEmpty(message)) OnPrompt?.Invoke(message);

        foreach (var d in descriptions)
        {
            string label = string.IsNullOrEmpty(d.Name) ? "Eingabe" : d.Name;
            OnPrompt?.Invoke($"{label}: ");
            string line = WaitForLine();
            result[d.Name] = PSObject.AsPSObject(line);
        }
        return result;
    }

    /// <summary>
    /// Confirm-Action im Modul nutzt zwar Read-Host (also Prompt), aber echte
    /// PromptForChoice-Aufrufe (z.B. ShouldProcess) landen hier. Wir zeigen die
    /// Optionen an und interpretieren die getippte Eingabe.
    /// </summary>
    public override int PromptForChoice(
        string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
    {
        if (!string.IsNullOrEmpty(caption)) OnPrompt?.Invoke(caption + "\n");
        if (!string.IsNullOrEmpty(message)) OnPrompt?.Invoke(message + "\n");

        var sb = new StringBuilder();
        for (int i = 0; i < choices.Count; i++)
        {
            string label = choices[i].Label.Replace("&", "");
            sb.Append($"[{i}] {label}  ");
        }
        sb.Append($"(Default={defaultChoice}): ");
        OnPrompt?.Invoke(sb.ToString());

        string line = WaitForLine().Trim();
        if (string.IsNullOrEmpty(line)) return defaultChoice;

        // Erst numerisch versuchen, dann gegen die Labels matchen.
        if (int.TryParse(line, out int idx) && idx >= 0 && idx < choices.Count)
            return idx;

        for (int i = 0; i < choices.Count; i++)
        {
            string label = choices[i].Label.Replace("&", "");
            if (string.Equals(label, line, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return defaultChoice;
    }

    public override PSCredential PromptForCredential(
        string caption, string message, string userName, string targetName)
        => PromptForCredential(caption, message, userName, targetName,
            PSCredentialTypes.Default, PSCredentialUIOptions.Default);

    public override PSCredential PromptForCredential(
        string caption, string message, string userName, string targetName,
        PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
    {
        // Keine interaktive Credential-Eingabe im Tab — das Modul nutzt
        // credential.xml. Falls doch jemand Get-Credential ruft: leer.
        OnError?.Invoke("[Interaktive Credential-Eingabe wird im DTM-Tab nicht unterstützt — bitte credential.xml nutzen]\n");
        return null!;
    }
}

/// <summary>
/// Minimaler RawUI. Liefert eine feste Buffer-/Window-Größe, damit der
/// Format-Default (Tabellen) eine Breite kennt. Cursor/Key-Operationen sind
/// No-Ops, weil das scroll-basierte Output-Modell sie nicht braucht.
/// </summary>
public sealed class DtmPSHostRawUI : PSHostRawUserInterface
{
    public override ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;
    public override ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;
    public override Size BufferSize { get; set; } = new Size(200, 9999);
    public override Coordinates CursorPosition { get; set; } = new Coordinates(0, 0);
    public override int CursorSize { get; set; } = 1;
    public override Coordinates WindowPosition { get; set; } = new Coordinates(0, 0);
    public override Size WindowSize { get; set; } = new Size(200, 50);
    public override Size MaxPhysicalWindowSize => new Size(200, 50);
    public override Size MaxWindowSize => new Size(200, 50);
    public override string WindowTitle { get; set; } = "DTM";
    public override bool KeyAvailable => false;

    public override KeyInfo ReadKey(ReadKeyOptions options) => default;
    public override void FlushInputBuffer() { }
    public override BufferCell[,] GetBufferContents(Rectangle rectangle) => new BufferCell[0, 0];
    public override void SetBufferContents(Coordinates origin, BufferCell[,] contents) { }
    public override void SetBufferContents(Rectangle rectangle, BufferCell fill) { }
    public override void ScrollBufferContents(Rectangle source, Coordinates destination, Rectangle clip, BufferCell fill) { }
}
