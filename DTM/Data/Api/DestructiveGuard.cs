namespace DTM.Data.Api;

/// <summary>
/// Entscheidet, ob ein per API angefragter Klick bzw. Command eine Datenbank
/// verändern kann.
///
/// <para><b>Warum es das überhaupt gibt:</b> Bei einer Endanwender-App ist
/// eine Steuer-API harmlos. DTM löst dagegen Backups, Restores,
/// Snapshot-Drops und Session-Kills auf produktiven Datenbanken aus — ein
/// versehentlich (oder fremd) abgesetzter Request kann echten Schaden
/// anrichten. Deshalb ist die API standardmäßig ein
/// <b>Beobachtungs- und Navigationskanal</b>: ansehen, durchklicken,
/// Screenshots — aber nichts, was Daten anfasst.</para>
///
/// <para>Freischalten mit <c>Api.AllowDestructive</c> in der settings.json
/// oder <c>--api-allow-destructive</c> auf der Kommandozeile.</para>
///
/// <para>Die Liste ist bewusst eine <b>Sperrliste über Namen</b> und keine
/// Analyse dessen, was ein Command tatsächlich tut: sie ist damit lesbar und
/// prüfbar. Preis ist Pflegeaufwand — <b>jede neue Aktion, die schreibend auf
/// eine Datenbank geht, muss hier eingetragen werden</b>. Ein Test hält die
/// Liste gegen die Commands des MainWindowViewModel gegen, damit ein
/// vergessener Eintrag auffällt.</para>
/// </summary>
public static class DestructiveGuard
{
    /// <summary>
    /// Commands am <c>MainWindowViewModel</c>, die schreibend wirken.
    /// Namen OHNE das vom Toolkit angehängte "Command"-Suffix.
    /// </summary>
    private static readonly HashSet<string> DestructiveCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // Sicherung: schreiben ins Dateisystem bzw. auf den Zielserver
        "Backup", "Clone", "DbToSamba",
        // Snapshots: erzeugen, überschreiben, löschen
        "Snapshot", "RestoreSnapshot", "RemoveSnapshot",
        "OlvmSnapshot", "OlvmRestoreSnapshot", "OlvmRemoveSnapshot",
        // Recovery-/Archivelog-Umschaltung bricht ggf. die Log-Chain
        "ArchiveLogOn", "ArchiveLogOff",
        // Wartung: CHECKDB ist lesend, Rebuild und Shrink sind es nicht
        "RunIndexRebuild", "RunShrinkLog",
        // MariaDB: OPTIMIZE schreibt jede Tabelle neu und sperrt sie dabei.
        // CHECK ist rein lesend und ANALYZE berührt nur die Optimizer-
        // Statistiken — beide gelten als unkritisch.
        "MariaDbOptimizeTables",
    };

    /// <summary>
    /// Benannte Controls in Dialogen, die eine destruktive Aktion bestätigen.
    /// Ohne diese Sperre könnte die API den Confirm-Dialog wegklicken, den die
    /// Sperre oben gerade erzwungen hat.
    /// </summary>
    private static readonly HashSet<string> DestructiveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConfirmButton",      // ConfirmWindow — bestätigt genau die Aktionen von oben
        "RestoreButton",      // BackupBrowserWindow / OracleRestoreSelectWindow
        "CloseSessionsButton",// SessionsWindow — beendet alle Sessions per KILL
    };

    /// <summary>Command-Name (mit oder ohne "Command"-Suffix) gesperrt?</summary>
    public static bool IsDestructiveCommand(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        string normalized = commandName.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
            ? commandName[..^"Command".Length]
            : commandName;
        return DestructiveCommands.Contains(normalized);
    }

    /// <summary>Benanntes Control gesperrt?</summary>
    public static bool IsDestructiveElement(string elementName) =>
        !string.IsNullOrWhiteSpace(elementName) && DestructiveElements.Contains(elementName);

    /// <summary>Für Tests und den <c>/state</c>-Endpoint: die Sperrliste.</summary>
    public static IReadOnlyCollection<string> KnownDestructiveCommands => DestructiveCommands;

    public static IReadOnlyCollection<string> KnownDestructiveElements => DestructiveElements;
}
