using System.Text.Json;
using BDP.App.Models;

namespace BDP.App.Services;

public enum SyncState { Hidden, Uploading, Synced, Failed }

public sealed class UploadService : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    private readonly IDatabaseService _db;
    private readonly IGpxSerializer _gpx;
    private readonly IApiService _api;
    private readonly IAuthService _auth;
    private Timer? _retryTimer;
    private bool _uploading;

    public event Action<SyncState>? SyncStateChanged;

    public UploadService(IDatabaseService db, IGpxSerializer gpx, IApiService api, IAuthService auth)
    {
        _db = db;
        _gpx = gpx;
        _api = api;
        _auth = auth;
    }

    public void Start()
    {
        _retryTimer = new Timer(_ => _ = UploadPendingAsync(), null, RetryInterval, RetryInterval);

        // Check initial state — if there are previously uploaded rides, show synced
        _ = RefreshSyncStateAsync();
    }

    public async Task UploadPendingAsync()
    {
        if (_uploading) return;
        if (!_auth.IsLoggedIn) return;

        _uploading = true;
        try
        {
            var pending = await _db.GetPendingUploadsAsync();
            if (pending.Count == 0)
            {
                await RefreshSyncStateAsync();
                return;
            }

            SyncStateChanged?.Invoke(SyncState.Uploading);
            var anyFailed = false;

            foreach (var ride in pending)
            {
                try
                {
                    var points = JsonSerializer.Deserialize<List<TrackPoint>>(ride.TrackPointsJson) ?? [];
                    if (points.Count < 2) continue;

                    var gpxXml = _gpx.Serialize(points, ride.StartTime);
                    var fileName = $"ride_{ride.StartTime:yyyyMMdd_HHmmss}.gpx";
                    var result = await _api.UploadGpxAsync(gpxXml, fileName);

                    if (result.Imported > 0 || result.Duplicates > 0)
                    {
                        await _db.MarkUploadedAsync(ride.Id);
                    }
                    else
                    {
                        anyFailed = true;
                    }
                }
                catch
                {
                    anyFailed = true;
                }
            }

            SyncStateChanged?.Invoke(anyFailed ? SyncState.Failed : SyncState.Synced);
        }
        finally
        {
            _uploading = false;
        }
    }

    private async Task RefreshSyncStateAsync()
    {
        var pending = await _db.GetPendingUploadsAsync();
        if (pending.Count > 0)
        {
            SyncStateChanged?.Invoke(SyncState.Failed);
            return;
        }

        var all = await _db.GetAllRidesAsync();
        if (all.Count > 0)
            SyncStateChanged?.Invoke(SyncState.Synced);
        else
            SyncStateChanged?.Invoke(SyncState.Hidden);
    }

    public void Dispose()
    {
        _retryTimer?.Dispose();
    }
}
