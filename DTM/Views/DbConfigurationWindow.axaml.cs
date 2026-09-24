using Avalonia.Interactivity;
using DTM.ViewModels;

namespace DTM.Views;

/// <summary>
/// Dialog für die drei selteneren MSSQL-DB-Settings (Phase 5.1/5.3):
/// Query-Store an/aus, Page-Verify-Modus, Compatibility-Reset. Pro
/// Sektion ein „Anwenden"-Button mit vorgeschaltetem
/// <see cref="ConfirmWindow"/>, da alle drei Änderungen das
/// Verhalten der DB beeinflussen können.
/// </summary>
public partial class DbConfigurationWindow : ChromeWindow
{
    public DbConfigurationWindow()
    {
        InitializeComponent();
    }

    private void OnCancel(object? _, RoutedEventArgs e) => Close();

    private async void OnApplyQueryStore(object? _, RoutedEventArgs e)
    {
        if (DataContext is not DbConfigurationViewModel vm) return;
        string target = vm.QueryStoreOn ? "ON" : "OFF";
        if (!await ConfirmAsync(
                "Query-Store ändern?",
                $"Query-Store für „{vm.DatabaseName}\" wird auf {target} gesetzt.",
                target))
            return;
        vm.PerformQueryStore();
    }

    private async void OnApplyPageVerify(object? _, RoutedEventArgs e)
    {
        if (DataContext is not DbConfigurationViewModel vm) return;
        string warning = vm.PageVerifySelected == "NONE"
            ? "\n\nAchtung: NONE deaktiviert die Page-Verify-Prüfung komplett — Datenseiten-Korruption wird nicht mehr erkannt."
            : string.Empty;
        if (!await ConfirmAsync(
                "Page-Verify ändern?",
                $"Page-Verify für „{vm.DatabaseName}\" wird auf {vm.PageVerifySelected} gesetzt.{warning}",
                vm.PageVerifySelected))
            return;
        vm.PerformPageVerify();
    }

    private async void OnResetCompatibility(object? _, RoutedEventArgs e)
    {
        if (DataContext is not DbConfigurationViewModel vm) return;
        if (!await ConfirmAsync(
                "Compatibility-Level zurücksetzen?",
                $"Compatibility-Level der Datenbank „{vm.DatabaseName}\" (aktuell {vm.CompatibilityLevel}) "
                + "wird auf den MASTER-DB-Default des Servers gesetzt.\n\n"
                + "Änderungen können das Query-Optimizer-Verhalten beeinflussen und in Einzelfällen "
                + "Anwendungen brechen.",
                "Zurücksetzen"))
            return;
        vm.PerformCompatibilityReset();
    }

    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        ConfirmWindow dlg = new()
        {
            WindowTitle = title,
            Message = message,
            ConfirmText = confirmLabel,
            CancelText = "Abbrechen",
        };
        return await dlg.ShowDialog<bool>(this);
    }
}
