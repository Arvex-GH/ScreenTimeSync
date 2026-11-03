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
import com.screentimesync.bluetooth.BluetoothViewModel

@Composable
fun PairingScreen(btViewModel: BluetoothViewModel, onBack: ()->Unit) {
    val state by btViewModel.uiState.collectAsState()
    Column(modifier = Modifier.fillMaxSize().background(Color(0xFF0B0E14)).padding(16.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onBack) { Text("← Back", color = Color(0xFF9AA0B8)) }
            Spacer(Modifier.weight(1f))
            Text("Pair to PC", color = Color.White, fontWeight = FontWeight.Bold, fontSize = 18.sp)
            Spacer(Modifier.weight(1f))
            Spacer(Modifier.width(60.dp))
        }
        Spacer(Modifier.height(20.dp))
        Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF151A26)), shape = RoundedCornerShape(20.dp), modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(20.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                Text("🔍", fontSize = 40.sp)
                Spacer(Modifier.height(12.dp))
                Text("Scanning for PC...", color = Color.White, fontWeight = FontWeight.Bold, fontSize = 16.sp)
                Text("Make sure Bluetooth is ON and ScreenTimeSync is running on your Windows PC", color = Color(0xFF9AA0B8), fontSize = 12.sp)
                Spacer(Modifier.height(16.dp))
                if (state.isScanning) LinearProgressIndicator(modifier = Modifier.fillMaxWidth(), color = Color(0xFF6C5CE7))
                Spacer(Modifier.height(16.dp))
                Button(onClick = { btViewModel.startScan() }, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF6C5CE7))) {
                    Text(if(state.isScanning) "Scanning..." else "Start Scan")
                }
            }
        }
        Spacer(Modifier.height(16.dp))
        Text("AVAILABLE DEVICES", color = Color(0xFF5A6380), fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp)
        Spacer(Modifier.height(8.dp))
        // Mock devices
        listOf("ScreenTimeSync-PC (DESKTOP-9A2F)" to -42, "DESKTOP-JAN" to -68).forEach { (name, rssi) ->
            Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF151A26)), shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
                Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                    Box(modifier = Modifier.size(40.dp).background(Color(0xFF1E2438), RoundedCornerShape(10.dp)), contentAlignment = Alignment.Center) { Text("💻") }
                    Spacer(Modifier.width(12.dp))
                    Column(Modifier.weight(1f)) {
                        Text(name, color = Color.White, fontWeight = FontWeight.SemiBold, fontSize = 13.sp)
                        Text("$rssi dBm • Bluetooth LE", color = Color(0xFF6B7280), fontSize = 11.sp)
                    }
                    Button(onClick = { btViewModel.connectTo(name) }, colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF6C5CE7)), shape = RoundedCornerShape(8.dp)) {
                        Text("Connect", fontSize = 12.sp)
                    }
                }
            }
        }
        Spacer(Modifier.height(16.dp))
        Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF102A43)), shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp)) {
                Text("How pairing works", color = Color.White, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                Spacer(Modifier.height(6.dp))
                Text("1. Open ScreenTimeSync on Windows — it advertises as 'ScreenTimeSync-PC'\n2. Tap Connect on your phone\n3. Accept pairing on both devices (no PIN needed)\n4. Real-time sync starts — edit limits instantly", color = Color(0xFF9AA0B8), fontSize = 11.sp, lineHeight = 15.sp)
            }
        }
    }
}
