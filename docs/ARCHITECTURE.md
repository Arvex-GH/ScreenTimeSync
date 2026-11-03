# Architecture

## Goal
Measure Windows screen time per-app and allow real-time limit editing from Android via Bluetooth.

## Components

### 1. Windows App (C# / C++)
**Target:** `net8.0-windows10.0.19041.0`, WPF, x64
**Entry:** `App.xaml.cs` → `TrackingService` + `BluetoothService` + `TrayIconService`

- **TrackingService** (`windows-app/ScreenTimeSync/Services/TrackingService.cs`)
  - Polling `GetForegroundWindow` every 800ms → `GetWindowThreadProcessId` → `Process.GetProcessById` → processName
  - Idle via `GetLastInputInfo` >60s stops counting
  - Caches `AppUsageModel` per processName, flushes elapsed on switch, persists every 10s via `StorageService`
  - Exposes `AppSwitched`, `TotalTimeUpdated`, `DataRefreshed` events for UI & Bluetooth
  - `UpdateLimit(string processName, TimeSpan limit, bool isLimited)` called from BLE

- **NativeHook (C++)** (`windows-native/NativeHook/`)
  - DLL exporting `InstallHook`, `UninstallHook`, `SetCallback`, `GetIdleMilliseconds`
  - `WH_SHELL` hook catches `HSHELL_WINDOWACTIVATED` → instant foreground name via `GetModuleBaseNameW`
  - Optional — TrackingService falls back to polling if DLL missing

- **StorageService** (`StorageService.cs`)
  - SQLite at `%LOCALAPPDATA%/ScreenTimeSync/screentime.db`
  - Tables: `daily_usage(date PK, total_minutes, payload_json)`, `app_limits(process_name PK, limit_minutes, is_limited)`
  - JSON payload of per-app minutes; load on startup, save snapshot every 10s

- **BluetoothService** (`BluetoothService.cs`)
  - GATT ServiceProvider with UUID `6E400001-B5A3-F393-E0A9-E50E24DCCA9E`
  - TX char `6E400003` (Notify) PC→Phone, RX char `6E400002` (Write) Phone→PC
  - Advertises as `ScreenTimeSync-PC`, pushes sync every 2s + on change, handles JSON `update_limit`/`remove_limit`/`reset_today`/`ping`
  - Fallback: RFCOMM SPP UUID `00001101-...` (stub)

- **UI** (`MainWindow.xaml`, `DashboardViewModel.cs`)
  - 3-column: Left hero total + weekly bars + phone card, Center app list, Right pie chart + quick limits
  - LiveCharts2 PieSeries, WPF-UI controls, custom converters

### 2. Android App (Kotlin / Compose)
**Target:** minSdk 26, target 34, Compose BOM 2024.02.02
**Entry:** `MainActivity.kt` → `AppNavHost` → Dashboard/Pair/Settings

- **BluetoothViewModel** (`bluetooth/BluetoothViewModel.kt`)
  - Uses `BluetoothManager` + `BluetoothLeScanner` with `ScanFilter` SERVICE_UUID
  - `BluetoothGattCallback` handles `onConnectionStateChange`, `onServicesDiscovered`, `onCharacteristicChanged`
  - Enables CCCD notifications, parses incoming `sync` JSON → `ScreenTimeState`, sends `update_limit` via RX write
  - StateFlow UI state: `isConnected`, `totalMinutes`, `apps`, `currentApp`

- **UI** (`ui/screens/`)
  - `DashboardScreen.kt` hero total card, LIVE pill, LazyColumn AppRow with category color + progress, Limit dialog with Slider
  - `PairingScreen.kt` mock device list + auto-connect
  - `Theme.kt` dark scheme matching Windows palette

- **Data** (`data/AppUsage.kt`)
  - `AppUsage(id, name, minutes, limitMinutes, isLimited, category, isActive)`, `ScreenTimeState` with demo data

## Data Flow
```
ForegroundWindow → TrackingService → TodayApps (+ SQLite)
                                      ↓ events ↓
                            BluetoothService → GATT Notify (JSON sync)
                                      ↓
                              Android GATT → StateFlow → Compose recompose
                                      ↓ write RX (JSON limit)
                              BluetoothService → TrackingService.UpdateLimit()
```

## Threading
- Windows: DispatcherTimer on UI thread; SQLite async; GATT callbacks on system thread → marshal via Dispatcher if UI touch
- Android: Main thread + viewModelScope coroutines, Ble callbacks on binder thread → StateFlow update (thread-safe)

## Limitations & Future
- Enforcement today is UI warning only — can add Win32 `BlockInput` or overlay when limit exceeded
- Add Windows service for background tracking without UI
- Add historical charts (week/month) from SQLite
