package com.screentimesync.ui.screens

import androidx.compose.foundation.*
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.screentimesync.bluetooth.BluetoothViewModel
import com.screentimesync.data.AppUsage

@Composable
fun DashboardScreen(btViewModel: BluetoothViewModel, onNavigateSettings: ()->Unit, onNavigatePair: ()->Unit) {
    val state by btViewModel.uiState.collectAsState()

    Column(modifier = Modifier.fillMaxSize().background(Color(0xFF0B0E14)).padding(16.dp)) {
        // Top Bar
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Column {
                Text("ScreenTimeSync", style = MaterialTheme.typography.headlineLarge.copy(fontSize = 22.sp))
                Text("Connected to DESKTOP-PC", color = Color(0xFF9AA0B8), fontSize = 12.sp)
            }
            Box(modifier = Modifier.clip(CircleShape).background(if(state.isConnected) Color(0xFF00E676) else Color(0xFFE17055)).padding(horizontal = 10.dp, vertical = 6.dp)) {
                Text(if(state.isConnected) "● LIVE" else "○ OFFLINE", color = Color.White, fontSize = 11.sp, fontWeight = FontWeight.Bold)
            }
        }

        Spacer(Modifier.height(16.dp))

        // Total Card - hero
        Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF151A26)), shape = RoundedCornerShape(20.dp), modifier = Modifier.fillMaxWidth()) {
            Column(Modifier.padding(20.dp)) {
                Text("TODAY'S TOTAL", color = Color(0xFF9AA0B8), fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp)
                Spacer(Modifier.height(6.dp))
                Text(state.totalTimeFormatted, color = Color.White, fontSize = 42.sp, fontWeight = FontWeight.Black)
                Spacer(Modifier.height(4.dp))
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(8.dp).clip(CircleShape).background(Color(0xFF00E676)))
                    Spacer(Modifier.width(6.dp))
                    Text("Active: ${state.currentApp} • ${if(state.isIdle) "Idle" else "Tracking"}", color = Color(0xFF9AA0B8), fontSize = 12.sp)
                }
                Spacer(Modifier.height(16.dp))
                // Progress bar simulated
                Box(modifier = Modifier.fillMaxWidth().height(8.dp).clip(RoundedCornerShape(4.dp)).background(Color(0xFF232A3D))) {
                    Box(modifier = Modifier.fillMaxHeight().fillMaxWidth(state.progressFraction).clip(RoundedCornerShape(4.dp)).background(Color(0xFF6C5CE7)))
                }
                Spacer(Modifier.height(8.dp))
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text("${(state.progressFraction*100).toInt()}% of daily goal (6h)", color = Color(0xFF6B7280), fontSize = 11.sp)
                    Text("2h 12m left", color = Color(0xFF00CEC9), fontSize = 11.sp, fontWeight = FontWeight.Bold)
                }
            }
        }

        Spacer(Modifier.height(16.dp))

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text("Apps Today", color = Color.White, fontSize = 18.sp, fontWeight = FontWeight.Bold)
            TextButton(onClick = onNavigatePair) { Text("Pair PC", color = Color(0xFF6C5CE7)) }
        }
        Text("Tap an app to edit its daily limit — syncs in <1s", color = Color(0xFF5A6380), fontSize = 11.sp)
        Spacer(Modifier.height(10.dp))

        if (!state.isConnected) {
            Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF1A2338)), shape = RoundedCornerShape(12.dp), modifier = Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("⚡ Not connected to PC", color = Color.White, fontWeight = FontWeight.Bold)
                    Text("Enable Bluetooth and pair with 'ScreenTimeSync-PC'", color = Color(0xFF9AA0B8), fontSize = 12.sp)
                    Spacer(Modifier.height(10.dp))
                    Button(onClick = onNavigatePair, colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF6C5CE7))) {
                        Text("Scan & Connect")
                    }
                }
            }
            Spacer(Modifier.height(12.dp))
        }

        LazyColumn(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            items(state.apps) { app ->
                AppRow(app = app, onLimitChange = { mins -> btViewModel.setLimit(app.id, mins) })
            }
            item {
                Spacer(Modifier.height(16.dp))
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                    OutlinedButton(onClick = { btViewModel.resetToday() }, shape = RoundedCornerShape(12.dp)) { Text("Reset Today") }
                    Button(onClick = onNavigateSettings, colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF2D2A4A))) { Text("Settings") }
                }
                Spacer(Modifier.height(80.dp))
            }
        }
    }
}

@Composable
fun AppRow(app: AppUsage, onLimitChange: (Int)->Unit) {
    var showDialog by remember { mutableStateOf(false) }
    val accent = when(app.category) {
        "Browser" -> Color(0xFF6C5CE7)
        "Development" -> Color(0xFF0984E3)
        "Social" -> Color(0xFFE84393)
        "Gaming" -> Color(0xFFFDCB6E)
        "Productivity" -> Color(0xFF00B894)
        "Media" -> Color(0xFFE17055)
        else -> Color(0xFF636E72)
    }

    Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF151A26)), shape = RoundedCornerShape(16.dp), modifier = Modifier.fillMaxWidth().clickable { showDialog = true }) {
        Row(Modifier.padding(14.dp), verticalAlignment = Alignment.CenterVertically) {
            Box(modifier = Modifier.size(44.dp).clip(RoundedCornerShape(10.dp)).background(accent.copy(alpha=0.15f)), contentAlignment = Alignment.Center) {
                Text(app.name.take(1).uppercase(), color = accent, fontWeight = FontWeight.Bold, fontSize = 16.sp)
            }
            Spacer(Modifier.width(12.dp))
            Column(Modifier.weight(1f)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(app.name, color = Color.White, fontWeight = FontWeight.SemiBold, fontSize = 14.sp)
                    if (app.isActive) {
                        Spacer(Modifier.width(6.dp))
                        Box(Modifier.clip(RoundedCornerShape(6.dp)).background(Color(0xFF003E1F)).padding(horizontal = 6.dp, vertical = 2.dp)) {
                            Text("ACTIVE", color = Color(0xFF00E676), fontSize = 9.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                    if (app.isLimited) {
                        Spacer(Modifier.width(4.dp))
                        Box(Modifier.clip(RoundedCornerShape(6.dp)).background(Color(0xFF3A1F3A)).padding(horizontal = 6.dp, vertical = 2.dp)) {
                            Text("LIMITED", color = Color(0xFFE84393), fontSize = 9.sp, fontWeight = FontWeight.Bold)
                        }
                    }
                }
                Text(app.category, color = accent, fontSize = 11.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(6.dp))
                Box(Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)).background(Color(0xFF1E2438))) {
                    val frac = if(app.isLimited && app.limitMinutes>0) (app.minutes.toFloat()/app.limitMinutes).coerceIn(0f,1f) else 0.35f
                    Box(Modifier.fillMaxHeight().fillMaxWidth(frac).clip(RoundedCornerShape(3.dp)).background(accent))
                }
                Spacer(Modifier.height(4.dp))
                Text(if(app.isLimited) "${app.limitMinutes/60}h ${app.limitMinutes%60}m limit • ${app.minutes}m used" else "${app.minutes}m today • no limit", color = Color(0xFF5A6380), fontSize = 11.sp)
            }
            Column(horizontalAlignment = Alignment.End) {
                Text(app.displayTime, color = Color.White, fontWeight = FontWeight.Bold, fontSize = 15.sp)
                Text("${app.minutes}m", color = Color(0xFF6B7280), fontSize = 11.sp)
            }
        }
    }

    if (showDialog) {
        var sliderValue by remember { mutableStateOf(app.limitMinutes.toFloat().coerceAtLeast(15f).coerceAtMost(360f)) }
        var enabled by remember { mutableStateOf(app.isLimited) }
        AlertDialog(
            onDismissRequest = { showDialog = false },
            title = { Text("Edit limit — ${app.name}", color = Color.White) },
            text = {
                Column {
                    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth()) {
                        Text("Enable limit", color = Color.White, modifier = Modifier.weight(1f))
                        Switch(checked = enabled, onCheckedChange = { enabled = it })
                    }
                    Spacer(Modifier.height(12.dp))
                    Text("${sliderValue.toInt()/60}h ${sliderValue.toInt()%60}m", color = Color(0xFF6C5CE7), fontWeight = FontWeight.Bold, fontSize = 18.sp)
                    Slider(value = sliderValue, onValueChange = { sliderValue = it }, valueRange = 15f..360f, steps = 22, enabled = enabled)
                    Text("15 min — 6 hours", color = Color(0xFF5A6380), fontSize = 11.sp)
                    Spacer(Modifier.height(8.dp))
                    Text("Changes sync instantly via Bluetooth LE to your PC.", color = Color(0xFF9AA0B8), fontSize = 11.sp)
                }
            },
            confirmButton = {
                Button(onClick = {
                    onLimitChange(if(enabled) sliderValue.toInt() else 0)
                    showDialog = false
                }, colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF6C5CE7))) { Text(if(enabled) "Save Limit" else "Remove Limit") }
            },
            dismissButton = { TextButton(onClick = { showDialog=false }) { Text("Cancel") } },
            containerColor = Color(0xFF151A26),
            titleContentColor = Color.White
        )
    }
}
