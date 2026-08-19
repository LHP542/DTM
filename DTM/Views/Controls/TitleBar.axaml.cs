using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DTM.Views.Controls;

/// <summary>
/// Wiederverwendbare Titelleiste fuer DTM-Dialoge (ChromeWindow-Basis).
/// Drag/Move via PointerPressed, Doppelklick maximiert (falls
/// ShowMaximize=true), Close-Button ruft Host-Window.Close.
/// </summary>
public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));

    // Optionales Piktogramm links vom Titel (Warnung, Zahnrad, Restore-Pfeil).
    // Nicht gesetzt = kein Glyph, Layout wie bei einer reinen Text-Titelleiste.
    public static readonly StyledProperty<string?> GlyphProperty =
        AvaloniaProperty.Register<TitleBar, string?>(nameof(Glyph));

    // Farbe des Glyphs. Default Gold — im Kroste-Look der Highlight-Ton;
    // Dialoge mit Warncharakter setzen KrosteDangerBrush.
    public static readonly StyledProperty<IBrush?> GlyphBrushProperty =
        AvaloniaProperty.Register<TitleBar, IBrush?>(nameof(GlyphBrush));

    // Fensterspezifische Chrome-Buttons links von Min/Max/Close.
    // MainWindow haengt hier seinen "Ueber"-Button ein.
    public static readonly StyledProperty<object?> ExtraContentProperty =
        AvaloniaProperty.Register<TitleBar, object?>(nameof(ExtraContent));

    public static readonly StyledProperty<bool> ShowMinimizeProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMinimize));

    public static readonly StyledProperty<bool> ShowMaximizeProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMaximize));

    // Dialog-Result, das beim Klick auf "X" per Close(object?) mitgesendet wird.
    // Fuer ShowDialog<TResult>: TimePickerWindow braucht z.B. TimePickResult.Cancel(),
    // EditConnectionWindow braucht "false". Bleibt null -> Close() ohne Argument
    // (bei ShowDialog<bool> liefert das default(bool)=false, bei Objekt-Types null).
    public static readonly StyledProperty<object?> CloseResultProperty =
        AvaloniaProperty.Register<TitleBar, object?>(nameof(CloseResult));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public IBrush? GlyphBrush
    {
        get => GetValue(GlyphBrushProperty);
        set => SetValue(GlyphBrushProperty, value);
    }

    public object? ExtraContent
    {
        get => GetValue(ExtraContentProperty);
        set => SetValue(ExtraContentProperty, value);
    }

    public bool ShowMinimize
    {
        get => GetValue(ShowMinimizeProperty);
        set => SetValue(ShowMinimizeProperty, value);
    }

    public bool ShowMaximize
    {
        get => GetValue(ShowMaximizeProperty);
        set => SetValue(ShowMaximizeProperty, value);
    }

    public object? CloseResult
    {
        get => GetValue(CloseResultProperty);
        set => SetValue(CloseResultProperty, value);
    }

    public TitleBar()
    {
        InitializeComponent();
    }

    // Avalonia 12: VisualRoot ist nicht mehr das Window selbst — TopLevel.GetTopLevel
    // liefert das Window ueber den internen TopLevelHost.
    private Window? Host => TopLevel.GetTopLevel(this) as Window;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
            TitleText.Text = Title;
        else if (change.Property == GlyphProperty)
        {
            GlyphText.Text = Glyph;
            GlyphText.IsVisible = !string.IsNullOrEmpty(Glyph);
        }
        else if (change.Property == GlyphBrushProperty)
            GlyphText.Foreground = GlyphBrush;
        else if (change.Property == ExtraContentProperty)
            ExtraSlot.Content = ExtraContent;
        else if (change.Property == ShowMinimizeProperty)
            MinButton.IsVisible = ShowMinimize;
        else if (change.Property == ShowMaximizeProperty)
        {
            MaxButton.IsVisible = ShowMaximize;
            UpdateMaximizeGlyph();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Erst hier existiert das Host-Window. Der Glyph muss dem WindowState
        // folgen, sonst zeigt ein maximiertes Fenster weiter das
        // "Maximieren"-Symbol.
        if (Host is { } w)
        {
            w.PropertyChanged += OnHostPropertyChanged;
            UpdateMaximizeGlyph();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (Host is { } w) w.PropertyChanged -= OnHostPropertyChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
    {
        if (!ShowMaximize || Host is not { } w) return;
        // U+2750 = Wiederherstellen, U+2610 = Maximieren
        MaxButton.Content = w.WindowState == WindowState.Maximized ? "❐" : "☐";
    }

    private void OnBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Host?.BeginMoveDrag(e);
    }

    private void OnBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!ShowMaximize || Host is not { } w) return;
        w.WindowState = w.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnMinClick(object? sender, RoutedEventArgs e)
    {
        if (Host is { } w) w.WindowState = WindowState.Minimized;
    }

    private void OnMaxClick(object? sender, RoutedEventArgs e)
    {
        if (Host is { } w)
            w.WindowState = w.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (Host is not { } w) return;
        if (CloseResult is null) w.Close();
        else w.Close(CloseResult);
    }
}
