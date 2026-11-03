package com.screentimesync.data

data class AppUsage(
    val id: String, // process name
    val name: String,
    val minutes: Int,
    val limitMinutes: Int = 0,
    val isLimited: Boolean = false,
    val category: String = "Other",
    val isActive: Boolean = false
) {
    val displayTime: String get() = "${minutes/60}h ${minutes%60}m"
}

data class ScreenTimeState(
    val isConnected: Boolean = true, // demo starts connected
    val isScanning: Boolean = false,
    val isIdle: Boolean = false,
    val currentApp: String = "Chrome",
    val totalMinutes: Int = 228, // 3h 48m demo
    val apps: List<AppUsage> = listOf(
        AppUsage("chrome", "Chrome", 134, 120, true, "Browser", true),
        AppUsage("code", "VS Code", 89, 0, false, "Development"),
        AppUsage("discord", "Discord", 45, 60, true, "Social"),
        AppUsage("spotify", "Spotify", 32, 0, false, "Media"),
        AppUsage("steam", "Steam", 28, 0, false, "Gaming"),
        AppUsage("notion", "Notion", 22, 60, true, "Productivity"),
        AppUsage("figma", "Figma", 18, 0, false, "Productivity"),
    )
) {
    val totalTimeFormatted: String get() = "${totalMinutes/60}h ${totalMinutes%60}m"
    val progressFraction: Float get() = (totalMinutes / 360f).coerceIn(0f, 1f)
}
