using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class RecordViewModel : ObservableObject, IDisposable
{
    private readonly IRideTracker _tracker;
    private readonly IDatabaseService _db;
    private IDispatcherTimer? _timer;

    [ObservableProperty]
    private RideState _state;

    [ObservableProperty]
    private double _distanceKm;

    [ObservableProperty]
    private string _duration = "00:00:00";

    [ObservableProperty]
    private double _speedKmh;

    [ObservableProperty]
    private int _pointCount;

    public RecordViewModel(IRideTracker tracker, IDatabaseService db)
    {
        _tracker = tracker;
        _db = db;
        _tracker.StateChanged += OnStateChanged;
        _tracker.PointAdded += OnPointAdded;
    }

    public void StartTimer(IDispatcher dispatcher)
    {
        if (_timer is not null) return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateDisplay();
        _timer.Start();
    }

    [RelayCommand]
    private async Task StartRideAsync()
    {
        await _tracker.StartAsync();
    }

    [RelayCommand]
    private void PauseRide()
    {
        _tracker.Pause();
    }

    [RelayCommand]
    private async Task ResumeRideAsync()
    {
        await _tracker.ResumeAsync();
    }

    [RelayCommand]
    private async Task StopRideAsync()
    {
        var ride = await _tracker.StopAsync();

        if (ride.TrackPointsJson == "[]" || PointCount < 2)
        {
            await Shell.Current.DisplayAlertAsync("Ride Discarded",
                "Not enough GPS points were recorded. The ride was not saved.", "OK");
            PointCount = 0;
            return;
        }

        await _db.SaveRideAsync(ride);

        await Shell.Current.DisplayAlertAsync("Ride Saved",
            $"Distance: {ride.DistanceMeters / 1000:F2} km\nDuration: {ride.Duration:hh\\:mm\\:ss}", "OK");

        PointCount = 0;
    }

    private void OnStateChanged()
    {
        State = _tracker.State;
        OnPropertyChanged(nameof(State));
    }

    private void OnPointAdded(TrackPoint point)
    {
        PointCount = _tracker.Points.Count;
    }

    private void UpdateDisplay()
    {
        if (_tracker.State is RideState.Recording or RideState.Paused)
        {
            DistanceKm = _tracker.DistanceMeters / 1000.0;
            Duration = _tracker.Duration.ToString(@"hh\:mm\:ss");
            SpeedKmh = _tracker.CurrentSpeedKmh;
            PointCount = _tracker.Points.Count;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _tracker.StateChanged -= OnStateChanged;
        _tracker.PointAdded -= OnPointAdded;
    }
}
