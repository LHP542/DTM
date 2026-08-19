# DTM — Datenbank-Manager

[![CI](https://github.com/Kroste/DTM/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/DTM/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/DTM)](https://github.com/Kroste/DTM/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Avalonia-Desktop-App (.NET 10) zur Verwaltung von MSSQL- und Oracle-Datenbanken
(Backup, Clone, Snapshot, Archive-Log, Samba-Copy). Alle Datenbank-Aktionen
laufen über das PowerShell-Modul **FOC-SQL.psm1**; DTM baut kein eigenes
Remoting nach, sondern ruft die Modulfunktionen in einer eingebetteten
PowerShell-Session auf.

Entwickelt von **Lars Oste** · Landeshauptstadt Potsdam · Fachbereich 54.2

![DTM Hauptfenster](docs/screenshot-main.png)

---

## Installation

Fertige Pakete gibt es auf der [Releases-Seite](https://github.com/Kroste/DTM/releases):

**Windows:** `DTM-vX.Y.Z-windows.zip` herunterladen, entpacken, `DTM.exe`
starten. Keine Installation nötig (self-contained, .NET-Runtime ist enthalten).

**Linux (AppImage, empfohlen):** `DTM-vX.Y.Z-x86_64.AppImage` herunterladen,
ausführbar machen und starten:

```bash
chmod +x DTM-*-x86_64.AppImage
./DTM-*-x86_64.AppImage
```

**Linux (tar.gz):** `DTM-vX.Y.Z-linux.tar.gz` entpacken und `./DTM` starten.

### Voraussetzungen

- **Windows** für die Modul-Aktionen (Backup/Snapshot etc.): die FOC-SQL-
  Funktionen brauchen die Windows-/Domänen-Umgebung. Die App selbst läuft auch
  unter Linux — dort ist das Feature-Set aber auf reine Read-Operationen
  begrenzt.
- Eine `credential.xml` im Benutzerprofil:
  ```powershell
  Get-Credential | Export-Clixml "$env:USERPROFILE\credential.xml"
  ```
- Das FOC-SQL-Modul unter der konfigurierten Samba-Quelle.

---

## Erste Schritte

1. App starten, Verbindungen über das ⚙-Symbol neben „Datenbanken" einrichten.
2. Im selben Dialog Samba-Quelle für FOC-SQL und optional den Modulpfad-Override
   eintragen und **Speichern** klicken.

---

## Verbindungen verwalten

Das ⚙-Symbol neben der „Datenbanken"-Überschrift öffnet den Dialog
**Verbindungen verwalten**.

![Verbindungen verwalten](docs/screenshot-connections.png)

| Feld | Bedeutung |
|------|-----------|
| Typ | Datenbanktyp (`MSSQL`, `ORACLE`) — DropDown |
| Server | Hostname oder IP des Datenbankservers |
| Benutzer | DB-Benutzername |
| Passwort | Wird verschlüsselt gespeichert (DPAPI unter Windows, Base64 unter Linux) |
| Datenbank | Standard-Datenbankname |
| ConnectionString | Optionaler ODBC-ConnectionString; überschreibt Server/User/Passwort |

Bei MSSQL zusätzlich (**PS-Remoting-Credentials**, wenn abweichend von der
Default-`credential.xml`):

| Feld | Bedeutung |
|------|-----------|
| RemoteUser | Windows-User für WinRM-Aufrufe (leer = Fallback auf `credential.xml`) |
| RemotePassword | DPAPI-verschlüsselt in `connections.json` |

Und **Backend** (nur MSSQL): `FocSql` (Default, WinRM-Aufruf ans FOC-SQL-Modul)
oder `OdbcDirect` (direkte SQL-Ausführung via ODBC — für DMZ-Server ohne WinRM,
15 der 17 Aktionen verfügbar; Copy-to-Samba und Sync-to-Test sind File-System-
Operationen und deaktiviert).

Aktionen: **Neu**, **Bearbeiten** (Doppelklick oder Schaltfläche), **Löschen**.
Änderungen werden sofort in `%APPDATA%\DTM\connections.json` persistiert.

Unter **FOC-SQL Modul** im gleichen Dialog:

| Feld | Bedeutung |
|------|-----------|
| Samba-Quelle | UNC-Pfad mit `FOC-SQL.psm1` (z. B. `\\server\share\Modules\FOC`) |
| Modulpfad (Override) | Absoluter lokaler Pfad; leer = Samba-Logik aktiv |

---

## Auto-Update

DTM prüft beim Start (einmalig pro App-Start, im Hintergrund) gegen die
[GitHub-Releases-Seite](https://github.com/Kroste/DTM/releases), ob eine neuere
Version verfügbar ist. Ein manueller Check ist jederzeit über **ℹ → Auf Updates
prüfen** in der About-Box möglich (umgeht den Cache).

### Ablauf

1. DTM ruft `api.github.com/repos/Kroste/DTM/releases/latest` auf (proxy-fähig
   via `WebRequest.DefaultWebProxy` + `CredentialCache.DefaultCredentials`).
2. Ist die veröffentlichte Version größer als die laufende (`Assembly­Informational­Version`),
   erscheint der Update-Dialog mit den Release Notes zwischen aktueller und
   Zielversion (aus `raw.githubusercontent.com/Kroste/DTM/main/release-notes.json`):

   | Option | Verhalten |
   |--------|-----------|
   | **Jetzt aktualisieren** | Lädt das passende Plattform-Asset (Windows-ZIP / Linux-tar.gz / AppImage), zeigt einen Fortschrittsbalken und startet ein Skript, das nach dem Beenden von DTM die Dateien austauscht und die App neu startet. |
   | **Später** | Erinnerung beim nächsten App-Start. |
   | **Überspringen** | Kein weiterer Hinweis in dieser Sitzung. |

Ist die `release-notes.json` mit einem `"modulesChanged"`-Eintrag markiert
(`"MSSQL"` bzw. `"FOC-SQL"`), zeigt der Dialog einen roten Banner
(„MSSQL-Modul wurde geändert — jeder Server braucht einmal eine
PowerShell-Sitzung") bzw. einen grünen Hinweis (FOC-SQL sync't automatisch
beim nächsten Start).

### GitHub Actions

Bei einem Git-Tag (`v*`) läuft `.github/workflows/release.yml`:

- Tests auf Ubuntu
- Self-contained Builds für `win-x64` (`.zip`), `linux-x64` (`.tar.gz`) und
  `linux-x64` **AppImage**
- GitHub Release mit automatischen Release-Notes und allen drei Assets

---

## Datenbank-Übersicht (Info-Card)

Nach Auswahl einer Datenbank zeigt die Info-Card oben Host, Status, Compatibility-
Level bzw. Oracle-Version, den Recovery-/ArchiveLog-Modus und die **Größe**.

Die **Größe** ist die allokierte Gesamtgröße der Datenbank – passend zur Anzeige
in SQL Server Management Studio bzw. den Oracle-Dictionary-Views:

- **MSSQL:** Datendateien **+** Transaktionslog (entspricht der SSMS-Eigenschaft
  „Größe"). Weicht der Wert stark vom reinen Datenbestand ab, ist meist ein
  aufgeblähtes Log die Ursache (FULL-Recovery ohne Log-Backup) – dann hilft
  „Log Aus" bzw. „Shrink Log".
- **Oracle:** Summe aus `dba_data_files` **+** `dba_temp_files` (inkl. TEMP-
  Tablespaces).

---

## Aktionen

| Button | Modulfunktion | Zeitplanung | Interaktiv |
|--------|---------------|-------------|------------|
| Backup           | `Backup-Database`         | ja  | – |
| Clone            | `Sync-Database-ToTest`    | ja  | – |
| DB → Samba       | `Copy-Database-ToSamba`   | –   | – |
| Snapshot         | `Set-Snapshot`            | ja  | – |
| Restore          | `Restore-Snapshot`        | –   | Oracle: Vorab-Dialog mit Restore-Points + PDB-Liste; MSSQL: pwsh-Prompt |
| Remove           | `Remove-Snapshot`         | –   | ja |
| ArchiveLog An    | `Set-Archive-Log`         | –   | – |
| ArchiveLog Aus   | `Set-Archive-Log -Off`    | –   | – |
| Cluster-Health   | `Get-ClusterHealthStatus` | –   | – (MSSQL-only, read-only Status im Info-Card) |
| VM-Snapshot / VM-Restore / VM-Remove | Ansible/OLVM-REST | – | Oracle-only, VM-Snapshot via Ansible-Playbook, Restore/Remove aktuell disabled bis Playbooks fertig |

Zeitplanung: Im Zeit-Dialog „Sofort" oder „Geplant" (Datum/Uhrzeit) wählen.
Interaktive Aktionen (Restore/Remove) zeigen Prompts im pwsh-Tab;
Antworten (Nummer, `ja`/`j`) in die Befehlszeile tippen.

**ArchiveLog-Buttons** togglen je nach DB-Typ unterschiedlich:
- **Oracle**: echter `ARCHIVELOG ON/OFF`.
- **MSSQL**: `Recovery FULL/SIMPLE` (`Set-Archive-Log` dispatched im Modul nach
  DB-Typ — siehe `CLAUDE.md` „Akzeptierte Abweichungen").

Die Buttons spiegeln den aktuellen Modus: ist „ON"/`FULL` aktiv, ist „Log An"
deaktiviert und „Log Aus" klickbar — und umgekehrt. Nach einem Klick
aktualisieren sich die Stats automatisch nach ca. 8 Sekunden.

**Oracle-Restore-Vorschau:** Bei Oracle öffnet sich vor `Restore-Snapshot` ein
Dialog mit den verfügbaren Restore Points und der PDB-Liste der CDB. Bei
Multi-PDB-Konfiguration wird prominent gewarnt — `Restore-Snapshot` fährt die
gesamte CDB herunter und setzt sie auf den gewählten Restore Point zurück
(alle PDBs sind betroffen, nicht nur die ausgewählte).

![Oracle Restore-Vorschau](docs/screenshot-oracle-restore.png)

---

## Benutzeroberfläche

- **Titelleiste** — eigene Titelleiste ohne nativen OS-Rahmen
  (`ChromeWindow`-Basisklasse: `WindowDecorations.BorderOnly`,
  `ExtendClientAreaToDecorationsHint = true`, `CanResize = true`).
  - **ℹ** öffnet die About-Box (Version, Entwickler, Update-Check).
  - **−** minimiert, **⊡/❐** maximiert/restauriert, **✕** schließt.
- Alle Dialoge verwenden denselben Style (draggable Titelleiste, nur Schließen-Button).
- **System-Tray** — Minimieren legt DTM in den Tray, statt es zu beenden: die
  PowerShell-Sitzung und offene Datenbankverbindungen bleiben bestehen. Zurück
  über einen Klick aufs Tray-Icon oder „Anzeigen" im Kontextmenü. **✕** beendet
  regulär.
- **Nur eine Instanz** — startest du DTM ein zweites Mal, wird stattdessen das
  bereits laufende Fenster nach vorn geholt (auch aus dem Tray heraus). Das
  verhindert, dass zwei Prozesse gleichzeitig auf `connections.json` schreiben
  und sich die Änderungen gegenseitig überschreiben.

---

## Datenspeicherung

| Datei | Inhalt |
|-------|--------|
| `%APPDATA%\DTM\connections.json` | Verbindungsliste (Passwörter und optionale PS-Remoting-Credentials DPAPI-verschlüsselt) |
| `%APPDATA%\DTM\settings.json` | FocSql-Einstellungen (SambaSource, ModulePath) |

Beide Dateien werden beim ersten Speichern automatisch angelegt.

Geschrieben wird immer atomar (erst `.tmp`, dann Umbenennen), damit ein Absturz
mitten im Speichern keine halbe Datei hinterlässt. Ist eine der Dateien beim
Start trotzdem nicht lesbar, wird sie als `<name>.json.broken` beiseitegelegt
und DTM startet mit leerem Bestand — die alte Datei bleibt damit für eine
Rettung von Hand erhalten. Der Vorfall steht mit Begründung im Error-Log.

---

## REST-API (optional, standardmäßig aus)

DTM kann eine kleine HTTP-API im eigenen Prozess mitlaufen lassen, über die
sich der Zustand auslesen, durch den Baum navigieren und ein Bildschirmfoto
des Fensters abrufen lässt. Gedacht ist sie für automatisierte Prüfungen der
Oberfläche — Screenshots entstehen über Avalonias eigenes Rendering, es wird
nichts von außen ferngesteuert.

**Sie ist standardmäßig aus und muss bewusst eingeschaltet werden:**

```bash
DTM.exe --api-port 8765 --api-token <geheim> --auto-shutdown-after 10m
```

Oder dauerhaft in `%APPDATA%\DTM\settings.json`:

```json
{ "Api": { "Enabled": true, "Port": 8765, "BearerToken": "<geheim>" } }
```

Jeder Aufruf braucht `Authorization: Bearer <token>`. Ohne gesetztes Token
antwortet die API auf **jede** Anfrage mit `403` — eine offene Steuerschnittstelle
auf einem Rechner mit Datenbankzugängen soll es nicht versehentlich geben.
Gelauscht wird ausschließlich auf `127.0.0.1`; für den Zugriff von einem anderen
Rechner ist ein SSH-Tunnel der vorgesehene Weg.

| Endpunkt | Zweck |
|---|---|
| `GET /state` | Ausgewählter Knoten, Statuszeile, Kennzahlen der Datenbank, offene Fenster |
| `GET /tree` | Server- und Datenbankbaum (`databasesLoaded` zeigt, ob schon geladen wurde) |
| `GET /elements` | Namen aller ansprechbaren Bedienelemente |
| `POST /select-node` | `{ "path": "SRV" }` oder `{ "path": "SRV/DATENBANK" }` |
| `POST /command` | `{ "name": "ManageConnections" }` — Command des Hauptfensters |
| `POST /click` | `{ "elementId": "CancelButton" }` |
| `POST /text` | `{ "elementId": "…", "text": "…" }` |
| `POST /screenshot` | PNG des Fensters (`?target=active`, `?format=json` für Base64) |

**Was die API nicht darf.** Aktionen, die Datenbanken verändern — Backup,
Clone, Snapshot anlegen/zurückspielen/löschen, Index-Rebuild, Shrink-Log,
Archive-Log umschalten und das Bestätigen solcher Dialoge — sind gesperrt und
liefern `403`. Freischalten nur bewusst, per `--api-allow-destructive` oder
`"AllowDestructive": true`. Ohne diese Sperre könnte ein einzelner
HTTP-Aufruf eine produktive Datenbank überschreiben.

Der Datenbankbaum lädt seine Einträge erst beim Auswählen eines Servers:
erst `/select-node` mit dem Servernamen, dann mit dem vollen Pfad.

---

## Logs & Fehlersuche

DTM verwendet **NLog**. Die Log-Dateien liegen neben der Anwendung unter `logs/`:

| Datei | Inhalt |
|-------|--------|
| `logs/info.log` | Debug- und Info-Meldungen (Verbindungsaufbau, DB-Ladevorgänge, Aktionen) |
| `logs/error.log` | Warnungen und Fehler |
| `logs/powershell.log` | Gesamte PS-Terminal-Ausgabe (Ein-/Ausgaben, Fehler, Job-Header); tägliche Archivierung, 7 Tage Aufbewahrung |

**Passwörter, Tokens und Credentials werden automatisch maskiert** — der
`${masked}`-Layout-Renderer greift auf `Password=`/`PWD=` in ConnectionStrings,
URL-Query-Params (`password=`/`token=`/`api_key=`), `Bearer`-Tokens und
`Authorization`-Header. Ergebnis im Log: `Password=***` statt Klartext.

Bei einem Problem bitte ein Issue mit der aktuellen Logdatei eröffnen.

---

## Entwicklung

```bash
# Klone (inkl. Dev-Submodul FOC-SQL unter external/):
git clone --recurse-submodules https://github.com/Kroste/DTM.git
# oder, falls schon geklont:
git submodule update --init external/FOC-SQL

# Bauen und Tests (VSCode-Task "build" / "test" ruft dasselbe):
dotnet build DTM.slnx -c Debug
dotnet test  DTM.Tests/DTM.Tests.csproj

# Starten (VSCode-Task "DTM ausfuehren" umgeht das coreclr-Problem
# auf Code-OSS/Codium):
dotnet run --project DTM/DTM.csproj
```

Release: VSCode-Task **„release (tag + push)"** — prüft den Git-Zustand, setzt
den `vX.Y.Z`-Tag und stößt die GitHub-Action an, die alle Pakete baut.

Das Submodul unter `external/FOC-SQL/` ist eine reine **Entwicklungs-Referenz**
auf den FOC-SQL-Quellcode. Die App lädt FOC-SQL zur Laufzeit weiterhin über die
in den Einstellungen konfigurierte Samba-Quelle bzw. den Modulpfad-Override.

### Architektur (Kurzüberblick)

- **Views/** — Avalonia-UI (alle Fenster über `ChromeWindow`-Basisklasse).
  - `MainWindow` — DB-Baum, Info-Anzeige, Aktions-Buttons, PowerShell-Konsole.
  - `ConnectionManagerWindow` / `EditConnectionWindow` — Verbindungsverwaltung.
  - `TimePickerWindow` — Zeitplanung für Backup/Clone/Snapshot.
  - `SessionsWindow` — Anzeige aktiver DB-Sessions.
  - `UpdatePromptWindow` — Update-Dialog (Jetzt / Später / Überspringen).
  - `AboutWindow` — Versionsinfo, Entwickler, manueller Update-Check.
  - `OracleRestoreSelectWindow`, `MssqlSnapshotSelectWindow`, `OlvmSnapshotSelectWindow`
    — Auswahl-Dialoge für destruktive Aktionen mit Bestätigung.
- **ViewModels/** — MVVM (CommunityToolkit.Mvvm).
  - `MainWindowViewModel` — Aktionen, Statistik-Anzeige, Baum-Aufbau, Auto-Update.
  - `ConnectionManagerViewModel` — Verbindungsliste, FocSql-Einstellungen.
  - `EditConnectionViewModel` — Formular für eine einzelne Verbindung.
- **Data/Config/**
  - `ConnectionStore` — `connections.json` (DPAPI/Base64-Passwortschutz).
  - `AppSettingsStore` — `settings.json`.
  - `FocSqlRuntime` — Laufzeit-Zustand der FocSql-Konfiguration.
- **Data/Updater/**
  - `UpdateService` — Klemmbrett-Pattern: `HttpClient` gegen GitHub-Releases-API,
    Cache pro App-Start, `forceRefresh` für manuellen Check, Cross-Platform-
    Self-Update via `.bat` (Windows) / `.sh` (Linux, inkl. AppImage inplace-cp).
- **Data/Terminal/**
  - `PowerShellTerminalSession` — in-process Runspace mit `DtmPSHost`/`DtmPSHostUI`.
  - `TerminalBus` — Mediator zwischen ViewModel-Aktionen und Session.
  - `AnsiParser` / `AnsiPalette` / `AnsiConsole` — farbige Ausgabe.
- **Data/HelperClasses/**
  - ODBC-Zugriff für DB-Liste und Statistik (MSSQL/Oracle).
  - `LogMask` — maskiert Passwörter in Connection-Strings vor dem Logging
    (ergänzt den globalen `${masked}`-Renderer als Ad-hoc-Schutz).
  - `ORACLE_REST` — oVirt/OLVM REST-API für VM-FQDNs und -Snapshots.
  - `OdbcMssqlActionService` — direkte ODBC-Ausführung für DMZ-Server ohne WinRM
    (Backend `OdbcDirect`).
- **Diagnostics/**
  - `MaskingLayoutRenderer` — `${masked}`-LayoutRenderer für NLog (Regex-basiert,
    per `[ModuleInitializer]` registriert).
  - `FatalErrorHandler` — globaler Handler für UnhandledException,
    UnobservedTaskException und Dispatcher-Fehler.

### Tests

```bash
dotnet test DTM.Tests/DTM.Tests.csproj
```

Die Test-Suite (~361 Tests, xUnit.v3 + FluentAssertions 7.x) deckt ab:

- `Data/Config/` — ConnectionStore, AppSettingsStore, ConnectionEntry
- `Data/Terminal/` — AnsiParser, AnsiPalette, FocSqlRuntime, TerminalBus, DtmPSHostUI
- `Data/HelperClasses/` — ServerCredential, DB_SERVER, Database_Info, Database_Stats-Varianten
- `Data/` — DTM_DATA (Routing via FakeFactory), AsyncUtil, OdbcMssqlActionService-Validierung
- `Data/Updater/` — UpdateService (Version-Parser)
- `Diagnostics/` — MaskingLayoutRenderer (Secret-Regex)
- `ViewModels/` — MainWindowViewModel, ConnectionManagerViewModel, EditConnectionViewModel,
  SessionsViewModel, TimePickerViewModel, TreeNode-ViewModels

Keine Abhängigkeit auf DB-Server, Avalonia-UI-Thread oder PowerShell-Runspace.

---

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ Gefällt dir das Tool? [Buy me a coffee](https://buymeacoffee.com/kroste)
