using BDP.App.Models;
using SQLite;

namespace BDP.App.Services;

public sealed class DatabaseService : IDatabaseService
{
    private SQLiteAsyncConnection? _db;

    private async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_db is not null) return _db;

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "rides.db");
        _db = new SQLiteAsyncConnection(dbPath);
        await _db.CreateTableAsync<RideRecord>();
        return _db;
    }

    public async Task InitializeAsync()
    {
        await GetConnectionAsync();
    }

    public async Task<int> SaveRideAsync(RideRecord ride)
    {
        var db = await GetConnectionAsync();
        if (ride.Id == 0)
        {
            await db.InsertAsync(ride);
            return ride.Id;
        }

        await db.UpdateAsync(ride);
        return ride.Id;
    }

    public async Task<RideRecord?> GetRideAsync(int id)
    {
        var db = await GetConnectionAsync();
        return await db.FindAsync<RideRecord>(id);
    }

    public async Task<List<RideRecord>> GetAllRidesAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<RideRecord>()
            .OrderByDescending(r => r.StartTime)
            .ToListAsync();
    }

    public async Task<List<RideRecord>> GetPendingUploadsAsync()
    {
        var db = await GetConnectionAsync();
        return await db.Table<RideRecord>()
            .Where(r => !r.IsUploaded)
            .OrderByDescending(r => r.StartTime)
            .ToListAsync();
    }

    public async Task MarkUploadedAsync(int id)
    {
        var db = await GetConnectionAsync();
        var ride = await db.FindAsync<RideRecord>(id);
        if (ride is null) return;

        ride.IsUploaded = true;
        await db.UpdateAsync(ride);
    }

    public async Task DeleteRideAsync(int id)
    {
        var db = await GetConnectionAsync();
        await db.DeleteAsync<RideRecord>(id);
    }
}
