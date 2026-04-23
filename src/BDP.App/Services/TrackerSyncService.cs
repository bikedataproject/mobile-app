using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BDP.App.Models;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions;

namespace BDP.App.Services;

public sealed class TrackerSyncService : ITrackerSyncService
{
    private const string DeviceName = "BikeTracker";
    private const string DeviceNameSetup = "BikeTracker (Setup)";
    private const string ServiceUuid = "4fafc201-1fb5-459e-8fcc-c5c9c331914b";
    private const string BatchCharUuid = "beb5483e-36e1-4688-b7f5-ea07361b26a8";
    private const string CountCharUuid = "8ca20d91-5a20-4d5b-a7f1-7c4e3a1c2b3d";
    private const string ClearCharUuid = "9da30e02-6b31-4e6c-b802-8d5f4b2d3c4e";
    private const string PairCharUuid = "a1b2c3d4-1234-5678-9abc-def012345678";
    private const string StatusCharUuid = "b2c3d4e5-2345-6789-abcd-ef0123456789";
    private const string StateCharUuid = "c3d4e5f6-3456-789a-bcde-f01234567890";
    private const string SeekCharUuid = "d4e5f607-4567-89ab-cdef-012345678901";
    private const string KeyStorageKey = "tracker_enc_key";

    // Epoch base: 2000-01-01 00:00:00 UTC
    private static readonly DateTimeOffset Epoch2000 = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Trip split threshold: 60 seconds gap between points = new trip
    private const int TripGapSeconds = 600;

    private readonly IAdapter _adapter;
    private IDevice? _device;
    private IService? _service;
    private ICharacteristic? _batchChar;
    private ICharacteristic? _countChar;
    private ICharacteristic? _clearChar;
    private ICharacteristic? _pairChar;
    private ICharacteristic? _statusChar;
    private ICharacteristic? _stateChar;
    private ICharacteristic? _seekChar;
    private byte[]? _encKey;
    private EventHandler<Plugin.BLE.Abstractions.EventArgs.DeviceEventArgs>? _scanHandler;

    public bool IsScanning { get; private set; }
    public bool IsConnected => _device?.State == DeviceState.Connected;
    public bool IsPaired => _encKey is not null;
    public bool NeedsPairing { get; private set; }
    public string Status { get; private set; } = "Not connected";

    public event Action? StatusChanged;

    public TrackerSyncService()
    {
        _adapter = CrossBluetoothLE.Current.Adapter;
        _adapter.ScanTimeout = 10000;
        LoadKey();
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    private void LoadKey()
    {
        var hex = SecureStorage.Default.GetAsync(KeyStorageKey).Result;
        if (hex is not null)
            _encKey = Convert.FromHexString(hex);
    }

    private async Task SaveKeyAsync(byte[] key)
    {
        _encKey = key;
        await SecureStorage.Default.SetAsync(KeyStorageKey, Convert.ToHexString(key));
    }

    public async Task<bool> ScanAndConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return true;

        IsScanning = true;
        SetStatus("Scanning for BikeTracker...");

        IDevice? found = null;

        // Remove old handler before adding new one
        if (_scanHandler is not null)
            _adapter.DeviceDiscovered -= _scanHandler;

        int totalSeen = 0;
        var names = new List<string>();
        _scanHandler = (_, args) =>
        {
            totalSeen++;
            var name = args.Device.Name;
            var id = args.Device.Id.ToString()[..8];
            var label = string.IsNullOrEmpty(name) ? $"[{id}]" : name;
            if (!names.Contains(label))
                names.Add(label);
            if (name is DeviceName or DeviceNameSetup)
                found = args.Device;
            // Also try matching by advertised service UUID
            if (found is null)
            {
                foreach (var rec in args.Device.AdvertisementRecords ?? [])
                {
                    if (rec.Type == Plugin.BLE.Abstractions.AdvertisementRecordType.UuidsComplete128Bit ||
                        rec.Type == Plugin.BLE.Abstractions.AdvertisementRecordType.UuidsIncomplete128Bit)
                    {
                        var hex = Convert.ToHexString(rec.Data).ToLower();
                        if (hex.Contains("4fafc201"))
                            found = args.Device;
                    }
                }
            }
        };
        _adapter.DeviceDiscovered += _scanHandler;

        try
        {
            if (_adapter.IsScanning)
                await _adapter.StopScanningForDevicesAsync();

            await _adapter.StartScanningForDevicesAsync(cancellationToken: ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }

        IsScanning = false;

        if (found is null)
        {
            SetStatus($"Seen {totalSeen}: {string.Join(", ", names)}");
            return false;
        }

        SetStatus("Connecting...");
        try
        {
            await _adapter.ConnectToDeviceAsync(found, cancellationToken: ct);
            _device = found;

            _service = await _device.GetServiceAsync(Guid.Parse(ServiceUuid));
            if (_service is null)
            {
                SetStatus("Tracker service not found");
                await DisconnectAsync();
                return false;
            }

            _batchChar = await _service.GetCharacteristicAsync(Guid.Parse(BatchCharUuid));
            _countChar = await _service.GetCharacteristicAsync(Guid.Parse(CountCharUuid));
            _clearChar = await _service.GetCharacteristicAsync(Guid.Parse(ClearCharUuid));
            _pairChar = await _service.GetCharacteristicAsync(Guid.Parse(PairCharUuid));
            _statusChar = await _service.GetCharacteristicAsync(Guid.Parse(StatusCharUuid));
            _stateChar = await _service.GetCharacteristicAsync(Guid.Parse(StateCharUuid));
            _seekChar = await _service.GetCharacteristicAsync(Guid.Parse(SeekCharUuid));

            if (_statusChar is not null)
            {
                var statusBytes = await _statusChar.ReadAsync();
                var deviceStatus = Encoding.UTF8.GetString(statusBytes.data);
                NeedsPairing = deviceStatus == "SETUP";
            }

            if (NeedsPairing)
                SetStatus("Connected — device needs pairing");
            else if (IsPaired)
                SetStatus("Connected to BikeTracker");
            else
                SetStatus("Connected — but no key stored. Factory reset device to re-pair.");

            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Connection failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PairAsync()
    {
        if (_pairChar is null || !NeedsPairing) return false;

        var key = RandomNumberGenerator.GetBytes(16);

        try
        {
            await _pairChar.WriteAsync(key);
            await SaveKeyAsync(key);
            NeedsPairing = false;
            SetStatus("Paired successfully");
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Pairing failed: {ex.Message}");
            return false;
        }
    }

    public async Task<int> GetPointCountAsync()
    {
        if (_countChar is null) return 0;

        var bytes = await _countChar.ReadAsync();
        var value = Encoding.UTF8.GetString(bytes.data);
        if (value == "LOCKED") return 0;
        return int.TryParse(value, out var count) ? count : 0;
    }

    public async Task SeekAsync(int index)
    {
        if (_seekChar is null) return;
        await _seekChar.WriteAsync(Encoding.UTF8.GetBytes(index.ToString()));
    }

    private byte[] Decrypt(byte[] encrypted)
    {
        if (_encKey is null) return encrypted;

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = _encKey;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
    }

    private List<TrackPoint> ParseBatch(byte[] decrypted)
    {
        var points = new List<TrackPoint>();
        if (decrypted.Length < 16) return points; // minimum: 4B header + 12B reference

        int pos = 0;

        // Header
        int startIndex = (decrypted[pos] << 8) | decrypted[pos + 1]; pos += 2;
        int count = (decrypted[pos] << 8) | decrypted[pos + 1]; pos += 2;

        if (count <= 0 || pos + 12 > decrypted.Length) return points;

        // Reference point
        float refLat = BitConverter.ToSingle(decrypted, pos); pos += 4;
        float refLng = BitConverter.ToSingle(decrypted, pos); pos += 4;
        uint epoch = BitConverter.ToUInt32(decrypted, pos); pos += 4;

        var refTime = Epoch2000.AddSeconds(epoch);
        double prevLat = refLat;
        double prevLng = refLng;
        uint prevEpoch = epoch;

        points.Add(new TrackPoint
        {
            Latitude = refLat,
            Longitude = refLng,
            Timestamp = refTime,
            Accuracy = 0
        });

        // Delta points
        for (int i = 1; i < count && pos + 6 <= decrypted.Length; i++)
        {
            short dlat = BitConverter.ToInt16(decrypted, pos); pos += 2;
            short dlng = BitConverter.ToInt16(decrypted, pos); pos += 2;
            ushort dt = BitConverter.ToUInt16(decrypted, pos); pos += 2;

            prevLat += dlat / 1000000.0;
            prevLng += dlng / 1000000.0;
            prevEpoch += dt;

            points.Add(new TrackPoint
            {
                Latitude = prevLat,
                Longitude = prevLng,
                Timestamp = Epoch2000.AddSeconds(prevEpoch),
                Accuracy = 0
            });
        }

        return points;
    }

    public async Task<List<TrackPoint>> DownloadPointsAsync(IProgress<int>? progress = null)
    {
        if (_batchChar is null || _encKey is null) return [];

        var allPoints = new List<TrackPoint>();

        while (true)
        {
            var result = await _batchChar.ReadAsync();
            var data = result.data;

            // Check for DONE or LOCKED
            if (data.Length <= 6)
            {
                var text = Encoding.UTF8.GetString(data);
                if (text is "DONE" or "LOCKED") break;
            }

            try
            {
                var decrypted = Decrypt(data);
                var batch = ParseBatch(decrypted);
                allPoints.AddRange(batch);
                progress?.Report(allPoints.Count);
            }
            catch
            {
                SetStatus("Decryption failed — wrong key?");
                break;
            }
        }

        SetStatus($"Downloaded {allPoints.Count} points");
        return allPoints;
    }

    /// <summary>
    /// Splits a list of track points into separate rides based on time gaps.
    /// </summary>
    public static List<List<TrackPoint>> SplitIntoTrips(List<TrackPoint> points)
    {
        if (points.Count == 0) return [];

        var trips = new List<List<TrackPoint>>();
        var currentTrip = new List<TrackPoint> { points[0] };

        for (int i = 1; i < points.Count; i++)
        {
            var gap = (points[i].Timestamp - points[i - 1].Timestamp).TotalSeconds;
            if (gap > TripGapSeconds)
            {
                if (currentTrip.Count >= 2)
                    trips.Add(currentTrip);
                currentTrip = [];
            }
            currentTrip.Add(points[i]);
        }

        if (currentTrip.Count >= 2)
            trips.Add(currentTrip);

        return trips;
    }

    public async Task ClearDeviceAsync()
    {
        if (_clearChar is null) return;
        await _clearChar.WriteAsync(Encoding.UTF8.GetBytes("1"));
        SetStatus("Device cleared");
    }

    public async Task<string> GetTrackerStateAsync()
    {
        if (_stateChar is null) return "UNKNOWN";
        var bytes = await _stateChar.ReadAsync();
        return Encoding.UTF8.GetString(bytes.data);
    }

    public async Task DisconnectAsync()
    {
        if (_device is not null)
        {
            await _adapter.DisconnectDeviceAsync(_device);
            _device = null;
            _service = null;
            _batchChar = null;
            _countChar = null;
            _clearChar = null;
            _pairChar = null;
            _statusChar = null;
            _stateChar = null;
            _seekChar = null;
        }

        NeedsPairing = false;
        SetStatus("Disconnected");
    }
}
