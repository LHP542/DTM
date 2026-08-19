namespace DTM.Data.Api;

/// <summary>
/// Entscheidet, ob ein per API angefragter Klick bzw. Command eine Datenbank
/// veraendern kann.
///
/// <para><b>Warum es das ueberhaupt gibt:</b> Bei einer Endanwender-App ist
/// eine Steuer-API harmlos. DTM loest dagegen Backups, Restores,
/// Snapshot-Drops und Session-Kills auf produktiven Datenbanken aus — ein
/// versehentlich (oder fremd) abgesetzter Request kann echten Schaden
/// anrichten. Deshalb ist die API standardmaessig ein
/// <b>Beobachtungs- und Navigationskanal</b>: ansehen, durchklicken,
/// Screenshots — aber nichts, was Daten anfasst.</para>
///
/// <para>Freischalten mit <c>Api.AllowDestructive</c> in der settings.json
/// oder <c>--api-allow-destructive</c> auf der Kommandozeile.</para>
///
/// <para>Die Liste ist bewusst eine <b>Sperrliste ueber Namen</b> und keine
/// Analyse dessen, was ein Command tatsaechlich tut: sie ist damit lesbar und
/// pruefbar. Preis ist Pflegeaufwand — <b>jede neue Aktion, die schreibend auf
/// eine Datenbank geht, muss hier eingetragen werden</b>. Ein Test haelt die
/// Liste gegen die Commands des MainWindowViewModel gegen, damit ein
/// vergessener Eintrag auffaellt.</para>
/// </summary>
public static class DestructiveGuard
{
    /// <summary>
    /// Commands am <c>MainWindowViewModel</c>, die schreibend wirken.
    /// Namen OHNE das vom Toolkit angehaengte "Command"-Suffix.
    /// </summary>
    private static readonly HashSet<string> DestructiveCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        // Sicherung: schreiben ins Dateisystem bzw. auf den Zielserver
        "Backup", "Clone", "DbToSamba",
        // Snapshots: erzeugen, ueberschreiben, loeschen
        "Snapshot", "RestoreSnapshot", "RemoveSnapshot",
        "OlvmSnapshot", "OlvmRestoreSnapshot", "OlvmRemoveSnapshot",
        // Recovery-/Archivelog-Umschaltung bricht ggf. die Log-Chain
        "ArchiveLogOn", "ArchiveLogOff",
        // Wartung: CHECKDB ist lesend, Rebuild und Shrink sind es nicht
        "RunIndexRebuild", "RunShrinkLog",
    };

    /// <summary>
    /// Benannte Controls in Dialogen, die eine destruktive Aktion bestaetigen.
    /// Ohne diese Sperre koennte die API den Confirm-Dialog wegklicken, den die
    /// Sperre oben gerade erzwungen hat.
    /// </summary>
    private static readonly HashSet<string> DestructiveElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConfirmButton",      // ConfirmWindow — bestaetigt genau die Aktionen von oben
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

    /// <summary>Fuer Tests und den <c>/state</c>-Endpoint: die Sperrliste.</summary>
    public static IReadOnlyCollection<string> KnownDestructiveCommands => DestructiveCommands;

    public static IReadOnlyCollection<string> KnownDestructiveElements => DestructiveElements;
}
