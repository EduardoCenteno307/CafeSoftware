using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CafeSoftware.ViewModels;

// What the orange / red pop-up banner shows
public partial class AlertViewModel : ObservableObject
{
    private bool _pulseOn;

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _message;
    [ObservableProperty] private Brush _cardBrush;
    [ObservableProperty] private double _cardOpacity = 1.0;

    public AlertViewModel(string title, string message, Color color)
    {
        _title = title;
        _message = message;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _cardBrush = brush;
    }

    public void Update(string title, string message)
    {
        Title = title;
        Message = message;
    }

    // Called every second during the final countdown so the banner blinks
    public void Pulse()
    {
        _pulseOn = !_pulseOn;
        CardOpacity = _pulseOn ? 1.0 : 0.65;
    }
}