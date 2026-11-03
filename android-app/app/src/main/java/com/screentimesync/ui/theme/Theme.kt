package com.screentimesync.ui.theme

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

private val DarkColorScheme = darkColorScheme(
    primary = Color(0xFF6C5CE7),
    onPrimary = Color.White,
    primaryContainer = Color(0xFF2D2A4A),
    secondary = Color(0xFF00CEC9),
    background = Color(0xFF0B0E14),
    surface = Color(0xFF151A26),
    surfaceVariant = Color(0xFF232A3D),
    onSurface = Color(0xFFF8F9FF),
    onSurfaceVariant = Color(0xFF9AA0B8),
    error = Color(0xFFE17055),
    outline = Color(0xFF2A3350)
)

private val Typography = Typography(
    headlineLarge = androidx.compose.ui.text.TextStyle(fontSize = 32.sp, fontWeight = FontWeight.Black, color = Color.White),
    headlineMedium = androidx.compose.ui.text.TextStyle(fontSize = 20.sp, fontWeight = FontWeight.Bold),
    titleLarge = androidx.compose.ui.text.TextStyle(fontSize = 18.sp, fontWeight = FontWeight.SemiBold),
    bodyMedium = androidx.compose.ui.text.TextStyle(fontSize = 14.sp, color = Color(0xFF9AA0B8)),
    labelSmall = androidx.compose.ui.text.TextStyle(fontSize = 11.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp)
)

@Composable
fun ScreenTimeSyncTheme(darkTheme: Boolean = isSystemInDarkTheme(), content: @Composable () -> Unit) {
    MaterialTheme(colorScheme = DarkColorScheme, typography = Typography, content = content)
}
