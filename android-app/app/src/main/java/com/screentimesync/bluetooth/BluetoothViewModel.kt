package com.screentimesync.bluetooth

import android.app.Application
import android.bluetooth.*
import android.bluetooth.le.*
import android.content.Context
import android.os.ParcelUuid
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.screentimesync.data.AppUsage
import com.screentimesync.data.ScreenTimeState
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import org.json.JSONObject
import java.util.*

class BluetoothViewModel(app: Application) : AndroidViewModel(app) {
    private val _uiState = MutableStateFlow(ScreenTimeState())
    val uiState: StateFlow<ScreenTimeState> = _uiState

    private val bleManager: BluetoothManager = app.getSystemService(Context.BLUETOOTH_SERVICE) as BluetoothManager
    private val adapter: BluetoothAdapter? = bleManager.adapter
    private var gatt: BluetoothGatt? = null
    private var txChar: BluetoothGattCharacteristic? = null
    private var rxChar: BluetoothGattCharacteristic? = null

    companion object {
        val SERVICE_UUID: UUID = UUID.fromString("6e400001-b5a3-f393-e0a9-e50e24dcca9e")
        val TX_UUID: UUID = UUID.fromString("6e400003-b5a3-f393-e0a9-e50e24dcca9e") // PC -> Phone (notify)
        val RX_UUID: UUID = UUID.fromString("6e400002-b5a3-f393-e0a9-e50e24dcca9e") // Phone -> PC (write)
    }

    private val gattCallback = object : BluetoothGattCallback() {
        override fun onConnectionStateChange(gatt: BluetoothGatt, status: Int, newState: Int) {
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                _uiState.update { it.copy(isConnected = true) }
                gatt.discoverServices()
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                _uiState.update { it.copy(isConnected = false) }
            }
        }
        override fun onServicesDiscovered(gatt: BluetoothGatt, status: Int) {
            val svc = gatt.getService(SERVICE_UUID) ?: return
            txChar = svc.getCharacteristic(TX_UUID)
            rxChar = svc.getCharacteristic(RX_UUID)
            txChar?.let { enableNotifications(gatt, it) }
        }
        override fun onCharacteristicChanged(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic, value: ByteArray) {
            handleIncoming(String(value))
        }
        override fun onCharacteristicChanged(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic) {
            @Suppress("DEPRECATION")
            handleIncoming(String(characteristic.value))
        }
    }

    private fun enableNotifications(gatt: BluetoothGatt, char: BluetoothGattCharacteristic) {
        gatt.setCharacteristicNotification(char, true)
        val ccc = char.getDescriptor(UUID.fromString("00002902-0000-1000-8000-00805f9b34fb"))
        ccc?.let {
            it.value = BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE
            gatt.writeDescriptor(it)
        }
    }

    private fun handleIncoming(json: String) {
        try {
            val obj = JSONObject(json)
            if (obj.optString("type") != "sync") return
            val total = obj.optInt("totalMinutes", 0)
            val current = obj.optString("currentApp", "")
            val isIdle = obj.optBoolean("isIdle", false)
            val appsJson = obj.optJSONArray("apps") ?: return
            val apps = mutableListOf<AppUsage>()
            for (i in 0 until appsJson.length()) {
                val a = appsJson.getJSONObject(i)
                apps.add(AppUsage(
                    id = a.getString("id"),
                    name = a.getString("name"),
                    minutes = a.optInt("minutes", 0),
                    limitMinutes = a.optInt("limitMinutes", 0),
                    isLimited = a.optBoolean("isLimited", false),
                    category = a.optString("category", "Other"),
                    isActive = a.optBoolean("isActive", false)
                ))
            }
            _uiState.update { it.copy(totalMinutes = total, currentApp = current, isIdle = isIdle, apps = apps.sortedByDescending { a -> a.minutes }) }
        } catch (e: Exception) { /* ignore */ }
    }

    fun startScan() {
        _uiState.update { it.copy(isScanning = true) }
        val scanner = adapter?.bluetoothLeScanner ?: return
        val filter = ScanFilter.Builder().setServiceUuid(ParcelUuid(SERVICE_UUID)).build()
        val settings = ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build()
        scanner.startScan(listOf(filter), settings, scanCallback)
        viewModelScope.launch {
            kotlinx.coroutines.delay(8000)
            scanner.stopScan(scanCallback)
            _uiState.update { it.copy(isScanning = false) }
        }
    }

    private val scanCallback = object : ScanCallback() {
        override fun onScanResult(callbackType: Int, result: ScanResult) {
            // auto-connect to first ScreenTimeSync-PC found
            result.device?.let { dev ->
                if (dev.name?.contains("ScreenTimeSync") == true) {
                    connectToDevice(dev)
                    adapter?.bluetoothLeScanner?.stopScan(this)
                    _uiState.update { it.copy(isScanning = false) }
                }
            }
        }
    }

    fun connectTo(name: String) { startScan() } // mock: triggers scan

    private fun connectToDevice(device: BluetoothDevice) {
        try { gatt = device.connectGatt(getApplication(), false, gattCallback, BluetoothDevice.TRANSPORT_LE) } catch (_: SecurityException) {}
    }

    fun setLimit(appId: String, minutes: Int) {
        // Optimistic UI
        _uiState.update { s ->
            s.copy(apps = s.apps.map { if(it.id==appId) it.copy(limitMinutes = minutes, isLimited = minutes>0) else it })
        }
        // Send to PC via RX characteristic
        val payload = JSONObject().apply {
            put("type", if(minutes>0) "update_limit" else "remove_limit")
            put("appId", appId)
            put("limitMinutes", minutes)
        }.toString()
        rxChar?.let { c ->
            c.value = payload.toByteArray()
            c.writeType = BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT
            try { gatt?.writeCharacteristic(c) } catch (_: SecurityException) {}
        }
    }

    fun resetToday() {
        val payload = JSONObject().apply { put("type","reset_today") }.toString()
        rxChar?.let { c ->
            c.value = payload.toByteArray()
            try { gatt?.writeCharacteristic(c) } catch (_: SecurityException) {}
        }
        _uiState.update { it.copy(totalMinutes = 0, apps = it.apps.map { a -> a.copy(minutes = 0) }) }
    }

    override fun onCleared() {
        try { gatt?.disconnect(); gatt?.close() } catch(_:Exception){}
        super.onCleared()
    }
}
