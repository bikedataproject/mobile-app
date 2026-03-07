using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BDP.App.Models;
using BDP.App.Services;

namespace BDP.App.ViewModels;

public partial class RideHistoryViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    private readonly IGpxSerializer _gpx;
    private readonly IApiService _api;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _pendingCount;

    public ObservableCollection<RideRecord> Rides { get; } = [];

    public RideHistoryViewModel(IDatabaseService db, IGpxSerializer gpx, IApiService api)
    {
        _db = db;
        _gpx = gpx;
        _api = api;
    }

    [RelayCommand]
    private async Task LoadRidesAsync()
    {
        IsBusy = true;
        var rides = await _db.GetAllRidesAsync();

        Rides.Clear();
        foreach (var ride in rides)
            Rides.Add(ride);

        PendingCount = rides.Count(r => !r.IsUploaded);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task UploadAllPendingAsync()
    {
        IsBusy = true;
        var pending = await _db.GetPendingUploadsAsync();
        var uploaded = 0;
        var errors = new List<string>();

        foreach (var ride in pending)
        {
            try
            {
                var points = JsonSerializer.Deserialize<List<TrackPoint>>(ride.TrackPointsJson) ?? [];
                if (points.Count < 2)
                {
                    errors.Add($"Ride {ride.StartTime:MMM dd HH:mm}: too few track points ({points.Count})");
                    continue;
                }

                var gpxXml = _gpx.Serialize(points, ride.StartTime);
                var fileName = $"ride_{ride.StartTime:yyyyMMdd_HHmmss}.gpx";
                var result = await _api.UploadGpxAsync(gpxXml, fileName);

                if (result.Errors.Count > 0)
                {
                    errors.AddRange(result.Errors);
                }
                else if (result.Imported > 0 || result.Duplicates > 0)
                {
                    await _db.MarkUploadedAsync(ride.Id);
                    uploaded++;
                }
                else
                {
                    errors.Add($"Ride {ride.StartTime:MMM dd HH:mm}: server returned 0 imported, 0 duplicates, 0 failed");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Ride {ride.StartTime:MMM dd HH:mm}: {ex.Message}");
            }
        }

        await LoadRidesAsync();

        var message = $"Uploaded {uploaded} of {pending.Count} rides.";
        if (errors.Count > 0)
            message += $"\n\nErrors:\n{string.Join("\n", errors)}";

        await Shell.Current.DisplayAlertAsync(
            uploaded == pending.Count ? "Upload Complete" : "Upload Issues",
            message, "OK");
        IsBusy = false;
    }

    [RelayCommand]
    private async Task OpenRideAsync(RideRecord ride)
    {
        await Shell.Current.GoToAsync($"rideDetail?rideId={ride.Id}");
    }

    [RelayCommand]
    private async Task DeleteRideAsync(RideRecord ride)
    {
        var confirm = await Shell.Current.DisplayAlertAsync("Delete Ride", "Are you sure you want to delete this ride?", "Delete", "Cancel");
        if (!confirm) return;

        await _db.DeleteRideAsync(ride.Id);
        Rides.Remove(ride);
        PendingCount = Rides.Count(r => !r.IsUploaded);
    }
}
