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

        foreach (var ride in pending)
        {
            var points = JsonSerializer.Deserialize<List<TrackPoint>>(ride.TrackPointsJson) ?? [];
            if (points.Count < 2) continue;

            var gpxXml = _gpx.Serialize(points, ride.StartTime);
            var fileName = $"ride_{ride.StartTime:yyyyMMdd_HHmmss}.gpx";
            var result = await _api.UploadGpxAsync(gpxXml, fileName);

            if (result is not null && (result.Imported > 0 || result.Duplicates > 0))
            {
                await _db.MarkUploadedAsync(ride.Id);
                uploaded++;
            }
        }

        await LoadRidesAsync();
        await Shell.Current.DisplayAlertAsync("Upload Complete", $"Uploaded {uploaded} of {pending.Count} rides.", "OK");
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
