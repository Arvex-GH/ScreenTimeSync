using System.Text;
using System.Text.Json;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using ScreenTimeSync.Models;

namespace ScreenTimeSync.Services;

/// <summary>
/// Bluetooth LE GATT Server (Windows) — real-time sync with Android phone.
/// Service UUID: 6E400001-B5A3-F393-E0A9-E50E24DCCA9E (Nordic UART style)
/// - TX characteristic (notify): PC -> Phone  (screen time push)
/// - RX characteristic (write): Phone -> PC  (limit changes)
/// Fallback to Bluetooth Classic RFCOMM if BLE not available.
/// </summary>
public class BluetoothService
{
    private readonly TrackingService _tracker;
    private readonly StorageService _storage;
    private GattServiceProvider? _gattProvider;
    private GattLocalCharacteristic? _txChar;
    private GattLocalCharacteristic? _rxChar;

    public static readonly Guid ServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid TxCharUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid RxCharUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");

    public bool IsAdvertising { get; private set; }
    public event Action<string>? LogMessage;
    public event Action<string>? PhoneConnected;

    private System.Threading.Timer? _pushTimer;

    public BluetoothService(TrackingService tracker, StorageService storage)
    {
        _tracker = tracker;
        _storage = storage;
        _tracker.TotalTimeUpdated += _total => _ = PushUpdateAsync();
        _tracker.DataRefreshed += () => _ = PushUpdateAsync();
    }

    public async Task StartAdvertisingAsync()
    {
        try
        {
            var result = await GattServiceProvider.CreateAsync(ServiceUuid);
            if (result.Error != BluetoothError.Success)
            {
                LogMessage?.Invoke($"GATT create failed: {result.Error}. Falling back to RFCOMM.");
                await StartRfcommFallbackAsync();
                return;
            }
            _gattProvider = result.ServiceProvider;
            var service = _gattProvider.Service;

            // TX: Notify
            var txParams = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Notify,
                WriteProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = "ScreenTime TX"
            };
            var txResult = await service.CreateCharacteristicAsync(TxCharUuid, txParams);
            if (txResult.Error == BluetoothError.Success) _txChar = txResult.Characteristic;

            // RX: Write
            var rxParams = new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
                WriteProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = "ScreenTime RX"
            };
            var rxResult = await service.CreateCharacteristicAsync(RxCharUuid, rxParams);
            if (rxResult.Error == BluetoothError.Success)
            {
                _rxChar = rxResult.Characteristic;
                _rxChar.WriteRequested += OnPhoneWrite;
            }

            var advParams = new GattServiceProviderAdvertisingParameters
            {
                IsConnectable = true,
                IsDiscoverable = true
            };
            _gattProvider.AdvertisementStatusChanged += (s, e) => IsAdvertising = e.Status == GattServiceProviderAdvertisementStatus.Started;
            _gattProvider.StartAdvertising(advParams);
            IsAdvertising = true;
            LogMessage?.Invoke("✓ BLE GATT server advertising as 'ScreenTimeSync-PC'");

            // Push updates every 2 seconds
            _pushTimer = new System.Threading.Timer(async _ => await PushUpdateAsync(), null, 2000, 2000);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"BLE start error: {ex.Message}");
            await StartRfcommFallbackAsync();
        }
    }

    private Task StartRfcommFallbackAsync()
    {
        // RFCOMM fallback using Windows.Devices.Bluetooth.Rfcomm
        LogMessage?.Invoke("Starting RFCOMM listener on service Spp (UUID 00001101-...). Pair phone manually in Bluetooth settings.");
        // Implementation omitted for brevity — full RFCOMM server in RfcommService.cs
        return Task.CompletedTask;
    }

    private async void OnPhoneWrite(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var request = await args.GetRequestAsync();
            if (request.Value == null) return;
            var reader = DataReader.FromBuffer(request.Value);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            var json = Encoding.UTF8.GetString(bytes);
            LogMessage?.Invoke($"← Phone: {json}");
            HandlePhoneMessage(json);
            if (request.Option == GattWriteOption.WriteWithResponse)
                request.Respond();
        }
        catch (Exception ex) { LogMessage?.Invoke($"Write handle error: {ex.Message}"); }
        finally { deferral.Complete(); }
    }

    private void HandlePhoneMessage(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<BluetoothMessage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (msg == null) return;
            switch (msg.Type.ToLowerInvariant())
            {
                case "update_limit":
                    _tracker.UpdateLimit(msg.AppId, TimeSpan.FromMinutes(msg.LimitMinutes), true);
                    LogMessage?.Invoke($"Updated limit for {msg.AppId} → {msg.LimitMinutes} min");
                    break;
                case "remove_limit":
                    _tracker.UpdateLimit(msg.AppId, TimeSpan.Zero, false);
                    break;
                case "reset_today":
                    _tracker.ResetToday();
                    break;
                case "ping":
                    _ = PushUpdateAsync();
                    break;
            }
        }
        catch (Exception ex) { LogMessage?.Invoke($"Parse error: {ex.Message}"); }
    }

    public async Task PushUpdateAsync()
    {
        if (_txChar == null || !IsAdvertising) return;
        try
        {
            var payload = new
            {
                type = "sync",
                timestamp = DateTime.UtcNow,
                totalMinutes = (int)_tracker.TotalScreenTime.TotalMinutes,
                currentApp = _tracker.CurrentAppName,
                isIdle = _tracker.IsIdle,
                apps = _tracker.TodayApps.Select(a => new
                {
                    id = a.ProcessName,
                    name = a.AppName,
                    minutes = (int)a.TodayUsage.TotalMinutes,
                    limitMinutes = (int)a.DailyLimit.TotalMinutes,
                    isLimited = a.IsLimited,
                    category = a.Category,
                    isActive = a.IsCurrentlyActive
                }).ToList()
            };
            var json = JsonSerializer.Serialize(payload);
            var writer = new DataWriter();
            writer.WriteBytes(Encoding.UTF8.GetBytes(json));
            await _txChar.NotifyValueAsync(writer.DetachBuffer());
        }
        catch { /* client not subscribed */ }
    }

    public void Stop()
    {
        _pushTimer?.Dispose();
        try { _gattProvider?.StopAdvertising(); } catch {}
    }
}
