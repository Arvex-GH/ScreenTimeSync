# UI/UX Design

## Principles
- Dark-first, glassmorphism, high contrast for screen-time at-a-glance
- Real-time feel: LIVE pill, active pulse, <1s feedback
- Per-app: category color + progress vs limit

## Windows (WPF)
Palette: bg `#0B0E14`, card `#151A26`, border `#232A3D`, accent `#6C5CE7`, secondary `#00CEC9`, text `#F8F9FF` / `#9AA0B8`
Layout: Custom title bar (36px app dot + pill), 3-column grid (320 | * | 360), footer 28px
Left: 48sp total time + active pill + weekly 7-bar + phone card (pair code ST-8842 + latency)
Center: Search/filter, scroll app cards (44px icon, name, category, 6px bar, limit text, time right)
Right: 220px PieChart (LiveCharts2) + Quick Limits toggles
States: Active (green border), Limited (pink badge), Over-limit (red bar)

## Android (Compose Material 3)
Same dark scheme, 20dp cards, 42sp hero, LIVE green pill
Dashboard: hero total + progress bar (6h goal) + app list + bottom actions (Reset/Settings)
AppRow: 44dp tinted icon, ACTIVE/LIMITED pills, 6px bar, limit caption
Edit dialog: Switch + Slider 15–360min + info text
Pair screen: Scan spinner + device cards + 4-step how-to
Settings: grouped cards, notifications, privacy note

## Typography
Segoe UI Variable / Roboto, Black 32sp hero, Bold 18sp H2, 12sp caption (#9AA0B8, 1sp letter)
Radius 16–20dp, shadow 20blur 4dp 25% black, 8–10dp inner

## Assets
Icons: WPF-UI SymbolIcon, Emoji fallback, LiveCharts pie
Future: Lottie idle animation, confetti on goal hit
