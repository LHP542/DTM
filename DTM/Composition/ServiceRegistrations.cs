using DTM.Config;
using DTM.Data.Terminal;
using DTM.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DTM.Composition;

/// <summary>
/// Composition-Root fuer DTM. Buendelt die Service- und ViewModel-
/// Registrierungen an einer Stelle und wird in <see cref="App.Initialize"/>
/// einmal aufgerufen.
///
/// Bewusste Abweichung von der Roadmap-Formulierung („+ Hosting"):
/// Microsoft.Extensions.Hosting wuerde IHost/IConfiguration/ILogger
/// mitbringen. Davon nutzt DTM nichts (NLog konfiguriert sich selbst,
/// die JSON-Stores haben ihr eigenes Schema, ein BackgroundService-
/// Lifecycle kollidiert mit Avalonias eigenem Lifecycle). Daher nur
/// das schlanke <c>Microsoft.Extensions.DependencyInjection</c>-Paket.
/// Falls spaeter Config/Logging via DI kommen, kann <c>HostBuilder</c>
/// jederzeit nachgezogen werden.
/// </summary>
internal static class ServiceRegistrations
{
    public static IServiceCollection AddDtmServices(this IServiceCollection services)
    {
        // --- Daten-/Infrastruktur-Schicht (Singletons) ---
        services.AddSingleton<ODBC_Factory>();

        // Multi-Server: aus jedem ConnectionEntry wird ein DB_SERVER. Mehrere
        // Eintraege mit gleichem Typ (z. B. zwei MSSQL-Hosts) sind erlaubt;
        // die Composite-Identity (Typ, Hostname) macht sie unterscheidbar.
        services.AddSingleton<IReadOnlyList<DB_SERVER>>(_ =>
        {
            List<DB_SERVER> list = new();
            foreach (ConnectionEntry entry in ConnectionStore.Load())
            {
                if (Enum.TryParse<DB_SERVER.ServerTyp>(entry.Key, ignoreCase: true, out var typ))
                    list.Add(new DB_SERVER(typ, entry.ToCredential(), entry.Backend));
            }
            return list;
        });

        services.AddSingleton<IDTM_DATA>(sp =>
            new DTM_DATA(
                sp.GetRequiredService<IReadOnlyList<DB_SERVER>>(),
                sp.GetRequiredService<ODBC_Factory>()));

        // Strukturierte FOC-SQL-Aufrufe ueber eigenen PS-Runspace (komplementaer
        // zum TerminalBus, der Text in den pwsh-Tab schreibt).
        services.AddSingleton<OracleRestoreService>();
        services.AddSingleton<BackupBrowserService>();

        // Update-Check gegen GitHub Releases (v2.3.0-prep). Singleton, weil
        // der Service intern einen Cache pro App-Start haelt — 2. Check nach
        // dem 1. gibt sonst noch einen GitHub-API-Call.
        services.AddSingleton<DTM.Updater.UpdateService>();

        // --- ViewModels (Transient — neue Instanz pro Aufloesung) ---
        // MainWindowViewModel braucht den IServiceProvider, um untergeordnete
        // VMs (ConnectionManager, Sessions, TimePicker) zur Laufzeit aufzu-
        // loesen. Daher explizite Factory statt Default-Activator.
        services.AddTransient<MainWindowViewModel>(sp => new MainWindowViewModel(
            sp.GetRequiredService<IDTM_DATA>(),
            sp.GetRequiredService<IReadOnlyList<DB_SERVER>>(),
            sp));

        services.AddTransient<ConnectionManagerViewModel>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<TimePickerViewModel>();
        services.AddTransient<OracleRestoreSelectViewModel>();
        services.AddTransient<BackupBrowserViewModel>();
        services.AddTransient<MssqlSnapshotSelectViewModel>();
        services.AddTransient<OlvmSnapshotSelectViewModel>();
        services.AddTransient<DbConfigurationViewModel>();

        return services;
    }
}
