namespace DTM.Updater;

/// <summary>
/// Eintrag aus <c>release-notes.json</c> auf der Update-Quelle. Wird im
/// UpdatePromptWindow als Versionshistorie zwischen aktueller und neuer
/// Version angezeigt (oeffnet sich beim Update-Check).
///
/// ModulesChanged enthaelt Tags wie "FOC-SQL" oder "MSSQL". FOC-SQL wird
/// vom DTM-Update automatisch nachgezogen (gruener Banner), MSSQL liegt
/// auf jedem Server in einem User-Profil — es muss dort manuell aktualisiert
/// werden (roter Banner, deutlicher Warnhinweis).
/// </summary>
public sealed class ReleaseNote
{
    public string Version { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = new();
    public List<string> ModulesChanged { get; set; } = new();
}
