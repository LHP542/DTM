# CLAUDE.md

> Diese Datei wird von Claude Code / Copilot beim Session-Start als Kontext geladen.
> Sie hält den **projektübergreifenden Standard-Kanon** fest, damit die Konventionen
> unabhängig vom Chat-Memory im Repo verfügbar sind.
>
> **Master-Vorlage** – pro Projekt nur den Abschnitt *„Projekt“* ausfüllen, der Rest bleibt fix.

---

## Arbeitsweise

**Deal:** Lars liefert die Ideen, Claude setzt um.

- Bei **jedem neuen Projekt** wird diese `CLAUDE.md` automatisch im Repo-Root angelegt.
- Sprache: **Deutsch**, immer **„du“**, nie „Sie“.
- Antwortstil: direkt, technisch tief, klare Single-Path-Empfehlung mit Begründung,
  sinnvolle Code-Erklärungen (keine Basics), Folgefragen vorausschauend mitdenken.

---

## Projekt

- **Name:** `DTM`
- **Kurzbeschreibung:** Avalonia-Desktop-App zur PowerShell-gestützten Administration von MSSQL- und Oracle-Datenbanken (Backup, Clone, Snapshot, Archive-Log, Samba-Copy) über das Modul `FOC-SQL.psm1` in einer in-process PowerShell-Session.
- **Repository:** `https://github.com/Kroste/DTM`
- **Lokaler Pfad:** `~/Entwicklung/DTM` (Linux) bzw. `D:\Entwicklung\DTM` (Windows)
- **Projektspezifische Besonderheiten:** Embedded PowerShell-Runspace via `Microsoft.PowerShell.SDK`; externes Update-Skript `dtm_update.ps1`; keine KI-Integration; Logo der Landeshauptstadt Potsdam (`Assets/lhp_logo.png`).

---

## Tech-Stack (Baseline)

- **.NET 10** / **C#** (LangVersion `latest`, `ImplicitUsings`, `Nullable enable`)
- Desktop-UI: **Avalonia ≥ 12.0.4** (Mindestversion, niemals darunter)
- MVVM: **CommunityToolkit.Mvvm**
- DI/Hosting: **Microsoft.Extensions.DependencyInjection** + Hosting
- Logging: **NLog**
- GitHub-Account: **Kroste** (`lars-oste@gmx.de`)
- **Referenz-Vorlage für KI-Apps: Allpaca** (Provider-Abstraktion, Settings-UI,
  Ollama-Modell-Download, AppImage-Packaging).

---

## Repo-Struktur & Tooling (Pflicht bei jedem Projekt)

### `Directory.Build.props` (Repo-Root)

Zentrale Metadaten, damit nichts pro csproj wiederholt wird:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Authors>Lars Oste</Authors>
    <RepositoryUrl>https://github.com/Kroste/$(MSBuildProjectName)</RepositoryUrl>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

### NuGet-Pakete (Central Package Management)

Alle Paketversionen werden zentral in **`Directory.Packages.props`** (Repo-Root)
gepflegt; die `.csproj` enthalten `<PackageReference>` **ohne** `Version`-Attribut.
Beim Hinzufügen eines Pakets: Eintrag in `Directory.Packages.props` ergänzen.
**FluentAssertions bleibt auf 7.x** (Range `[7.x,8.0.0)` + Dependabot-Ignore):
ab v8 gilt die Xceed-Lizenz, kommerzielle Nutzung ist kostenpflichtig.

### Versionierung via MinVer

- Version kommt aus dem **Git-Tag** (`v1.4.0` → Assembly `1.4.0`), **kein** manuelles
  Hochzählen von `<Version>` in der csproj.
- Tag `vX.Y.Z` koppelt direkt an die Release-Action.

### `.editorconfig` + Analyzer

- File-scoped Namespaces, Accessibility-Modifier erzwingen, konsistenter Stil.
- Zusammen mit `TreatWarningsAsErrors`: Fehler am Compile statt erst im Log.

### `.vscode/`

- `launch.json` + `tasks.json` beilegen.
- **Hard-Clean-Task** (löscht `bin/` und `obj/` rekursiv).
- Task zum **Öffnen des aktuellen Logfiles** (Logs gehören zum Workflow).

### Tests

- **Eigenes Testprojekt** ist Pflicht – kein Projekt gilt ohne als „aufgesetzt“.

### Repo-Hygiene

- `README.md` (Build/Run + Screenshot), `LICENSE`, dotnet-`.gitignore`.
- Einheitliches **App-Icon** für Fenster + Exe + AppImage.

---

## GitHub Actions (Pflicht)

### CI – bei jedem Push/PR

- `dotnet build` + `dotnet test`. Macht die Test-Pflicht durchsetzbar.

### Release – bei Tag `vX.Y.Z`

- Fertige Pakete für **Windows (win-x64 ZIP)**, **Linux (tar.gz)** und **AppImage**.
- **Node 24** verwenden.

---

## UI / Fenster (Avalonia)

- Alle Fenster erben von der **`ChromeWindow`**-Basisklasse (Custom-Chrome:
  eigene Titelleiste mit Drag, Min/Max/Close), **sauberes Beenden**.
- **Alle Fenster sind resizable** (`CanResize = true`, in `ChromeWindow` gesetzt) –
  inkl. Dialoge und Einstellungen. Keine fix dimensionierten Fenster; sinnvolle
  `MinWidth`/`MinHeight` setzen statt das Resizing zu sperren.
- **Info-/About-Fenster (InfoBox)** ist Pflicht:
  App-Name, Version (aus Assembly), Kurzbeschreibung,
  GitHub-Link (Kroste) und **„Buy me a coffee“-Button** (buymeacoffee.com).
- **Einstellungen-Fenster** ist Pflicht, sobald die App KI nutzt: hier liegen
  Provider-, Endpoint-, Modell- und API-Key-Auswahl **sowie der Modell-Download**
  (Vorbild: **Allpaca**, siehe KI-Integration).

### Avalonia-12-Konventionen (Breaking Changes ggü. v11)

- **Diagnostics:** `Avalonia.Diagnostics` ist entfernt → `AvaloniaUI.DiagnosticsSupport`
  (Debug-only, z. B. 2.2.1):

  ```xml
  <ItemGroup Condition="'$(Configuration)' == 'Debug'">
    <PackageReference Include="AvaloniaUI.DiagnosticsSupport" Version="2.2.1" />
  </ItemGroup>
  ```

- **Custom-Chrome:** `ExtendClientAreaChromeHints` (inkl. `NoChrome`) ist entfernt.
  Stattdessen:

  ```csharp
  WindowDecorations = WindowDecorations.BorderOnly;   // NICHT .None (killt Resize-Griffe)
  ExtendClientAreaToDecorationsHint = true;
  CanResize = true;
  ```

- **APIs:** `TextBox.PlaceholderText` statt `Watermark`.

---

## Architektur & Runtime

- **MVVM** via CommunityToolkit.Mvvm (`ObservableObject`, `[ObservableProperty]`,
  `[RelayCommand]`, Source-Generator-basiert).
- **DI/Komposition** via Microsoft.Extensions.DependencyInjection + Hosting –
  Logger, KI-Provider und Services werden eingehängt (testbar/austauschbar).
- **Globaler Exception-Handler:**

  ```csharp
  AppDomain.CurrentDomain.UnhandledException += (_, e) =>
      _logger.Fatal(e.ExceptionObject as Exception, "Unbehandelte Exception");
  TaskScheduler.UnobservedTaskException += (_, e) =>
  {
      _logger.Fatal(e.Exception, "Unbeobachtete Task-Exception");
      e.SetObserved();
  };
  ```

  → NLog `Fatal` + freundlicher Dialog statt stillem Absturz.

---

## Logging (NLog)

- **Grundsätzlich alles loggen** (Trace/Debug für Abläufe, Info für Aktionen,
  Warn/Error für Probleme).
- **Passwörter/Secrets dürfen NIEMALS geloggt werden** – vor dem Logging
  entfernen/maskieren. Connection-Strings über `SqlConnectionStringBuilder`
  mit geleertem `Password` loggen.
- Logs gehören zum Workflow: **nach Änderungen immer mitanschauen** und gezielt
  auf `Warn`/`Error`/Exceptions prüfen → Teil der Definition of Done.

---

## Secrets & Konfiguration

- Config plattformkonform unter **`%APPDATA%`** bzw. **`$XDG_CONFIG_HOME`** ablegen
  (**nicht** neben die Exe).
- API-Keys **nie im Klartext**: Windows **DPAPI** (`ProtectedData`),
  Linux **libsecret/SecretService**.
- Secrets **nie committen** (`.gitignore`). Logische Erweiterung von
  „Passwörter nie loggen“.

---

## KI-Integration

**Für DTM nicht relevant.** DTM ist ein reines Datenbank-Administrationswerkzeug ohne KI-Funktionen — die Provider-/Modell-/Ollama-Konventionen des Master-Kanons sind hier nicht anzuwenden. Sollte sich das ändern, gilt wieder der Master-Kanon (Allpaca als Referenz).

---

## DTM-Konventionen

- **`SystemFile`-Alias Pflicht:** Im Namespace `DTM` existiert ein eigenes `record File`
  (in `Data/Database_Stats.cs`), das `System.IO.File` schattiert. In jeder Datei, die
  `System.IO.File` braucht, deshalb verpflichtend:

  ```csharp
  using SystemFile = System.IO.File;
  ```

  und konsequent `SystemFile.ReadAllText(...)` etc. verwenden. Ohne diesen Alias greift
  der Compiler auf `DTM.File` zu und die Aufrufe schlagen mit unverständlichen
  Fehlern fehl.

- **PowerShell-Lifecycle:** PowerShell läuft als **in-process Runspace** via
  `Microsoft.PowerShell.SDK` (kein `Process.Start`). Beim App-Ende wird der
  Hauptprozess via `Process.GetCurrentProcess().Kill()` beendet **statt**
  `Environment.Exit(...)`, um Finalizer-Hänger im PowerShell-SDK zu vermeiden
  (siehe `UpdateService`). Diese Sonderbehandlung darf nicht „aufgeräumt" werden.

- **Update-Mechanismus:** Der Updater kopiert nach `%TEMP%`, startet
  `dtm_update.ps1` und beendet sich danach. Änderungen am Update-Pfad müssen
  den Skript-Vertrag (Argumente, erwartete Pfade) wahren.

- **Connection-Strings:** Vor jedem Logging über `OdbcConnectionStringBuilder` /
  `SqlConnectionStringBuilder` `Password`/`PWD` leeren – nie als Rohstring loggen.
  Logische Spezialisierung der allgemeinen Secrets-Regel.

- **FOC-SQL als Dev-Submodul:** Das PowerShell-Modul `FOC-SQL.psm1` ist unter
  `external/FOC-SQL/` als Git-Submodul (`https://github.com/Kroste/FOC-SQL.git`)
  eingebunden — **ausschließlich als Code-Referenz für die Entwicklung** (Aufrufe
  und Verhalten der Modulfunktionen wie `Backup-Database`, `Set-Snapshot`,
  `Restore-Snapshot` im Original nachschlagen). Die DTM-Runtime lädt das Modul
  **nicht** aus diesem Pfad — die produktive Auflösung läuft weiterhin über
  `SambaSource`/`ModulePath` (siehe `Data/Terminal/FocSqlRuntime` und die
  Einstellungen in `ConnectionManagerWindow`). Submodul-Inhalt aktualisieren bei
  Bedarf: `git submodule update --remote external/FOC-SQL`.

- **FOC-SQL-Cmdlet ergänzen — Drei-Punkt-Checkliste:** Wenn eine neue Funktion
  ins FOC-SQL-Submodul kommt (für ein 📦-Roadmap-Item), müssen **alle drei**
  Files konsistent gepflegt werden — sonst ist die Funktion im Code da, wird aber
  zur Laufzeit nicht exportiert (Falle: `Get-Command <Cmdlet>` findet sie nicht,
  Fehlersuche schwer):
  1. **`Module/FOC-SQL/FOC-SQL.psm1`** — Funktionsdefinition + `Export-ModuleMember -Function <Name>` am Dateiende
  2. **`Module/FOC-SQL/FOC-SQL.psd1`** — `FunctionsToExport`-Whitelist erweitern (Modul-Manifest filtert sonst beim Import)
  3. **`Module/FOC-SQL_ToExport.ps1`** — Generator-Script konsistent halten (regeneriert `.psd1` per `New-ModuleManifest`)

  Sanity-Check nach Samba-Rollout: in einem frischen PowerShell-Runspace
  `Import-Module FOC-SQL; Get-Command <NeuesCmdlet>` — wenn nichts kommt, fehlt
  vermutlich der Eintrag in 2. oder 3. Lehre aus Phase 2: ich hatte 1+3 angepasst,
  aber 2 vergessen → `BackupBrowserService` warf „Modul nicht geladen" obwohl
  die `.psm1` korrekt war.

- **MSSQL-Modul-Versionen synchron halten (Phase 7.2):** Sobald `MSSQL.psm1`
  geändert wird, müssen **vier** Stellen synchron gehoben werden — sonst läuft
  FOC-SQL nach dem Rollout in seine eigene `VERSION_MISMATCH`-Exception und
  blockiert sämtliche MSSQL-Aufrufe (Backup, Snapshot, Wartung, Restore):
  1. `Module/MSSQL/MSSQL.psm1` — neue Funktion / geänderte Signatur
  2. `Module/MSSQL/MSSQL.psd1` — `ModuleVersion` **und** `FunctionsToExport`
  3. `Module/MSSQL_ToExport.ps1` — Generator-Script
  4. `Module/FOC-SQL/FOC-SQL.psm1` — `$script:RequiredMssqlVersion` auf die
     neue Mindestversion ziehen. FOC-SQL prüft diese vor jedem MSSQL-Aufruf
     im zentralen Helper `Invoke-MssqlServerScript` und wirft
     `VERSION_MISMATCH: MSSQL-Modul auf '<Host>' (gefunden: x.y.z) …`,
     wenn der Zielserver noch das alte Modul hat. DTM spiegelt dieses
     Pattern in den Statusbar — der User sieht direkt, welcher Server
     einmalig eine PowerShell-Sitzung braucht (Profil-Skript zieht das neue
     MSSQL-Modul automatisch nach).

  Release-Hinweis-Eintrag in `release-notes.json` mit `"modulesChanged":
  ["MSSQL"]` setzen — DTM zeigt im `UpdatePromptWindow` dann den roten
  „MSSQL-Modul wurde geändert"-Banner und der User weiss, dass jeder
  Server einmal angefasst werden muss.

---

## Projektspezifische Realität & offene Migrationen

### Akzeptierte Abweichungen (kein TODO)

- **Keine KI-Integration** – DTM ist Daten-Admin-Tool, nicht KI-Produkt
  (siehe Abschnitt „KI-Integration").

- **„Log An/Aus"-Buttons mit semantischer Doppelnutzung** – die Buttons rufen
  einheitlich `Set-Archive-Log` auf, das Modul dispatched MSSQL→Recovery-Mode-
  Toggle (FULL/SIMPLE), Oracle→echter Archivelog-Toggle. Die Labels sind
  Oracle-zentriert, funktional klappt es für beide. Eine sauberere MSSQL-
  Alternative kommt mit Phase 3.4 (Recovery-Mode-Dropdown).

### Erledigte Migrationen

1. **Avalonia 11.2.3 → 12.x (inkl. `ChromeWindow`-Basisklasse)** — erledigt
   - Core-Pakete auf `12.0.5`, `Avalonia.Controls.DataGrid` + `Avalonia.Fonts.Inter`
     auf `12.0.1` (höher gibt es nicht).
   - `Avalonia.Diagnostics` ersetzt durch `AvaloniaUI.DiagnosticsSupport 2.2.1`
     (Debug-only).
   - `Tmds.DBus.Protocol`-Pin entfernt.
   - `Watermark` → `PlaceholderText` in `ConnectionManagerWindow.axaml` und
     `Views/Controls/ConsoleControl.axaml`.
   - `ChromeWindow`-Basisklasse in `Views/ChromeWindow.cs` (setzt
     `WindowDecorations.BorderOnly`, `ExtendClientAreaToDecorationsHint = true`,
     `CanResize = true`; stellt gemeinsame `OnTitleBarPointerPressed` und
     `OnTitleBarDoubleTapped` bereit).
   - Alle 7 Windows umgestellt; `SystemDecorations`-Attribute entfernt;
     `CanResize="False"` in den 4 Dialogen ersetzt durch `MinWidth`/`MinHeight`.
   - Folge-Anpassungen wegen Avalonia-12-API-Änderungen:
     - `IClipboard.SetTextAsync` → `SetDataAsync(new DataTransfer { … })`
       in `Views/Controls/AnsiConsole.cs`.
     - `VisualTreeAttachmentEventArgs.Root` → `TopLevel.GetTopLevel(this)`
       in `Views/Controls/ConsoleControl.axaml.cs`.
   - Build + 269 Tests grün auf Linux. **Smoke-Test der UI auf Windows + Linux
     steht noch aus** (Drag/Resize/Min/Max in jedem Fenster prüfen).

2. **Kroste-Skill-Compliance-Pass (`v2.4.0`-Vorlauf)** — erledigt
   - GitHub-Actions-Majors auf Node-24-Runtime: `checkout@v7`, `upload-artifact@v7`,
     `download-artifact@v8`.
   - NLog Secret-Masking: `Diagnostics/MaskingLayoutRenderer.cs` (Wrapper-
     LayoutRenderer `${masked}`), per `[ModuleInitializer]` +
     `LogManager.Setup().SetupExtensions()` registriert. `Nlog.config` rendert
     Message und Exception durch den Renderer — greift auch, wenn eine
     `SqlException` einen ConnectionString mit Passwort enthaelt. Regex-Patterns:
     `Password=`/`PWD=` (ConnectionString), `password=`/`token=`/`api_key=`
     (URL/JSON), `Bearer <token>`, `Authorization:`.
   - Avalonia 12.0.5 → 12.1.0 (Skill-Mindest wegen nativem Wayland-Backend).
     `Avalonia.Controls.DataGrid` + `Avalonia.Fonts.Inter` von 12.0.1 auf 12.1.0
     mitgezogen. `CentralPackageTransitivePinningEnabled=true` aktiviert, um
     transitive Deps ueber `PackageVersion` pinnen zu koennen —
     `System.Security.Cryptography.Xml` auf 10.0.10 hochgezogen wegen mehrerer
     hoch-Sev CVEs.
   - xUnit 2.9.3 → `xunit.v3` 3.2.2 (v2 deprecated). API-kompatibel, kein
     Quellcode-Change noetig.
   - CI + Release-YAML bauen jetzt `DTM.slnx` statt `DTM.csproj` einzeln;
     `--no-build` im Test-Step vermeidet doppelten Compile in CI.
   - `.vscode/tasks.json` um Skill-Standard-Tasks ergaenzt: `test`, `clean`,
     `publish-win-x64`, `publish-linux-x64`, `release (tag + push)`.
     `scripts/release.sh` + `scripts/release.ps1` als Trigger fuer letzteren.
   - `Views/Controls/TitleBar.axaml(+.cs)` als wiederverwendbare Titelleiste
     angelegt (StyledProperties `Title`, `ShowMinimize`, `ShowMaximize`,
     `CloseResult`). **Rollout aktuell nur AboutWindow + ConnectionManagerWindow**
     — die restlichen 12 Dialoge behalten ihre eigene Titelleiste, weil:
     (a) 6 haben ein Icon-Header-StackPanel statt reinem Text (TitleBar braeuchte
     Content-Slot-API), (b) 6 haben spezielle Dialog-Result-Semantik
     (`TimePickResult.Cancel()`, EditConnection `Close(false)`, …) und die
     lassen sich nicht mit `CloseResult=".."` per XAML setzen — nur ueber
     `x:Name` + Code-Behind. Follow-up-Ticket unten.
   - Tests: 361/361 gruen (350 vorher + 11 neue MaskingLayoutRenderer-Tests).
   - **Repo-Umzug (`v2.4.0`-Vorlauf, Skill-Struktur):** App-Sourcen aus dem
     Repo-Root in den Unterordner `DTM/` verschoben (App.axaml, Program.cs,
     Assets/, Composition/, Data/, Diagnostics/, ViewModels/, Views/,
     Nlog.config, release-notes.json, app.manifest, DTM.csproj). Nachgezogen:
     `DTM.slnx` (jetzt beide Projekte explizit), `DTM.Tests/DTM.Tests.csproj`
     (ProjectReference auf `../DTM/DTM.csproj`), `DTM.csproj`
     (`DefaultItemExcludes` fuer `DTM.Tests\**` entfaellt, nicht mehr noetig),
     `.vscode/tasks.json` und `.vscode/launch.json` (Pfade `DTM/DTM.csproj`
     und `DTM/bin/...`), `.github/workflows/release.yml` (publish-Befehle
     und AppImage-Icon-Pfad `DTM/Assets/lhp_logo.png`), README (dotnet-run-
     Beispiele). Publish nach `linux-x64` liefert `DTM/bin/Release/...`,
     Binary laeuft. Skill-konforme Struktur: `DTM/` und `DTM.Tests/` parallel
     im Root.

3. **App-Icon + System-Tray (Skill-Standards Post-v2.3.0)** — erledigt
   - **App-Icon:** `DTM/Assets/dtm.png` (256x256, transparenter Grund,
     abgerundetes Quadrat mit klassischem Datenbank-Zylinder in Teal
     `#2DD4BF` — passt zu DTMs Akzentfarbe, ohne Text erkennbar auch als
     16x16-Favicon). Multi-Res `DTM/Assets/dtm.ico` (16/24/32/48/64/128/256)
     als `<ApplicationIcon>` in der csproj → Windows-Exe hat jetzt im
     Explorer/Taskbar ein Icon. `ChromeWindow`-Basisklasse laedt die PNG
     im ctor als `Window.Icon` (try/catch — ohne Icon lauffaehig). AppImage-
     Release-Job zieht die PNG direkt (kein ImageMagick-Convert vom
     lhp_logo mehr). `scripts/build_icon.py` als Pillow-Generator im Repo,
     um das Icon reproduzierbar zu rebuilden (Design-Iterationen als
     Python-Diff statt Binary). Das Institutionslogo `lhp_logo.png` bleibt
     im AboutWindow als „Absender-Info" — kein App-Icon.
   - **System-Tray:** `DTM/Views/TrayController.cs` nach Skill-Muster
     (Referenz Checkmk Cockpit). Verhalten: Minimieren → `window.Hide()`
     (Fenster in den Tray, Prozess laeuft weiter — PowerShell-Runspace und
     DB-Verbindungen bleiben bestehen), Schliessen ✕ → beendet regulaer
     (kein `ShutdownMode`-Umbau noetig). Tray-Menue „Anzeigen" + Separator
     + „Beenden"; Linksklick aufs Tray-Icon = Anzeigen. Vier Pflicht-
     Absicherungen umgesetzt: GC-Referenz (App._tray-Feld), Restore-Guard
     (`_restoreInProgress`-Flag + `Dispatcher.UIThread.Post`), try/catch
     mit Fallback auf Standard-Minimieren bei fehlendem Tray-Support
     (headless-Server / kaputtes DBus), Linux zieht `Tmds.DBus.Protocol`
     transitive ueber Avalonia. Instanziierung in
     `App.OnFrameworkInitializationCompleted` nach MainWindow-Erzeugung.

   Skill-Update parallel (siehe `~/.claude/skills/kroste-avalonia/`):
   `SKILL.md` verankert beides als Pflicht (nicht mehr optional/situativ),
   `references/design.md` bekommt einen eigenen „App-Icon"-Abschnitt mit
   Design-Vorgaben, `assets/TrayController.cs.example` +
   `assets/scripts/build_icon.py.example` als generische Kopiervorlagen
   fuer neue Projekte. Neue Projekte kriegen jetzt die Rollout-Schritte
   3a (Icon) und 3b (Tray) in „Neues Projekt aufsetzen".

### Offene Roadmap (Phasen, in dieser Reihenfolge)

> **Legende:** `S` = klein (1–3 h, 1 Commit) · `M` = mittel (halber Tag, 2–4 Commits) ·
> `L` = groß (mehrere Tage). 📦 = FOC-SQL-Submodul muss erweitert werden
> (eigener PR in `Kroste/FOC-SQL` + manueller Samba-Rollout durch Lars,
> danach DTM ankoppeln — **`.psm1` + `.psd1` + `_ToExport.ps1` konsistent**
> halten, siehe DTM-Konventionen). 🛡 = destruktive Aktion, braucht
> Bestätigungs-Dialog + Test-DB. 🔁 = setzt vorheriges Item voraus.

#### Phase 0 — Fundament (Schutz vor späteren Regressionen)

- [x] **0.1** CI-Workflow `.github/workflows/ci.yml`: `dotnet build` + `dotnet test`
      auf Push/PR. — `S` _(erledigt: `91991c7`, Actions nachgezogen auf
      Node-24-native Major-Versionen mit `1c7acde`)_
- [x] **0.2** Globaler Exception-Handler in `Program.cs`
      (`AppDomain.CurrentDomain.UnhandledException` +
      `TaskScheduler.UnobservedTaskException` → NLog Fatal + Dialog). — `S`
      _(erledigt: `0d0b48a`; zusätzlich `Dispatcher.UIThread.UnhandledException`
      für UI-Thread, `FatalErrorWindow` als ChromeWindow-Dialog)_
- [x] **0.3** `Microsoft.Extensions.DependencyInjection` einziehen; manuelle
      Instanziierung in `App.axaml.cs` (`BuildDataLayer`) durch DI ersetzen
      — ViewModels/Services über Container. — `M`
      _(erledigt: `a9b98be`; `Composition/ServiceRegistrations.cs` als Composition-Root,
      `App.Services` als statischer `IServiceProvider`. `Microsoft.Extensions.Hosting`
      bewusst weggelassen — IHost/IConfiguration/ILogger werden nicht gebraucht,
      NLog konfiguriert sich selbst, JSON-Stores haben ihr eigenes Schema. Lässt
      sich nachziehen, wenn Config/Logging via DI später nötig wird.)_
- [x] **0.4** `Directory.Build.props` (Inhalt wie Tech-Stack-Block oben) +
      `.editorconfig` (file-scoped Namespaces, Accessibility-Modifier erzwingen) +
      `LICENSE`. — `S`
      _(erledigt: `f9e1236` für `Directory.Build.props` + `.editorconfig` +
      csproj-Aufräumung, MIT-LICENSE © 2025-2026 Lars Oste separat. Bestehender
      Code ist mit den neuen Regeln konform — keine Quellcode-Anpassung nötig.)_

#### Phase 1 — Quick Wins (keine Submodul-Änderung nötig)

- [x] **1.1** `Set-Archive-Log`-Inkonsistenz geklärt — entschieden: Status quo.
      Code-Realität (entgegen ursprünglicher Roadmap-Annahme): die „Log An/Aus"-
      Buttons sind in `MainWindowViewModel.ApplyStats` für **beide** DB-Typen
      aktiv. `Set-Archive-Log` dispatched im Modul nach DB-Typ: MSSQL togglet
      `Recovery FULL/SIMPLE`, Oracle togglet echten `ARCHIVELOG ON/OFF`. Die
      semantische Doppelnutzung der gleichen Buttons bleibt absichtlich — Phase
      3.4 bringt für MSSQL einen dedizierten Recovery-Mode-Dropdown
      (FULL/SIMPLE/BULK_LOGGED), der die Mehrdeutigkeit für den MSSQL-Pfad
      auflöst. — `S` _(siehe „Akzeptierte Abweichungen" oben)_
- [x] **1.2** Snapshot-Buttons: Multi-PDB-Warnung für Oracle vor `Restore-Snapshot`
      — übersprungen, wird durch 1.4 (Restore-Vorschau-Dialog mit Restore-Points-
      und PDB-Liste) abgedeckt. Eigener Stop-Gap-Quick-Fix wäre Wegwerfcode. — `S` 🛡
      _(skip, siehe 1.4)_
- [x] **1.3** Cluster-Health-Indicator (`Get-ClusterHealthStatus`) in Info-Card oder
      als Status-Punkt. MSSQL-only, read-only. — `S`
      _(erledigt: `57040e5`; kleiner „Cluster-Health"-Pillenbutton im Info-Card-
      Header neben dem Status-Badge, `ClusterHealthVisible`-Binding blendet ihn
      bei Oracle aus. `TerminalBus.RunFocSqlServerAction` als neue Hilfsmethode
      für FOC-SQL-Funktionen mit `-Server` statt `-Database`.)_
- [x] **1.4** Oracle-Restore-Vorschau (`Get-OracleRestoreInfo`) — neuer Dialog
      `OracleRestoreSelectWindow` mit Liste der Restore Points + PDBs vor
      `Restore-Snapshot`. Macht 1.2 obsolet, wenn richtig gebaut. — `M` 🛡
      _(erledigt: `7e1c7d5`; Variante B = eigener In-Process-PowerShell-Runspace via
      `OracleRestoreService` + `FocSqlRuntime.BuildImportSnippet`. POCOs in
      `Data/Terminal/OracleRestoreInfo.cs`, ViewModel mit Loading/Error-State,
      Dialog mit prominenter Multi-PDB-Warnung. Integration in
      `MainWindowViewModel.RestoreSnapshot` macht den Weg fuer MSSQL unveraendert,
      fuer Oracle wird vorab der Dialog gezeigt — kein Aufruf ohne explizite
      Bestaetigung.)_

#### Phase 2 — Sessions & Backup-Workflow

- [x] **2.1** 📦 FOC-SQL erweitern: `Close-DbSessions` als Dispatch-Wrapper
      (MSSQL: PSSession → `Database-Close-Connections`; Oracle: SSH +
      PL/SQL-Schleife mit `ALTER SYSTEM KILL SESSION ... IMMEDIATE` über
      `v$session`, nur USER-Sessions). — `M`
      _(erledigt: FOC-SQL `fddb124`, Submodul-Pointer DTM `2658dcf`. Aktivierung
      nach Samba-Rollout.)_
- [x] **2.2** 🔁 „Alle Sessions beenden"-Button im `SessionsWindow` mit
      doppelter Bestätigung (neuer reusable `ConfirmWindow`-Dialog). Granularität
      „alle" statt „pro Row" — bewusst entschieden, vereinfacht die UI und reicht
      für den primären Use-Case (Pre-Check vor Backup-Restore). — `M` 🛡
      _(erledigt: DTM `bd66845`; SessionsViewModel mit
      `Configure(focDbId, displayName)`, Footer mit DB-Anzeige + Danger-Button,
      ConfirmWindow als reusable Dialog für künftige destruktive Aktionen.)_
- [x] **2.3** 📦 FOC-SQL erweitern: `Get-DbBackups` + `Invoke-DbRestore`
      (MSSQL: `Get-ChildItem` im Backup-Verzeichnis + `Database-Backup-Restore`;
      Oracle: in v1 nicht unterstützt — RMAN-Workflow kommt später). — `L`
      _(erledigt: FOC-SQL `0971904`, Submodul-Pointer DTM `c1c20d8`. Beide
      Wrapper liefern bei Oracle-Eingabe eine klare Fehlermeldung.)_
- [x] **2.4** 🔁 Backup-Browser als neue Action-Gruppe „BACKUPS" im
      MainWindow + Dialog mit DataGrid (Datei/Datum/Größe) + Restore-Knopf.
      MSSQL-only (Action-Gruppe via `BackupBrowserVisible` bei Oracle
      ausgeblendet). — `L` 🛡
      _(erledigt: DTM `6a14c08`; `BackupBrowserService` im eigenen In-Process-PS-
      Runspace (analog zu `OracleRestoreService`), Restore-Aufruf läuft über den
      TerminalBus im sichtbaren pwsh-Tab. Sessions-schließen passiert implizit
      in `Database-Backup-Restore` vor dem RESTORE. Restore-Confirm zeigt
      Backup-Details + Warnung.)_

#### Phase 3 — Wartungs-Tooling

- [x] **3.1** 📦 FOC-SQL erweitern: `Invoke-DbMaintenance` mit Switches
      (`-CheckDb`, `-IndexRebuild`, `-ShrinkLog`) — Wrapper um die drei
      MSSQL-Funktionen via PSSession + `Import-Module MSSQL`. Oracle wird
      explizit nicht unterstützt (T-SQL-spezifisch). — `M`
      _(erledigt: FOC-SQL `333b734`, Submodul-Pointer DTM `0006249`. Drei-
      Punkt-Checkliste `.psm1`+`.psd1`+`_ToExport.ps1` eingehalten.)_
- [x] **3.2** 🔁 Neue Action-Gruppe „WARTUNG" im MainWindow mit drei Buttons
      (CHECKDB / Index Rebuild / Shrink Log), MSSQL-only via
      `MaintenanceVisible`-Binding. Shrink-Log triggert vorab den
      `ConfirmWindow` mit Log-Chain-Hinweis; CHECKDB und Index-Rebuild
      laufen direkt. — `S`
      _(erledigt: DTM `df92c5c`.)_
- [x] **3.3** 📦 FOC-SQL erweitern: `Set-DbRecoveryMode` als Wrapper um
      `Database-Set-Recovery-Mode` (ValidateSet FULL/SIMPLE/BULK_LOGGED). — `S`
      _(erledigt: FOC-SQL `a5869b4`, Submodul-Pointer DTM `7daf557`.)_
- [x] **3.4** 🔁 Recovery-Mode-Dropdown im Info-Card (FULL/SIMPLE/BULK_LOGGED)
      für MSSQL, mit Bestätigung — Wechsel zu SIMPLE bricht Log-Chain. — `S`
      _(erledigt: DTM `31e938a`. ComboBox ersetzt bei MSSQL den
      Read-Only-TextBlock; bei Oracle bleibt der TextBlock mit
      `ArchiveLogMode`. Suppression-Flag verhindert dass das initiale
      Server-Sync den Change-Dialog triggert; bei User-Abbruch wird die
      ComboBox auf den zuletzt synchronisierten Wert zurueckgedreht.)_

#### Phase 4 — Polish & Komfort

- [x] **4.1** Snapshot-Cleanup mit Altersfilter — **nicht relevant für DTM**.
      Das automatische Löschen alter Snapshots läuft bereits als SQL-Server-Agent-Job
      auf dem MSSQL-Server (`Database-Snapshot-Delete -Day n` wird dort regelmäßig
      ausgeführt). DTM braucht dafür keinen UI-Pfad. — `S` 📦
      _(skip, redundant zum Server-Side-Job)_
- [x] **4.2** `AboutWindow` ergänzen: GitHub-Link auf `https://github.com/Kroste/DTM`
      + „Buy me a coffee"-Button (`buymeacoffee.com`). — `S`
      _(erledigt: dieser Commit; zwei Buttons vor dem Footer, BMC-URL
      `https://buymeacoffee.com/kroste` aus `.github/FUNDING.yml`. Browser-Open
      via `ProcessStartInfo { UseShellExecute = true }`.)_
- [x] **4.3** `.vscode/tasks.json` ergänzen: Hard-Clean-Task (rekursives Löschen
      `bin/`/`obj/`) + Task „Aktuelles Logfile öffnen" (`logs/info.log`/`error.log`). — `S`
      _(erledigt: `90fe0ba`; drei cross-platform Tasks „hard clean (bin + obj)",
      „Logfile info oeffnen", „Logfile error oeffnen" via `linux`/`osx`/`windows`-
      Branches.)_
- [x] **4.4** **MinVer** einbinden; manuelle `<Version>`/`<AssemblyVersion>` aus
      `DTM.csproj` entfernen. Tag-Schema `vX.Y.Z` ist schon vorhanden. — `S`
      _(erledigt: dieser Commit; MinVer 7.0.0 mit `MinVerTagPrefix=v`; ab jetzt
      kommt die Version aus dem juengsten Git-Tag. Zwischen Tags gibt's
      pre-release-Versionen wie `1.1.1-alpha.0.2+<sha>`.)_
- [x] **4.5** `.github/workflows/release.yml` um AppImage-Job erweitern
      (Node 24 ist bereits gesetzt). — `M`
      _(erledigt: `9121cab`; neuer `build-appimage`-Job (Publish + AppDir +
      `appimagetool`), `packaging/appimage/dtm.desktop` + `AppRun` als Repo-Files,
      `workflow_dispatch`-Trigger für Build-Tests ohne Tag, `release`-Job
      conditional auf Tag-Push. Manual dispatch hat alle 4 Build-Jobs grün
      laufen lassen.)_
- [x] **4.6** `README.md` um Screenshot ergänzen. — `S`
      _(erledigt: dieser Commit; drei Image-Slots im `docs/`-Ordner
      (`screenshot-main.png`, `screenshot-connections.png`,
      `screenshot-oracle-restore.png`), `docs/.gitkeep` hält das Verzeichnis
      auch ohne Bilder im Repo. Bilder werden separat von Lars eingelegt.
      Bonus: Aktionen-Tabelle um Cluster-Health-Zeile und Oracle-Restore-
      Vorschau-Absatz erweitert, „Log An/Aus"-Doppelnutzung präzisiert.)_

#### Phase 5 — Optional / Niedrige Priorität

- [x] **5.1** Query-Store-Toggle (MSSQL). — `S` 📦
      _(erledigt zusammen mit 5.3 als „DB-Konfiguration"-Dialog;
      FOC-SQL `d9f9e50`, DTM-Pointer + UI im nächsten Commit.)_
- [x] **5.2** SQL-Script-Runner-Dialog (`Database-Execute-SQL`/`-GetSQL-File`)
      — **bewusst nicht umgesetzt** (skip). Begründung: unkontrolliert ausgeführte,
      ggf. veraltete SQL-Scripts sind ein zu großes Sicherheits-Risiko, das den
      Komfortgewinn nicht aufwiegt. — `M` 📦
- [x] **5.3** `Database-Set-Page-Verify` / `Database-Set-Compatibility`. — `S` 📦
      _(erledigt; FOC-SQL `d9f9e50`: `Set-DbQueryStore`, `Set-DbPageVerify`,
      `Reset-DbCompatibility`. DTM: neuer `DbConfigurationWindow` mit drei
      Sektionen + „Anwenden"-Buttons + Confirm-Dialogen, Button „Konfiguration"
      in der WARTUNG-Action-Gruppe (MSSQL-only).)_
- [x] **5.4** Stats-Konsolidierung: ODBC-Stats durch `Get-DatabaseStats` ablösen
      — **bewusst nicht umgesetzt** (skip). Begründung: aktueller ODBC-Pfad
      ist schnell und stabil; PS-Runspace-Aufruf bei jedem DB-Wechsel wäre
      eine spürbare Latenz, der Refactor löst nur eine reine Code-Eleganz-Frage
      (Logik-Duplikation). Trade-off zugunsten Performance. — `L`

#### Phase 7 — Update-Kommunikation & Versions-Konsistenz (`v2.1.0`)

Zwei zusammenhängende Themen rund um „Was muss der User wissen, wenn DTM ein
Update ausliefert?" Hintergrund: das FOC-SQL-Modul wird beim Client durch
DTM automatisch von Samba gezogen, das MSSQL-Modul auf den Servern aber
**nicht** — dort sync't das User-PS-Profil, was eine einmalige PowerShell-
Aktion pro Server bedeutet.

- [x] **7.1** `release-notes.json` im Rollout-Verzeichnis (gepflegt im
      DTM-Repo) — pro Release ein Eintrag mit Notes-Liste und
      `modulesChanged`-Tags (`"FOC-SQL"` und/oder `"MSSQL"`).
      `UpdateService.LoadReleaseNotesAsync` liest die Datei beim Check;
      `UpdatePromptWindow` zeigt alle Einträge strikt zwischen aktueller
      und Ziel-Version. Roter Banner bei `"MSSQL"` (Server-Aktion nötig),
      grüner Hinweis bei `"FOC-SQL"` (Client-Sync automatisch). — `M`
      _(erledigt: `Data/Updater/ReleaseNote.cs` POCO,
      `UpdateService.LoadReleaseNotesAsync` mit Range-Filter
      `current < v <= newVersion` + Sortierung absteigend,
      `release-notes.json` im Repo-Root + csproj `CopyToOutputDirectory`,
      `UpdatePromptWindow.axaml` mit ScrollViewer, MSSQL-/FOC-SQL-Banner
      und ItemsControl-Notes-Liste. Initial-Eintrag v2.1.0 mit
      `modulesChanged: ["FOC-SQL"]`. Tests:
      `LoadReleaseNotesAsync_FiltersByRange_AndSortsDescending` +
      `LoadReleaseNotesAsync_ReturnsEmpty_WhenFileMissing`.)_
- [x] **7.2** 📦 FOC-SQL Versions-Konsistenz-Check Backend:
      `$script:RequiredMssqlVersion = [Version]'1.3.4'` als Modul-
      Konstante + neuer interner Helper `Invoke-MssqlServerScript`
      (PSSession + Credential + Versions-Check + Import-Module +
      User-ScriptBlock). Alle 8 MSSQL-Wrapper auf den Helper umgestellt
      (kein Inline-Dupe). Bei MSSQL-Modul-Version < RequiredMssqlVersion
      wirft der Helper eine klare `VERSION_MISMATCH:`-Exception mit
      Server-Name und Aufforderung. — `M`
      _(erledigt: FOC-SQL `4a45b47`. Helper wird intern verwendet —
      kein Export nötig, also keine Drei-Punkt-Checkliste-Pflege für
      `Invoke-MssqlServerScript` selbst. Refactored:
      `Close-DbSessions-MSSQL`, `Get-DbBackups-MSSQL`, `Invoke-DbRestore`,
      `Invoke-DbMaintenance`, `Set-DbRecoveryMode`, `Set-DbQueryStore`,
      `Set-DbPageVerify`, `Reset-DbCompatibility`. Aeltere Legacy-Wrapper
      (`Backup-Database`, `Set-Snapshot`, `Restore-Snapshot-MSSQL`,
      `Get-DatabaseStats-MSSQL`, `Copy-Database-ToSamba-MSSQL`) noch nicht
      auf den Helper umgezogen — eigener Refactor-Posten, falls
      VERSION_MISMATCH dort auch noch sauber gemeldet werden soll.)_
- [x] **7.3** DTM-Status-Bar-Spiegelung: `TerminalBus` exposed ein
      Output-Observable, das jede pwsh-Tab-Zeile durchreicht.
      `MainWindowViewModel` matched auf `VERSION_MISMATCH:`-Pattern und
      zeigt den Hinweis kompakt in der Statusleiste. — `S`
      _(erledigt: `TerminalBus.LineEmitted` (static event) + neues
      `TerminalLineEventArgs`/`TerminalLineKind`-Paar; Bus hooked die
      `OutputReceived`/`ErrorReceived` der jeweils registrierten Session.
      `MainWindowViewModel` lauscht im Konstruktor und matched per
      `Regex` auf `VERSION_MISMATCH:.*'(?<host>...)'.*gefunden:\s*(?<found>...)`
      — Statusbar zeigt
      `⚠ MSSQL-Modul auf '<Host>' veraltet (<Version>). Bitte PS-Sitzung
      auf dem Server oeffnen.` Auto-Reset über die naechste regulaere
      StatusBar-Setzung; Komplett-Banner mit Auto-Pre-Check beim DB-Wechsel
      ist eigener Posten 7.4 / spaeter.)_

#### Phase 9 — Multi-Credential-Support fuer PowerShell-Remoting

Kontext: DTM verbindet sich zum SQL-Server auf zwei Wegen — ODBC/1433
(Stats, Datenbank-Liste) und PowerShell-Remoting/WinRM (FOC-SQL-
Cmdlets: Backup, Snapshot, Wartung, …). Die ODBC-Credentials liegen
seit Phase 6 pro Server in `connections.json` (DPAPI-verschluesselt).
Die PS-Remoting-Credentials aber kamen bisher aus **einer** globalen
`credential.xml` im User-Profil — reicht, solange alle Server derselben
AD-Zone angehoeren.

Kaputt sobald ein Server in einer anderen Zone steht (z. B. DMZ mit
lokaler Domain). Aktuell gibt's keinen Weg, DTM/FOC-SQL fuer diesen
Server abweichende Windows-Credentials zu geben.

Entscheidungen:
- **Kombi B + Fallback A:** DTM ist Owner der Multi-Server-PS-Credentials
  (im ConnectionManager pro Server pflegbar, DPAPI in `connections.json`).
  Fallback = globales `credential.xml`, sodass Konsolen-Nutzer und
  Bestandssetups unveraendert weiterlaufen.
- **Runspace-Injektion:** DTM pusht beim pwsh-Session-Setup eine
  `$global:DtmCredMap = @{'<Server>' = <PSCredential>; …}` in den
  Runspace. Der FOC-SQL-Helper `Invoke-MssqlServerScript` konsultiert
  die Map anhand des `-Server`-Parameters vor dem xml-Fallback.
- **Kein Klartext im pwsh-Tab:** Credentials landen als Runspace-Variable
  in Memory, laufen nie durch die sichtbare Command-Line.
- **Optional pro Server:** wenn `RemoteUser` leer ist, verhaelt sich DTM
  wie bisher — kein Regressions-Risiko fuer den FOC-SQL-Server-Setup.

Sub-Items:

- [ ] **9.1** Netzwerk-Vorfrage: `Test-WSMan -ComputerName <dmz-host>
      -Credential (Get-Credential)` von Lars durchgefuehrt. Wenn nein:
      DMZ-Server bleibt „ODBC-only" (nur Stats-Panel, alle FOC-SQL-
      Actions ausgegraut) — Feature-Toggle ueber DB_SERVER-Property.
      Wenn ja: 9.2 - 9.5 rollout. — `S`
- [x] **9.2** `ServerCredential` + `ConnectionEntry` um optionale Felder
      `RemoteUser` / `RemotePassword(Protected)` erweitern; leer =
      Fallback aufs globale `credential.xml`. Backward-kompatibel zu
      bestehender `connections.json` (fehlende Felder → leer). — `S`
      _(erledigt: `e15b902`. `HasRemoteCredential`-Property, DPAPI-
      Roundtrip via `PlainRemotePassword`, Legacy-JSON-Deserialisierung
      getestet.)_
- [x] **9.3** ConnectionManagerWindow: Panel „PS-Remoting-Credentials
      (falls abweichend)" — zwei zusaetzliche Zellen pro Zeile (User +
      Password). Leer lassen = Fallback. Beim Speichern DPAPI wie beim
      ODBC-Passwort. — `M`
      _(erledigt: `a1627c0`. Panel im EditConnectionWindow, nur MSSQL
      sichtbar (Oracle nutzt SSH-Keys). Typ-Wechsel MSSQL→Oracle droppt
      Remote-Felder, damit kein "vergessener" DPAPI-Blob zurueckbleibt.)_
- [x] **9.4** 📦 FOC-SQL: `Invoke-MssqlServerScript` bekommt optionalen
      `[PSCredential]$Credential`-Parameter. Reihenfolge der Aufloesung:
      Parameter → `$global:DtmCredMap[$Server]` → `credential.xml`. — `M`
      _(erledigt: FOC-SQL `d5a38ee`. Wrapper unveraendert — geben nur
      `-Server` weiter, Helper macht Map-Lookup pro Server. Fehlermeldung
      bei fehlender xml erwaehnt den DTM-Weg fuer DMZ-Setup.)_
- [x] **9.5** DTM injiziert `$global:DtmCredMap` in den pwsh-Runspace
      beim `RegisterPowerShellSession` — aus der Server-Liste alle
      Eintraege mit gesetztem `RemoteUser` einsammeln, als
      `PSCredential`-Objekte in die Map schreiben. Bei
      Connection-Manager-Save neu synchronisieren. — `S`
      _(erledigt: `DtmCredMapBuilder` baut Hashtable
      (case-insensitive Keys) mit `PSCredential`-Objekten aus
      SecureString-Passwoertern. Injektion via
      `PowerShellTerminalSession.SetGlobalVariable` — direkt ueber
      `SessionStateProxy`, KEIN Command-Interpreter, keine
      Klartext-Passwoerter im pwsh-Log. `TerminalBus.SetCredMap` cached
      Server-Liste, injiziert sofort bei registrierter Session.
      `App.axaml.cs` (Startup) + `ConnectionManagerViewModel.Save`
      (nach Reload) rufen `SetCredMap`. Oracle-Server werden bewusst
      ausgefiltert.)_

**Sicherheit:** Passwoerter nur DPAPI-verschluesselt in `connections.json`;
niemals Klartext ins pwsh-Tab, ins Log, in Fehlermeldungen. Log-Maske via
`LogMask` (schon vorhanden fuer ConnectionString) wiederverwenden.

#### Phase 10 — ODBC-Direct-Backend fuer DMZ-Server (`v2.2.0`)

Kontext: In der DMZ ist WinRM (5985/5986) hart deaktiviert (Vorfrage
9.1 negativ). ODBC/1433 zum SQL-Server geht aber. FOC-SQL im
Standard-Setup ist zwei Dinge in einem: (a) T-SQL-Ausfuehrung
(BACKUP, RESTORE, Snapshot, Recovery-Mode, CHECKDB, …) und (b)
PowerShell-Orchestrierung drumherum (Mail nach Backup, Cluster-
Health-Aggregation, File-Copy zu Samba, ScheduledTasks, …). Fuer die
DMZ willst du (a) direkt ueber ODBC, ohne (b).

Kernentscheidung: **Backend-Wahl pro Server**, persistiert in
`connections.json` als `ServerBackend`-Enum. Default = `FocSql` →
Bestandssetups unveraendert. `OdbcDirect` = neue Codepfade,
15 von 17 Actions machbar (Copy-Database-ToSamba +
Sync-Database-ToTest sind File-System-Ops und werden bei OdbcDirect
ausgegraut).

Design-Entscheidungen (siehe Ultrathink-Notiz vor Phase-Start):
- **Dispatch am Aufrufer** (im MainWindowViewModel switch auf Backend),
  KEIN Backend-Interface — FOC-SQL ist fire-and-forget im pwsh-Tab,
  ODBC-Direct ist synchrones SQL, Signaturen passen nicht in eine
  gemeinsame Abstraktion. Erweitern wenn wirklich 3+ Backends.
- **Output-Kanal = derselbe pwsh-Tab** via
  `ITerminalBusInjector.InjectNotice`. `OdbcConnection.InfoMessage`
  wird gehookt und liefert `DBCC CHECKDB`- / `DBCC SHRINKFILE`-Live-
  Meldungen als Notices — konsistente UX zum FOC-SQL-Live-Stream.
- **SQL-Injection: `sp_executesql` + `QUOTENAME`** fuer Object-Namen,
  ODBC-Parameter fuer Werte. Kein String-Concat auf Client.
- **`MSSQL_ODBC`** bekommt `ExecuteNonQueryAsync` +
  `ExecuteReaderAsync` public. Bestehende Connection-Instanz wird
  geshared (Factory cached pro Server, Fix `0c7a4d5`).
- **Backup-Browser** ueber `msdb.dbo.backupset` join
  `backupmediafamily` — Server-verwaltete History, findet DTM-eigene
  Backups zuverlaessig. Kein FS-Zugriff noetig.
- **BackupRoot** via `master.dbo.xp_instance_regread` (SQL-Server-
  Default-BackupPath). Kein Extra-Config-Feld.
- **Snapshot-Naming**: File-Layout aus `sys.master_files` lesen,
  Snapshot-Filenames als `<Original>_Snapshot_<yyyyMMddHHmmss>.ss`,
  Multi-Data-File-DBs werden korrekt behandelt.
- **Async**: `Task.Run` um synchrone ODBC-Calls, `SemaphoreSlim` pro
  Server serialisiert parallele Actions (OdbcConnection nicht thread-
  safe).

Sub-Items:

- [x] **10.1** `ServerBackend`-Enum (`FocSql | OdbcDirect`) +
      `ConnectionEntry.Backend` + `DB_SERVER.Backend`-Property.
      Default `FocSql`. `JsonStringEnumConverter` fuer Lesbarkeit.
      Legacy-JSON deserialisiert korrekt zu Default. — `S`
      _(erledigt: `817f713`.)_
- [x] **10.2** `EditConnectionWindow` bekommt Backend-Dropdown, nur
      bei MSSQL sichtbar (Oracle bleibt unveraendert; SSH ist
      alternativlos). Bei Typ-Wechsel MSSQL→Oracle Backend auf Default
      zurueck. — `S`
      _(erledigt: `8ec66c1`.)_
- [x] **10.3a** `MSSQL_ODBC.ExecuteNonQueryAsync(sql, params)` +
      `ExecuteReaderAsync<T>(sql, mapper, params)` public. Optionaler
      `InfoMessage`-Callback fuer Live-Notices. Alle Connection-
      Lifecycle-Details bleiben intern. — `S`
      _(erledigt: `8ff4cf4`.)_
- [x] **10.3b** `OdbcMssqlActionService`: Recovery-Mode, Query-Store,
      Page-Verify, Compatibility-Reset (4 einfache `ALTER DATABASE`-
      Statements plus Archive-Log-Toggle-Wrapper fuer MSSQL). — `M`
      _(erledigt: `90c1efe`. Whitelist-Predicates
      `IsValidRecoveryMode`/`IsValidPageVerify` als static public
      exposed fuer Unit-Tests ohne ODBC-Roundtrip.)_
- [x] **10.3c** `OdbcMssqlActionService`: Snapshot Create / List /
      Restore / Drop. Multi-Data-File-Support via `sys.master_files`-Query. — `M`
      _(erledigt: `1678f28`. Naming
      `<db>_Snapshot_<yyyyMMddHHmmss>` per Lars-Entscheidung.)_
- [x] **10.3d** `OdbcMssqlActionService`: Backup (mit
      `xp_instance_regread` fuer BackupRoot) + Backup-Browser (msdb-
      Query, nur Fulls, TOP 100) + Restore. Backup-Layout flach:
      `<root>\<db>\<db>-<yyyyMMdd_HHmm>.bak` (Lars-Entscheidung). — `M`
      _(erledigt: `9fc30ff`.)_
- [x] **10.3e** `OdbcMssqlActionService`: Sessions-Kill (KILL-Loop),
      CHECKDB, Index-Rebuild (Table-Liste + pro-Table-Notice), Shrink-Log
      (Log-Files aus `sys.master_files` type=1). InfoMessage-Streaming
      aktiv. — `M`
      _(erledigt: `47f834f`.)_
- [x] **10.3f** `OdbcMssqlActionService`: Cluster-Health via
      `sys.dm_hadr_availability_replica_states`. — `S`
      _(erledigt: `48c1a7a`. Output als Text-Notice pro Replica,
      konsistent zum FOC-SQL-Weg.)_
- [x] **10.4** `MainWindowViewModel` + relevante Sub-VMs dispatchen
      pro Action auf Backend:
      `switch { FocSql → TerminalBus, OdbcDirect → _odbcActions }`.
      SemaphoreSlim pro Server, Notices als Live-Feedback. — `M`
      _(erledigt in vier Commits:
      `19ba184` (10.4a+b Simple-Actions), `9d90650` (10.4c Backup-
      Browser Backend-Aware), `683cefb` (10.4d Snapshot-Select-Dialog
      fuer Restore/Drop). `TerminalBus.InjectNotice` + `IDTM_DATA.
      GetMssqlActions` als Infrastruktur; `MainWindowViewModel.
      TryGetOdbcActions` + `RunOdbcActionAsync` als Dispatcher-Helper.
      DbConfigurationViewModel + SessionsViewModel + BackupBrowser-
      ViewModel bekommen `OdbcActions`-Property und switchen intern.
      Neuer `MssqlSnapshotSelectWindow` (analog OracleRestoreSelect-
      Window) fuer Restore + Drop im OdbcDirect-Modus — bei FocSql
      bleibt der interaktive pwsh-Read-Host-Weg.)_
- [x] **10.5** Sichtbarkeit: bei `OdbcDirect` `CopyToSambaVisible`
      = false, `SyncToTestVisible` = false. — `S`
      _(erledigt: dieser Commit. Beide Properties Default true, in
      ApplyStats bei OdbcDirect-DB auf false. Buttons „Clone" und
      „DB → Samba" in MainWindow.axaml haengen an den Bindings.
      MainWindowViewModel-Fallback-Guards in Backup/Clone/DbToSamba-
      Commands rufen bei OdbcDirect eine InjectNotice („nicht
      verfuegbar") — sicheres Netz falls ein Command doch getriggert
      wird.)_

**Was NICHT in Phase 10:**
- Copy-Database-ToSamba (FS-Operation, kein SQL-Weg)
- Sync-Database-ToTest (Multi-Step-PS-Orchestrierung)
- Mail-Versand nach Backup (bewusst weg im DMZ)
- Get-DatabaseStats-Konsolidierung (bleibt ODBC-Weg wie schon)

**Sicherheit:** SQL-Login-Credentials sind fuer BACKUP/RESTORE/
ALTER DATABASE zwingend `sysadmin`-privilegiert; das ist Realitaet
fuer Backup-Tools. Kein neuer Angriffsvektor gegenueber Status quo.
Windows-Credentials werden fuer OdbcDirect-Server nicht gebraucht —
Phase-9-Infrastruktur bleibt inaktiv, aber intakt fuer echte
WinRM-Multi-Zone-Setups.

#### Phase 11 — OLVM-Snapshot fuer Oracle (`v2.3.0` geplant)

Kontext: Oracle-Snapshots werden bei Lars ueber ein Ansible-Playbook
auf dem zentralen Manager-Host `DBMANAGER01` gefahren. Das Playbook
faehrt die DB + VM runter, macht ueber OLVM einen VM-Snapshot und
startet beides wieder. DTM soll das per Button ausloesen — als
zusaetzlicher Weg neben dem existierenden `Set-Snapshot` (das den
Oracle-DB-Snapshot per Restore Point macht, ohne VM anzufassen).

Design-Skizze:
- FOC-SQL: neue Funktion, die sich per SSH auf `DBMANAGER01`
  verbindet und `cd ansible && ansible-playbook <name>.yml -e ...`
  ausfuehrt. `-tt` fuer Live-Output im pwsh-Tab.
- DTM: eigene Action-Gruppe "OLVM" in der Oracle-Sicht, mit
  ConfirmWindow davor (DB + VM Shutdown ist destruktiv).
- Multi-Snapshot-Auswahl fuer Restore/Delete: neuer
  `OlvmSnapshotSelectWindow`, analog zum
  `MssqlSnapshotSelectWindow` aus Phase 10.4d.

Sub-Items:

- [x] **11.1** 📦 FOC-SQL: `Invoke-OlvmSnapshot` (create). Ansible-
      Playbook-Aufruf auf `DBMANAGER01`. — `M`
      _(erledigt: FOC-SQL `a8bbef2`. Playbook
      `olvm-create-dbvm-snapshot.yml` mit `-e "vm_dns=<Kurzname>"`;
      SSH `-tt` fuer Live-Output, Log-Kaskade `ts` + `tee` fuer
      Audit-Trail auf dem Manager beibehalten. Drei-Punkt-Checkliste
      psm1+psd1+_ToExport.ps1 eingehalten.)_
- [x] **11.2** DTM: Action-Gruppe "OLVM" mit Button „VM-Snapshot"
      fuer Oracle-DBs. ConfirmWindow mit 5-Schritt-Ablauf und
      „Dauer mehrere Minuten"-Warnung. — `S`
      _(erledigt: `OlvmVisible`-Property (Default false, in
      ApplyStats bei Oracle true), `OlvmSnapshotCommand` mit
      `Confirm→RunSimpleAction("Invoke-OlvmSnapshot", ...)`,
      neue Gruppe "OLVM" in MainWindow.axaml zwischen SNAPSHOTS
      und BACKUPS. `RunSimpleAction` reicht bei Oracle `-Server`
      als null durch — passt zum FOC-SQL-Wrapper der intern
      hardcoded auf DBMANAGER01 connectet.)_
- [x] **11.3** DTM: Snapshot-Liste via OLVM-REST-API (statt Ansible).
      `ORACLE_REST.GetSnapshotsAsync(vmId)` mit endpoint
      `GET /api/vms/{id}/snapshots`. `OlvmSnapshotService.ListAsync`
      filtert den "active"-Eintrag raus und mapt zu `OlvmSnapshotInfo`
      (Id, Description, CreatedAt, Status, Type). VM-UUID kommt aus
      `Database_Info.Id` (bereits von REST beim Namen-Laden gesetzt).
      Der User hat sich fuer REST statt Playbook entschieden, weil
      List rein lesend ist und die Latenz sonst zu hoch waere. — `M`
      _(erledigt: dieser Commit. IDTM_DATA.GetOlvmSnapshotService baut
      pro Aufruf einen frischen REST-Client (trustAllCertificates=true,
      analog ORACLE_ODBC).)_
- [ ] **11.4** 📦 FOC-SQL: `Restore-OlvmSnapshot` — Rollback auf
      einen ausgewaehlten Snapshot (per Ansible-Playbook, VM
      Shutdown → Restore → Start). Sobald verfuegbar: den
      "RestoreEnabled=false"-Placeholder im OlvmSnapshotSelectViewModel
      auf true setzen + Wrapper aufrufen. — `M`
- [ ] **11.5** 📦 FOC-SQL: `Remove-OlvmSnapshot` — alte VM-
      Snapshots loeschen (per Ansible-Playbook). Sobald verfuegbar:
      `DeleteEnabled=false` im ViewModel auf true. — `S`
- [x] **11.6** DTM: `OlvmSnapshotSelectWindow` (analog
      `MssqlSnapshotSelectWindow` aus 10.4d) — DataGrid mit
      Snapshots (Description, CreatedAt, Status, Type), Buttons
      „Restore" und „Löschen" beide DISABLED bis 11.4/11.5
      bereit sind. ToolTip erklaert warum. Prominenter gelber
      Hinweis-Banner im Dialog. Trigger aus zwei Commands
      (OlvmRestoreSnapshotCommand, OlvmRemoveSnapshotCommand) mit
      vorgewaehlter Aktion. UI-Buttons "VM-Restore" + "VM-Remove"
      in der OLVM-Gruppe neben "VM-Snapshot". — `M`
      _(erledigt: dieser Commit.)_

**Klarungen vor 11.1 (Lars liefert):**
1. Playbook-Dateiname (relativ zu `~oracle/ansible/`).
2. Parameter-Format: `-e "key=value"` inline, welche Keys?
3. Snapshot-Namensgebung: automatisch mit Timestamp im Playbook
   oder User-Input in DTM?
4. `DBMANAGER01` hardcoded oder als Config-Feld?

**Sicherheit:** SSH-Login als `oracle@DBMANAGER01` per Key-Auth
(gleiches Setup wie andere Oracle-Ziele — Pageant / OpenSSH
IdentityAgent). Kein Passwort in Klartext.

#### Phase 12 — TitleBar-Rollout-Fortsetzung (Skill-Compliance-Follow-up)

Der `TitleBar`-UserControl unter `Views/Controls/TitleBar.axaml` wird
aktuell nur von `AboutWindow` und `ConnectionManagerWindow` genutzt
(reine Text-Titel, ohne Dialog-Result-Semantik). Die 12 restlichen
Dialoge behalten ihre eigene Titelleiste. Um sie auch umzuziehen:

- [ ] **12.1** TitleBar um Content-Slot fuer Titel-Bereich erweitern
      (`TitleContent`-DependencyProperty oder inner `ContentPresenter`
      mit angesteuertem Slot), damit die 6 Icon-Titelleisten mit
      StackPanel + Glyph + Text auf `<c:TitleBar>` umgestellt werden
      koennen: `ConfirmWindow`, `BackupBrowserWindow`,
      `OracleRestoreSelectWindow`, `MssqlSnapshotSelectWindow`,
      `DbConfigurationWindow`, `FatalErrorWindow`. — `S`
- [ ] **12.2** Fuer die 6 Fenster mit Dialog-Result-Semantik
      (`EditConnectionWindow` → bool, `TimePickerWindow` →
      `TimePickResult`, `SessionsWindow`, `UpdatePromptWindow`,
      `OlvmSnapshotSelectWindow`, `MssqlSnapshotSelectWindow`):
      `<c:TitleBar x:Name="TitleBar" Title="..."/>` deklarieren,
      im Code-Behind-Konstruktor `TitleBar.CloseResult = ...` setzen.
      Fuer `TimePickerWindow` heisst das: `TitleBar.CloseResult =
      TimePickResult.Cancel();`. — `S`
- [ ] **12.3** `ChromeWindow`-Basisklasse aufraeumen: sobald ALLE Fenster
      auf `<c:TitleBar>` umgestellt sind, sind die geerbten
      `OnTitleBarPointerPressed`/`OnTitleBarDoubleTapped`-Handler
      unbenutzt und koennen entfernt werden. — `S`

Kein funktionaler Gewinn — reines Konsolidierungs-Refactor. Sinnvoll
gebuendelt mit einer anderen UI-Arbeit; kein eigener Release-Anlass.

#### Phase 8 — Erweiterte Stats & Transaktions-Management (Future)

Lars-Idee aus dem v2.0.0-Test: ein eigener Button bzw. Dialog, der **mehr
Stats** als das heutige Info-Card abruft (z. B. tatsächliche Buffer-Hit-
Ratio, aktuelle Lock-Waits, langlebige Sessions, **offene Transaktionen
mit Kill-Möglichkeit**). MSSQL via `sys.dm_tran_active_transactions` +
`sys.dm_exec_sessions`, Oracle via `v$transaction` + `v$session`. Idealerweise
mit dem gleichen Pattern wie SessionsWindow: Liste + Pre-Kill-Confirm.

Noch nicht geplant — separater Auftrag wenn relevant.

#### Phase 6 — Multi-Server-Support (`L`, ein Breaking-Change-Block, **`v2.0.0`**)

Heute reduziert die App die Connection-Liste auf ein
`Dictionary<ServerTyp, DB_SERVER>` — pro Typ überlebt nur ein Server,
die anderen verschwinden still beim Start. Mit mehreren MSSQL/Oracle/…-
Hosts in einer Umgebung (z. B. `FOC-SQL01` + `DEVFOC-SQL01`,
`olvm-mgm.lhp.intern` + `olvm-mgm.devlhp.intern` + `olv-mgm.dmz`) muss
DTM das echte Multi-Server-Modell tragen.

Entscheidungen:
- **Tree:** zweistufig (Typ → Server → DB), 3 Ebenen.
- **Identität persistiert:** `ConnectionEntry.Key` bleibt der Typ (kein
  Schema-Bruch der `connections.json`); die Composite-Identität wird
  zur Laufzeit aus `(Typ, Server)` gebildet.
- **FOC-SQL-Aufrufe:** alle Wrapper bekommen den `-Server`-Parameter
  explizit mitgegeben (kein Verlass mehr auf `$global:Server`-Default).
- **Connection-Manager-UI:** unverändert — User legt einfach mehrere
  Zeilen mit gleichem Typ + unterschiedlichen Hostnames an.
- **Künftige DB-Typen** (MariaDB, MySQL, PostgreSQL, DB2, MongoDB) sind
  hier nicht im Scope, aber das Tree-/Datenmodell wird so gehalten,
  dass weitere `ServerTyp`-Werte mit eigener Backend-Strategie später
  einfach addiert werden können. Das **Modul-Renaming** („FOC-SQL"
  ist als Name irreführend, weil es Oracle mitmacht) ist eigener
  späterer Punkt — nicht in Phase 6.

Sub-Items:

- [ ] **6.1** `ServerIdentity`-Record `(ServerTyp, string Server)` mit `Equals`/
      `GetHashCode`; `DB_SERVER` bekommt Identity-Property. Schema kompatibel
      zu vorhandenem `ConnectionEntry`. — `S`
- [x] **6.2** `Composition/ServiceRegistrations` + `App.axaml.cs`: aus
      `Dictionary<ServerTyp, DB_SERVER>` wird `IReadOnlyList<DB_SERVER>`. — `S`
      _(erledigt in Refactor-Commit `3c3f936`)_
- [x] **6.3** `IDTM_DATA`/`DTM_DATA`: Methoden nehmen `ServerIdentity` statt
      `ServerTyp`. Interner `Dictionary<ServerIdentity, DB_SERVER>`-Lookup für
      O(1); `KeyNotFoundException` mit klarer Meldung bei unbekannter
      Identität. — `M` _(erledigt in `3c3f936`)_
- [x] **6.4** Tree: neue `ServerGroupNodeViewModel` als statischer Top-Level-
      Container pro Typ; bestehende `ServerNodeViewModel` zeigt Hostname statt
      Typ-Enum; `DatabaseNodeViewModel` bekommt expliziten `ServerIdentity`-
      Kontext (alter Konstruktor mit nur `ServerTyp` bleibt als Test-Convenience).
      — `M` _(erledigt in `3c3f936`)_
- [x] **6.5** `MainWindowViewModel`: `BuildRootNodes()` baut Gruppen aus der
      Server-Liste (alphabetisch sortiert pro Gruppe); `OnSelectedNodeChanged`
      handhabt drei Typen (Gruppe → no-op, Server → DB-Liste, DB → Stats);
      `LoadStatsAsync` nutzt `db.ServerIdentity`. — `M` _(erledigt in `3c3f936`)_
- [x] **6.6** `TerminalBus.RunFocSqlAction`/`RunFocSqlSimple` haben optionalen
      `string? server`-Parameter; bei nicht-null wird `-Server '<host>'` ans
      Cmdlet angehängt. `RunFocSqlServerAction` war schon ok. — `M`
      _(erledigt in `3c3f936`)_
- [x] **6.7** `ServerParamFor(db)` liefert bei MSSQL den Hostname, bei Oracle
      `null` (Oracle adressiert via FQDN im `-Database`). `RunDbActionAsync`,
      `RunSimpleAction` und der `BackupBrowserViewModel`/`-Service` reichen den
      Wert durch. — `S` _(erledigt in `3c3f936`)_
- [x] **6.8** Tests (`DtmDataTests` komplett auf List/ServerIdentity, neuer
      „Multiple servers same type"-Test, `MainWindowViewModelTests.StubData`
      implementiert neues Interface). 278/278 grün. CLAUDE.md mit
      Sub-Item-Häkchen versehen (dieser Commit). — `M`
- [x] **6.9** Release `v2.0.0` (Breaking-Change-Major-Bump wegen
      Datenmodell und FOC-SQL-Aufruf-Pattern). — `S`
      _(erledigt; alle drei Bundles published — Windows-ZIP, Linux-tar.gz,
      AppImage. Lars hat vor dem Tag verifiziert.)_

---

## Definition of Done (Checkliste)

- [ ] `Directory.Build.props`, `.editorconfig`, `.gitignore`, `README`, `LICENSE` vorhanden
- [ ] `.vscode/` mit launch/tasks inkl. Hard-Clean + Log-Öffnen-Task
- [x] Testprojekt vorhanden, `dotnet test` grün
- [ ] CI-Action (build+test) und Release-Action (Win/Linux/AppImage, Node 24) eingerichtet
- [ ] MinVer aktiv, Release an Tag `vX.Y.Z` gekoppelt
- [ ] Alle Fenster über `ChromeWindow`, **resizable** ✓ — InfoBox mit BMC-Button noch offen
- [x] Avalonia ≥ 12.0.4, v12-Konventionen eingehalten
- [ ] Globaler Exception-Handler greift → NLog Fatal + Dialog
- [x] NLog loggt umfassend, **keine Secrets** im Log; Logs nach Änderung geprüft
- [x] Secrets sicher abgelegt (DPAPI/libsecret), nichts im Klartext committet
- [x] App-Icon einheitlich (Fenster + Exe + AppImage)
