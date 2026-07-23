namespace DTM.ViewModels;

/// <summary>
/// Tristate-Ergebnis des Zeitwahl-Dialogs:
///  - Cancelled = true        → Benutzer hat abgebrochen, keine Aktion.
///  - Cancelled = false, When = null → "Sofort" ausführen.
///  - Cancelled = false, When = [datetime] → geplante Zeit.
/// </summary>
public readonly record struct TimePickResult(bool Cancelled, DateTime? When)
{
    public static TimePickResult Cancel() => new(true, null);
    public static TimePickResult Immediate() => new(false, null);
    public static TimePickResult At(DateTime when) => new(false, when);
}
