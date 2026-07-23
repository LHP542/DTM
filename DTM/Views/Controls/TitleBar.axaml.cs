using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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
        else if (change.Property == ShowMinimizeProperty)
            MinButton.IsVisible = ShowMinimize;
        else if (change.Property == ShowMaximizeProperty)
            MaxButton.IsVisible = ShowMaximize;
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
