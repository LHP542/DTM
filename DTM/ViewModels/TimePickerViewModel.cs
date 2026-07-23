using CommunityToolkit.Mvvm.ComponentModel;

namespace DTM.ViewModels;

public sealed partial class TimePickerViewModel : ViewModelBase
{
    // Avalonia 11.2.3: CalendarDatePicker.SelectedDate ist DateTime? (NICHT DateTimeOffset?).
    // Der WinUI-style DatePicker dagegen nutzt DateTimeOffset?. Hier passend zum
    // CalendarDatePicker in TimePickerWindow.axaml.
    [ObservableProperty] private DateTime? _selectedDate = DateTime.Today;
    [ObservableProperty] private TimeSpan? _selectedTime = DateTime.Now.TimeOfDay;

    public DateTime ComposeDateTime()
    {
        var date = SelectedDate?.Date ?? DateTime.Today;
        var time = SelectedTime ?? TimeSpan.Zero;
        return date.Add(time);
    }
}
