using Avalonia.Interactivity;
using DTM.Updater;

namespace DTM.Views;

public enum UpdateDialogResult { ApplyNow, Later, Skip }

public partial class UpdatePromptWindow : ChromeWindow
{
    public UpdateDialogResult Result { get; private set; } = UpdateDialogResult.Skip;

    public UpdatePromptWindow() => InitializeComponent();

    public UpdatePromptWindow(string newVersion, string currentVersion,
                              IReadOnlyList<ReleaseNote>? notes = null)
    {
        InitializeComponent();
        MessageText.Text =
            $"Version {newVersion} ist verfügbar (aktuell: {currentVersion}).\n" +
            "Jetzt aktualisieren?";

        if (notes is { Count: > 0 })
        {
            NotesList.ItemsSource = notes
                .Select(n => new NoteRow(
                    Header: $"v{n.Version}",
                    DateLabel: string.IsNullOrWhiteSpace(n.Date) ? string.Empty : $"({n.Date})",
                    Bullets: n.Notes))
                .ToList();

            var allModules = notes.SelectMany(n => n.ModulesChanged).ToHashSet(StringComparer.OrdinalIgnoreCase);
            MssqlBanner.IsVisible  = allModules.Contains("MSSQL");
            FocSqlBanner.IsVisible = allModules.Contains("FOC-SQL");
        }
    }

    private void OnTitleClose(object? _, RoutedEventArgs e) => Close();

    private void OnApply(object? _, RoutedEventArgs e) { Result = UpdateDialogResult.ApplyNow; Close(); }
    private void OnLater(object? _, RoutedEventArgs e) { Result = UpdateDialogResult.Later;    Close(); }
    private void OnSkip (object? _, RoutedEventArgs e) { Result = UpdateDialogResult.Skip;     Close(); }
}

public sealed record NoteRow(string Header, string DateLabel, IReadOnlyList<string> Bullets);
