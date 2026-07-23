using System.Diagnostics;
using System.IO;
using Avalonia.Interactivity;
using SystemFile = System.IO.File;

namespace DTM.Views;

public partial class FatalErrorWindow : ChromeWindow
{
    public FatalErrorWindow()
    {
        InitializeComponent();
    }

    public FatalErrorWindow(Exception ex, string source) : this()
    {
        SourceText.Text = $"Quelle: {source}";
        ExceptionTypeText.Text = ex.GetType().FullName ?? ex.GetType().Name;
        MessageText.Text = ex.Message;
        StackTraceText.Text = ex.ToString();
    }

    private void OnTitleClose(object? _, RoutedEventArgs e) => Close();
    private void OnClose(object? _, RoutedEventArgs e) => Close();

    private void OnOpenLogs(object? _, RoutedEventArgs e)
    {
        // NLog schreibt unter ${basedir}/logs (siehe Nlog.config).
        string logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = logDir,
                UseShellExecute = true,
            });
        }
        catch
        {
            // Wenn sich das Verzeichnis nicht oeffnen laesst (z.B. headless), ignorieren —
            // der Pfad steht ohnehin in den Logs selbst.
        }
    }
}
