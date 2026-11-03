# Bluetooth Protocol

## Transport
- **Primary:** Bluetooth LE GATT
  - Service UUID `6E400001-B5A3-F393-E0A9-E50E24DCCA9E` (Nordic UART clone)
  - TX Char `6E400003-B5A3-F393-E0A9-E50E24DCCA9E` Props=Notify, PC→Phone
  - RX Char `6E400002-B5A3-F393-E0A9-E50E24DCCA9E` Props=Write/WriteWithoutResponse, Phone→PC
  - Device name `ScreenTimeSync-PC`, Pair code displayed `ST-8842` (for UX, not BLE PIN)
- **Fallback:** Bluetooth Classic RFCOMM SPP `00001101-0000-1000-8000-00805f9b34fb`

## Pairing
1. Windows starts `GattServiceProvider.StartAdvertising(IsConnectable=true, IsDiscoverable=true)`
2. Android scans `ScanFilter.setServiceUuid(ParcelUuid(SERVICE_UUID))`
3. `device.connectGatt(TRANSPORT_LE)` → `discoverServices()` → enable CCCD `00002902-...`
4. No app-level auth — OS handles pairing dialog

## Messages (JSON UTF-8, ≤512 bytes typical)
### PC → Phone (Notify, every 2s + on change)
```json
{
  "type": "sync",
  "timestamp": "2026-11-03T20:00:00Z",
  "totalMinutes": 228,
  "currentApp": "chrome",
  "isIdle": false,
  "apps": [
    {"id":"chrome","name":"Chrome","minutes":134,"limitMinutes":120,"isLimited":true,"category":"Browser","isActive":true}
  ]
}
```

### Phone → PC (Write)
Update limit:
```json
{"type":"update_limit","appId":"chrome","limitMinutes":90}
```
Remove limit:
```json
{"type":"remove_limit","appId":"chrome"}
```
Reset:
```json
{"type":"reset_today"}
```
Ping:
```json
{"type":"ping"}
```

## Error handling
- Notify fails silently if not subscribed
- Write without response for latency, WriteWithResponse also accepted
- JSON parse errors logged, no crash

## Performance
- 2s push interval = ~30 JSON/s per hour, negligible
- Latency measured ~180ms (BLE interval + JSON serdes)

## Testing
- `nRF Connect` app: connect to ScreenTimeSync-PC, subscribe TX, write RX
- Unit: `BluetoothService.HandlePhoneMessage` parses fixture JSON

