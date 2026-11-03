package com.screentimesync.ui.screens

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.collectAsState
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.navigation.compose.NavHost
import androidx.navigation.compose.composable
import androidx.navigation.compose.rememberNavController
import com.screentimesync.bluetooth.BluetoothViewModel

@Composable
fun AppNavHost() {
    val nav = rememberNavController()
    val btViewModel: BluetoothViewModel = viewModel()

    NavHost(navController = nav, startDestination = "dashboard") {
        composable("dashboard") {
            DashboardScreen(
                btViewModel = btViewModel,
                onNavigateSettings = { nav.navigate("settings") },
                onNavigatePair = { nav.navigate("pair") }
            )
        }
        composable("pair") {
            PairingScreen(btViewModel = btViewModel, onBack = { nav.popBackStack() })
        }
        composable("settings") {
            SettingsScreen(onBack = { nav.popBackStack() })
        }
    }
}
