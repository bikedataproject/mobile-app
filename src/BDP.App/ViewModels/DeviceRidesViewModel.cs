using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class DeviceRidesViewModel : ObservableObject
{
    private readonly IDatabaseService _db;

    [ObservableProperty]
    private string _deviceName = "BikeTracker";

    [ObservableProperty]
    private string _summary = "";

    public ObservableCollection<RideDisplayItem> Rides { get; } = [];

    public DeviceRidesViewModel(IDatabaseService db)
    {
        _db = db;
    }

    public async Task LoadRidesAsync(int trackerId)
    {
        var rides = await _db.GetRidesByTrackerAsync(trackerId);
        Rides.Clear();

        var totalDist = 0.0;
        var totalDuration = TimeSpan.Zero;

        foreach (var ride in rides)
        {
            var distKm = ride.DistanceMeters / 1000.0;
            totalDist += distKm;
            totalDuration += ride.Duration;

            Rides.Add(new RideDisplayItem
            {
                Date = ride.StartTime.LocalDateTime.ToString("ddd d MMM, HH:mm"),
                Distance = $"{distKm:F1} km",
                Duration = ride.Duration.ToString(@"h\:mm\:ss"),
                Points = $"{(ride.TrackPointsJson.Split("Latitude").Length - 1)} pts"
            });
        }

        Summary = rides.Count == 0
            ? "No rides recorded yet"
            : $"{rides.Count} rides, {totalDist:F1} km total, {totalDuration:h\\:mm} riding time";
    }
}

public class RideDisplayItem
{
    public string Date { get; init; } = "";
    public string Distance { get; init; } = "";
    public string Duration { get; init; } = "";
    public string Points { get; init; } = "";
}
