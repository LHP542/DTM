using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DTM.ViewModels;
using DTM.ViewModels.TreeNodes;
using NLog;

namespace DTM.Data.Api;

/// <summary>
/// Saemtliche Zugriffe der REST-API auf die Avalonia-Oberflaeche laufen hier
/// durch. Jede Methode marshalt ueber <see cref="Dispatcher.UIThread"/> und
/// kehrt erst zurueck, wenn die UI-Aktion abgeschlossen ist — der HTTP-Handler
/// kann danach direkt antworten.
///
/// <para><b>Warum das der richtige Weg fuer automatisierte UI-Pruefungen ist:</b>
/// Screenshots entstehen ueber Avalonias eigenes
/// <see cref="RenderTargetBitmap"/> und Klicks ueber
/// <see cref="ICommand.Execute"/> — alles innerhalb des Prozesses. Der Weg
/// von aussen (SetForegroundWindow, mouse_event, PrintWindow, UI-Automation)
/// sieht fuer verhaltensbasierte Virenscanner wie Fernsteuerungs-Schadsoftware
/// aus und wird auf verwalteten Rechnern blockiert — real passiert am
/// 2026-08-19 mit Trend Micro auf dem Arbeitslaptop.</para>
/// </summary>
internal sealed class DtmUiActions
{
    private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

    private readonly bool _allowDestructive;

    public DtmUiActions(bool allowDestructive) => _allowDestructive = allowDestructive;

    private static IClassicDesktopStyleApplicationLifetime? Lifetime =>
        Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public Task<Window?> GetMainWindowAsync() =>
        Dispatcher.UIThread.InvokeAsync(() => Lifetime?.MainWindow).GetTask();

    public Task<MainWindowViewModel?> GetMainVmAsync() =>
        Dispatcher.UIThread.InvokeAsync(() =>
            Lifetime?.MainWindow?.DataContext as MainWindowViewModel).GetTask();

    /// <summary>
    /// Momentaufnahme des App-Zustands. Wird komplett <b>innerhalb</b> des
    /// Dispatchers gebaut: die ViewModels und ihre
    /// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
    /// gehoeren dem UI-Thread. Wer sie vom HTTP-Thread aus liest, bekommt
    /// bestenfalls einen veralteten Stand — real passiert: der Baum meldete
    /// "keine Datenbanken", waehrend im Log 136 geladene standen.
    /// </summary>
    public Task<StateSnapshot> GetStateAsync() =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Window? win = Lifetime?.MainWindow;
            var vm = win?.DataContext as MainWindowViewModel;

            string? selection = vm?.SelectedNode switch
            {
                DatabaseNodeViewModel db => $"{db.ServerIdentity.Server}/{db.Header}",
                ServerNodeViewModel s => s.Header,
                NodeViewModelBase n => n.Header,
                _ => null,
            };

            IReadOnlyList<string> windows = Lifetime is { } lt
                ? lt.Windows.OrderByDescending(w => w.IsActive)
                     .Select(w => w.Title ?? w.GetType().Name).ToList()
                : [];

            return new StateSnapshot(
                SelectedNode: selection,
                StatusBar: vm?.StatusBar,
                DbName: vm?.DbName,
                DbHost: vm?.DbHost,
                DbStatus: vm?.DbStatus,
                DbVersion: vm?.DbVersion,
                DbSize: vm?.DbSize,
                Recovery: vm?.RecoveryOrArchiveMode,
                ActiveSessions: vm?.ActiveSessionsCount,
                OpenWindows: windows,
                Width: win is null ? null : (int)Math.Round(win.ClientSize.Width),
                Height: win is null ? null : (int)Math.Round(win.ClientSize.Height),
                IsMaximized: win?.WindowState == WindowState.Maximized);
        }).GetTask();

    /// <summary>
    /// Momentaufnahme des Server-/Datenbank-Baums — ebenfalls vollstaendig im
    /// Dispatcher, siehe <see cref="GetStateAsync"/>.
    /// </summary>
    public Task<IReadOnlyList<ServerGroupSnapshot>?> GetTreeAsync() =>
        Dispatcher.UIThread.InvokeAsync<IReadOnlyList<ServerGroupSnapshot>?>(() =>
        {
            if (Lifetime?.MainWindow?.DataContext is not MainWindowViewModel vm) return null;

            return vm.RootNodes.Select(group => new ServerGroupSnapshot(
                Type: group.Header,
                Servers: group.Children.OfType<ServerNodeViewModel>().Select(s => new ServerSnapshot(
                    Name: s.Header,
                    Host: s.ServerHost,
                    // Kinder werden beim Aufklappen nachgeladen; solange sie
                    // fehlen, heisst das "noch nicht geladen", nicht "keine da".
                    DatabasesLoaded: s.Children.Count > 0,
                    // Nur der Name — die Beschriftung im Baum haengt zusaetzlich
                    // den Status an ("ALKIS (up)"), und genau der Name ist es,
                    // den /select-node erwartet.
                    Databases: s.Children.OfType<DatabaseNodeViewModel>()
                        .Select(d => d.Database.Name).ToList()))
                    .ToList())).ToList();
        }).GetTask();

    /// <summary>Titel aller offenen Fenster, aktives zuerst.</summary>
    public Task<IReadOnlyList<string>> GetOpenWindowsAsync() =>
        Dispatcher.UIThread.InvokeAsync<IReadOnlyList<string>>(() =>
        {
            if (Lifetime is not { } lt) return Array.Empty<string>();
            return lt.Windows
                .OrderByDescending(w => w.IsActive)
                .Select(w => w.Title ?? w.GetType().Name)
                .ToList();
        }).GetTask();

    /// <summary>
    /// PNG-Abzug eines Fensters ueber <see cref="RenderTargetBitmap"/>.
    /// <paramref name="target"/>: "main" (Default) oder "active".
    /// Rendern laeuft auf dem UI-Thread — bei einem selten gerufenen Endpoint
    /// lohnt das Auslagern des Encodings nicht.
    /// </summary>
    public Task<byte[]?> ScreenshotAsync(string target) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Lifetime is not { } lt) return (byte[]?)null;

            Window? win = string.Equals(target, "active", StringComparison.OrdinalIgnoreCase)
                ? lt.Windows.FirstOrDefault(w => w.IsActive) ?? lt.MainWindow
                : lt.MainWindow;
            if (win is null) return null;

            Size size = win.ClientSize;
            if (size.Width < 1 || size.Height < 1) return null;

            PixelSize pixel = new(
                Math.Max(1, (int)Math.Ceiling(size.Width)),
                Math.Max(1, (int)Math.Ceiling(size.Height)));

            using RenderTargetBitmap rtb = new(pixel, new Vector(96, 96));
            rtb.Render(win);
            using MemoryStream ms = new();
            rtb.Save(ms, PngBitmapEncoderOptions.Default);
            return ms.ToArray();
        }).GetTask();

    /// <summary>
    /// Klickt ein benanntes Control. Bei einem <see cref="Button"/> mit
    /// gebundenem Command wird dieser direkt ausgefuehrt (zuverlaessiger als
    /// ein Event, und <c>CanExecute</c> laesst sich vorher pruefen); bei
    /// Buttons mit Click-Handler wird das <see cref="Button.ClickEvent"/>
    /// ausgeloest. Beides bleibt innerhalb des Prozesses.
    /// </summary>
    public Task<ActionResult> ClickAsync(string elementId) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_allowDestructive && DestructiveGuard.IsDestructiveElement(elementId))
                return ActionResult.Blocked(
                    $"'{elementId}' loest eine Aktion aus, die Datenbanken veraendert. "
                    + "Die API laeuft im Nur-Beobachten-Modus (Api.AllowDestructive=false).");

            (Control? control, IReadOnlyList<string> available) = FindNamed(elementId);
            if (control is null) return ActionResult.NotFound(available);

            // Reihenfolge beachten: In Avalonia erbt CheckBox von ToggleButton
            // und das von Button — die spezielleren Faelle muessen zuerst
            // stehen, sonst faengt der Button-Zweig sie ab.
            switch (control)
            {
                case CheckBox cb:
                    cb.IsChecked = !(cb.IsChecked ?? false);
                    return ActionResult.Ok;

                case ToggleButton toggle:
                    toggle.IsChecked = !(toggle.IsChecked ?? false);
                    return ActionResult.Ok;

                case Button btn:
                    if (btn.Command is not null)
                    {
                        if (!btn.Command.CanExecute(btn.CommandParameter))
                            return ActionResult.Conflict($"'{elementId}' ist derzeit nicht ausfuehrbar.");
                        btn.Command.Execute(btn.CommandParameter);
                        return ActionResult.Ok;
                    }

                    // Die Dialog-Buttons (Abbrechen, Speichern, Schliessen)
                    // haengen an Click-Handlern im Code-Behind statt an
                    // Commands. Ohne diesen Zweig liessen sich Dialoge zwar
                    // oeffnen, aber nicht wieder schliessen — womit die API
                    // fuer Bildschirmfotos von Dialogen unbrauchbar waere.
                    if (!btn.IsEffectivelyEnabled)
                        return ActionResult.Conflict($"'{elementId}' ist derzeit deaktiviert.");
                    btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return ActionResult.Ok;

                default:
                    return ActionResult.Unsupported(
                        $"'{elementId}' ist ein {control.GetType().Name} — dafuer ist keine Klick-Semantik hinterlegt.");
            }
        }).GetTask();

    /// <summary>
    /// Fuehrt einen Command des <c>MainWindowViewModel</c> ueber seinen Namen
    /// aus (mit oder ohne "Command"-Suffix). Zuverlaessiger als ein Klick, weil
    /// er nicht davon abhaengt, ob ein Button gerade im Visual Tree haengt.
    /// </summary>
    public Task<ActionResult> ExecuteCommandAsync(string commandName) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_allowDestructive && DestructiveGuard.IsDestructiveCommand(commandName))
                return ActionResult.Blocked(
                    $"Command '{commandName}' veraendert Datenbanken. "
                    + "Die API laeuft im Nur-Beobachten-Modus (Api.AllowDestructive=false).");

            if (Lifetime?.MainWindow?.DataContext is not MainWindowViewModel vm)
                return ActionResult.Conflict("MainWindow-ViewModel ist nicht verfuegbar.");

            string wanted = commandName.EndsWith("Command", StringComparison.OrdinalIgnoreCase)
                ? commandName
                : commandName + "Command";

            List<string> available = [];
            PropertyInfo? match = null;
            foreach (PropertyInfo prop in vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!typeof(ICommand).IsAssignableFrom(prop.PropertyType)) continue;
                available.Add(prop.Name);
                if (string.Equals(prop.Name, wanted, StringComparison.OrdinalIgnoreCase)) match = prop;
            }

            if (match is null) return ActionResult.NotFound(available.Order().ToList());
            if (match.GetValue(vm) is not ICommand cmd)
                return ActionResult.Conflict($"'{match.Name}' liefert keinen Command.");
            if (!cmd.CanExecute(null))
                return ActionResult.Conflict($"'{match.Name}' ist derzeit nicht ausfuehrbar (CanExecute=false).");

            cmd.Execute(null);
            return ActionResult.Ok;
        }).GetTask();

    /// <summary>Schreibt Text in eine benannte <see cref="TextBox"/>.</summary>
    public Task<ActionResult> SetTextAsync(string elementId, string text) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            (Control? control, IReadOnlyList<string> available) = FindNamed(elementId);
            if (control is null) return ActionResult.NotFound(available);
            if (control is not TextBox tb)
                return ActionResult.Unsupported($"'{elementId}' ist ein {control.GetType().Name}, keine TextBox.");

            tb.Text = text;
            return ActionResult.Ok;
        }).GetTask();

    /// <summary>
    /// Waehlt einen Knoten im Datenbank-Baum. <paramref name="path"/> ist
    /// entweder "&lt;Server&gt;" oder "&lt;Server&gt;/&lt;Datenbank&gt;";
    /// die Auswahl einer Datenbank stoesst wie in der UI das Laden der Stats an.
    /// </summary>
    public Task<ActionResult> SelectNodeAsync(string path) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Lifetime?.MainWindow?.DataContext is not MainWindowViewModel vm)
                return ActionResult.Conflict("MainWindow-ViewModel ist nicht verfuegbar.");

            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is 0 or > 2)
                return ActionResult.Conflict("Pfad erwartet: '<Server>' oder '<Server>/<Datenbank>'.");

            List<string> available = [];
            ServerNodeViewModel? server = null;
            foreach (NodeViewModelBase group in vm.RootNodes)
            {
                foreach (NodeViewModelBase child in group.Children)
                {
                    if (child is not ServerNodeViewModel s) continue;
                    available.Add(s.Header);
                    if (string.Equals(s.Header, parts[0], StringComparison.OrdinalIgnoreCase)) server = s;
                }
            }
            if (server is null) return ActionResult.NotFound(available.Order().ToList());

            if (parts.Length == 1)
            {
                vm.SelectedNode = server;
                return ActionResult.Ok;
            }

            // Datenbank-Ebene: Kinder muessen geladen sein. Der Server-Knoten
            // laedt sie beim Selektieren nach, deshalb erst selektieren und
            // dann in einem zweiten Aufruf die Datenbank waehlen — ein
            // synchrones Warten wuerde hier den UI-Thread blockieren.
            if (server.Children.Count == 0)
            {
                vm.SelectedNode = server;
                return ActionResult.Conflict(
                    $"Datenbanken von '{parts[0]}' werden geladen. Den Aufruf mit dem vollen Pfad kurz danach wiederholen.");
            }

            // Der Baum zeigt "<Name> (<Status>)" an. Fuer die API waere es
            // laestig, den Status mittippen zu muessen — deshalb matcht sowohl
            // der reine Datenbankname als auch die vollstaendige Beschriftung.
            List<string> dbs = [];
            foreach (NodeViewModelBase child in server.Children)
            {
                if (child is not DatabaseNodeViewModel db) continue;
                dbs.Add(db.Database.Name);
                if (string.Equals(db.Database.Name, parts[1], StringComparison.OrdinalIgnoreCase)
                    || string.Equals(db.Header, parts[1], StringComparison.OrdinalIgnoreCase))
                {
                    vm.SelectedNode = db;
                    return ActionResult.Ok;
                }
            }
            return ActionResult.NotFound(dbs.Order().ToList());
        }).GetTask();

    /// <summary>Alle benannten Controls der offenen Fenster — Hilfe beim
    /// Herausfinden, was <c>/click</c> und <c>/text</c> ansprechen koennen.</summary>
    public Task<IReadOnlyList<string>> ListElementsAsync() =>
        Dispatcher.UIThread.InvokeAsync<IReadOnlyList<string>>(() => FindNamed(null).Available).GetTask();

    /// <summary>
    /// Sucht ein benanntes Control ueber alle offenen Fenster — aktives Fenster
    /// zuerst, dann das Hauptfenster, dann der Rest. Liefert zusaetzlich alle
    /// gefundenen Namen, damit ein 404 gleich sagt, was es stattdessen gibt.
    /// <paramref name="elementId"/> = <c>null</c> sammelt nur die Namen.
    /// </summary>
    private static (Control? Control, IReadOnlyList<string> Available) FindNamed(string? elementId)
    {
        if (Lifetime is not { } lt) return (null, Array.Empty<string>());

        List<Window> windows = [];
        if (lt.Windows.FirstOrDefault(w => w.IsActive) is { } focused) windows.Add(focused);
        if (lt.MainWindow is { } main && !windows.Contains(main)) windows.Add(main);
        foreach (Window w in lt.Windows) if (!windows.Contains(w)) windows.Add(w);

        Control? found = null;
        SortedSet<string> names = new(StringComparer.Ordinal);
        foreach (Window win in windows)
        {
            foreach (Visual v in win.GetVisualDescendants())
            {
                if (v is not Control c || string.IsNullOrEmpty(c.Name)) continue;
                names.Add(c.Name);
                if (found is null && elementId is not null && c.Name == elementId) found = c;
            }
        }
        return (found, names.ToList());
    }

    /// <summary>Zustands-Momentaufnahme fuer <c>GET /state</c>.</summary>
    internal sealed record StateSnapshot(
        string? SelectedNode,
        string? StatusBar,
        string? DbName,
        string? DbHost,
        string? DbStatus,
        string? DbVersion,
        string? DbSize,
        string? Recovery,
        string? ActiveSessions,
        IReadOnlyList<string> OpenWindows,
        int? Width,
        int? Height,
        bool IsMaximized);

    internal sealed record ServerGroupSnapshot(string Type, IReadOnlyList<ServerSnapshot> Servers);

    internal sealed record ServerSnapshot(
        string Name, string Host, bool DatabasesLoaded, IReadOnlyList<string> Databases);

    /// <summary>Ergebnis einer UI-Aktion, uebersetzt in ApiEndpoints zu HTTP-Status.</summary>
    internal sealed record ActionResult(
        bool Success,
        string? Error,
        ActionFailure Failure,
        IReadOnlyList<string> Available)
    {
        public static readonly ActionResult Ok = new(true, null, ActionFailure.None, []);

        public static ActionResult NotFound(IReadOnlyList<string> available) =>
            new(false, "Nicht gefunden", ActionFailure.NotFound, available);

        public static ActionResult Blocked(string reason) =>
            new(false, reason, ActionFailure.Blocked, []);

        public static ActionResult Conflict(string reason) =>
            new(false, reason, ActionFailure.Conflict, []);

        public static ActionResult Unsupported(string reason) =>
            new(false, reason, ActionFailure.Unsupported, []);
    }

    internal enum ActionFailure
    {
        None,
        NotFound,
        /// <summary>Durch <see cref="DestructiveGuard"/> gesperrt.</summary>
        Blocked,
        /// <summary>Zustand passt gerade nicht (CanExecute=false, VM fehlt …).</summary>
        Conflict,
        /// <summary>Control-Typ beherrscht die Aktion nicht.</summary>
        Unsupported,
    }
}
