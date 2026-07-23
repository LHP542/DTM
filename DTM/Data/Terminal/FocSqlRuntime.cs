using DTM.Config;

namespace DTM.Data.Terminal;

/// <summary>
/// Statische Brücke für die FOC-SQL-Modul-Konfiguration, analog zu
/// <see cref="SshRuntimeConfig"/>. Wird beim App-Start gesetzt.
/// </summary>
public static class FocSqlRuntime
{
    private static FocSqlConfig _config = new();

    public static FocSqlConfig Current
    {
        get => _config;
        set => _config = value ?? new FocSqlConfig();
    }

    /// <summary>
    /// Baut das PowerShell-Snippet, das das FOC-SQL-Modul bereitstellt.
    /// Spiegelt die Profil-Logik des Users: das Modul wird bei jedem Start
    /// stumpf von Samba in den User-PSModulePath kopiert (überschrieben) und
    /// frisch importiert – ohne Versions-/Vorhandenheitscheck, damit nie eine
    /// veraltete lokale Kopie "klebt".
    ///
    /// Wenn <see cref="FocSqlConfig.ModulePath"/> gesetzt ist, wird stattdessen
    /// direkt dieser Pfad importiert (Override für Tests/Spezialfälle).
    /// Sonst kommt die Samba-Quelle aus <see cref="FocSqlConfig.SambaSource"/>.
    /// </summary>
    public static string BuildImportSnippet()
    {
        string explicitPath = _config.ModulePath?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(explicitPath))
        {
            string escaped = explicitPath.Replace("'", "''");
            // Override: immer frisch importieren (vorher entladen), damit auch
            // hier keine alte Version hängenbleibt.
            return
                "if (Get-Module FOC-SQL) { Remove-Module FOC-SQL -Force -ErrorAction SilentlyContinue } ; " +
                $"Import-Module '{escaped}' -Force -DisableNameChecking";
        }

        string sambaGlob = (_config.SambaSource?.Trim() ?? DefaultSambaSource).Replace("'", "''");

        // Verhalten wie das PowerShell-Profil des Users: das Modul wird bei JEDEM
        // Start stumpf von Samba in den User-Modulpfad kopiert und überschrieben –
        // kein Versions-/Vorhandenheitscheck. So ist immer die aktuelle Modul-
        // Version aktiv (eine zwischengespeicherte alte Kopie kann nicht "kleben").
        //
        // Ablauf:
        //  1. Ziel = erstes Element des PSModulePath (User-Modulpfad).
        //  2. Evtl. bereits geladenes Modul entladen, damit die neue Kopie greift.
        //  3. Von Samba kopieren (-Force überschreibt; *_ToExport.ps1 ausgeschlossen).
        //  4. ENCODING-FIX: Skriptdateien ohne UTF-8-BOM, die Nicht-ASCII-Bytes
        //     (Umlaute) enthalten, werden von Windows-1252 nach UTF-8-mit-BOM
        //     konvertiert. Sonst liest PowerShell 7 (in-process) BOM-lose Dateien
        //     als UTF-8 und macht aus 'ä' (0xE4) das Ersatzzeichen '�' — was bei
        //     Pfaden wie "...\01 Täglich" zu "Pfad nicht gefunden" führt.
        //     (Windows PowerShell 5.1 / pwsh.exe liest sie korrekt als ANSI,
        //     daher tritt der Fehler nur im DTM-in-process-Runspace auf.)
        //  5. Modul frisch importieren.
        return string.Join(" ; ", new[]
        {
            "$__moduleName = 'FOC-SQL'",
            "$__dest = ($env:PSModulePath -split ';')[0]",
            "$__localDir = Join-Path $__dest $__moduleName",
            // try/catch als EIN Element (nicht über Semikola zerteilt), damit der
            // Block in jeder PowerShell-Version sauber bleibt.
            "try { " +
                "if (Get-Module $__moduleName) { Remove-Module $__moduleName -Force -ErrorAction SilentlyContinue } " +
                "if (-not (Test-Path $__dest)) { New-Item -ItemType Directory -Path $__dest -Force | Out-Null } " +
                $"Copy-Item -Path '{sambaGlob}' -Destination $__dest -Recurse -Force -Exclude *_ToExport.ps1 -ErrorAction Stop " +
            "} catch { Write-Error ('FOC-SQL Modul konnte nicht von Samba kopiert werden: ' + $_.Exception.Message) }",
            // Encoding-Normalisierung der frisch kopierten Skriptdateien.
            "try { " +
                "if (Test-Path $__localDir) { " +
                "  Get-ChildItem -Path $__localDir -Recurse -File -Include *.psm1,*.ps1,*.psd1 -ErrorAction SilentlyContinue | ForEach-Object { " +
                "    $__bytes = [System.IO.File]::ReadAllBytes($_.FullName) ; " +
                "    $__hasBom = ($__bytes.Length -ge 3 -and $__bytes[0] -eq 0xEF -and $__bytes[1] -eq 0xBB -and $__bytes[2] -eq 0xBF) ; " +
                "    $__hasHighByte = $false ; foreach ($__b in $__bytes) { if ($__b -ge 0x80) { $__hasHighByte = $true ; break } } ; " +
                "    if ((-not $__hasBom) -and $__hasHighByte) { " +
                "      $__text = [System.Text.Encoding]::GetEncoding(1252).GetString($__bytes) ; " +
                "      $__utf8Bom = New-Object System.Text.UTF8Encoding($true) ; " +
                "      [System.IO.File]::WriteAllText($_.FullName, $__text, $__utf8Bom) " +
                "    } " +
                "  } " +
                "} " +
            "} catch { Write-Error ('FOC-SQL Encoding-Normalisierung fehlgeschlagen: ' + $_.Exception.Message) }",
            "Import-Module $__moduleName -Force -DisableNameChecking -ErrorAction SilentlyContinue",
            "Remove-Variable __moduleName, __dest, __localDir, __bytes, __hasBom, __hasHighByte, __b, __text, __utf8Bom -ErrorAction SilentlyContinue"
        });
    }

    /// <summary>Default-Samba-Glob, falls in der Config nichts hinterlegt ist.</summary>
    private const string DefaultSambaSource =
        @"\\samba01\542$\5422_IT-Basis-Infrastruktur\MS-SQL\Powershell\Module\FOC*";

    /// <summary>
    /// Baut einen Aufruf einer FOC-SQL-Funktion mit -Database und entweder
    /// -Time/-Date (geplant) oder -Immediate (sofort, ohne interaktive Abfrage).
    /// </summary>
    /// <param name="functionName">z.B. "Backup-Database".</param>
    /// <param name="database">Datenbankname (MSSQL) bzw. FQDN (Oracle).</param>
    /// <param name="when">
    /// Geplanter Zeitpunkt oder null = sofort. Bei einem Wert wird
    /// -Time 'HH:mm' -Date 'dd.MM.yyyy' angehängt, bei null -Immediate.
    /// </param>
    public static string BuildCall(string functionName, string database, DateTime? when)
    {
        string dbEsc = database.Replace("'", "''");
        var sb = new System.Text.StringBuilder();
        sb.Append(functionName);
        sb.Append(" -Database '").Append(dbEsc).Append('\'');

        if (when is { } w)
        {
            sb.Append(" -Time '").Append(w.ToString("HH:mm")).Append('\'');
            sb.Append(" -Date '").Append(w.ToString("dd.MM.yyyy")).Append('\'');
        }
        else
        {
            // Sofort ausführen OHNE interaktive Read-Host-Abfrage im Modul.
            // (Ohne -Immediate würde Get-SchedTimeOrPrompt im Tab nach Zeit fragen.)
            sb.Append(" -Immediate");
        }

        return sb.ToString();
    }
}
