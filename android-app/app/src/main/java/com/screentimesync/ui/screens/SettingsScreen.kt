package com.screentimesync.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

@Composable
fun SettingsScreen(onBack: ()->Unit) {
    Column(modifier = Modifier.fillMaxSize().background(Color(0xFF0B0E14)).padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onBack) { Text("← Back", color = Color(0xFF9AA0B8)) }
            Spacer(Modifier.width(8.dp))
            Text("Settings", color = Color.White, fontWeight = FontWeight.Bold, fontSize = 18.sp)
        }
        Spacer(Modifier.height(20.dp))
        Section("Notifications") {
            SettingRow("Limit warnings", "Notify when app reaches 80% of limit", true)
            SettingRow("Daily summary", "Evening report at 9 PM", true)
        }
        Spacer(Modifier.height(16.dp))
        Section("Privacy") {
            Text("All tracking stays local on your PC. Phone only receives aggregated stats via Bluetooth — no cloud, no account.", color = Color(0xFF9AA0B8), fontSize = 12.sp)
            Spacer(Modifier.height(10.dp))
            OutlinedButton(onClick = {}, shape = RoundedCornerShape(10.dp), modifier = Modifier.fillMaxWidth()) { Text("Export Data (CSV)") }
        }
        Spacer(Modifier.height(16.dp))
        Section("About") {
            Text("ScreenTimeSync v1.0.0\nC# WPF • C++ Native Hook • Kotlin Compose\nBLE GATT UUID 6E400001-B5A3-...", color = Color(0xFF5A6380), fontSize = 11.sp)
        }
    }
}

@Composable
fun Section(title: String, content: @Composable ColumnScope.()->Unit) {
    Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF151A26)), shape = RoundedCornerShape(16.dp), modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text(title.uppercase(), color = Color(0xFF5A6380), fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp)
            Spacer(Modifier.height(10.dp))
            content()
        }
    }
}

@Composable
fun SettingRow(title: String, subtitle: String, checked: Boolean) {
    var c by remember { mutableStateOf(checked) }
    Row(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, color = Color.White, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
            Text(subtitle, color = Color(0xFF6B7280), fontSize = 11.sp)
        }
        Switch(checked = c, onCheckedChange = { c = it })
    }
}
