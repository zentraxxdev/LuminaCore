# LuminaCore

**LuminaCore** is a high-performance, user-space Windows ARGB lighting control suite built with **.NET 8** and **Edge WebView2**. It drives motherboard and Wi-Fi ARGB hardware at a locked **60 FPS** without kernel drivers, keeping it safe for kernel anti-cheats (Ricochet, Vanguard).

## Key Features
- **Multi-Vendor Native USB HID Engine:** Direct user-space HID support for **ASRock Polychrome USB** (`26CE:01A2`), **ASUS Aura USB**, **Gigabyte RGB Fusion 2.0**, **MSI Mystic Light**, **WLED Wi-Fi UDP** (`21324`), plus **OpenRGB SDK** fallback.
- **Locked 60 FPS Render Pipeline:** Zero-allocation background `PeriodicTimer` engine with decoupled async USB HID flushing and throttled 25 FPS WebView2 bridge.
- **24 Unified Themes:** Simultaneous UI + multi-channel hardware scenes with custom theme creator and `LUMINA-THEME-...` share codes.
- **Hot-Reload Lua Plugins:** Drop `.lua` shader scripts into `Plugins/` for instant live reloading via MoonSharp.
- **Pro HID Calibration & Diagnostics:** Per-channel color order fixer (`GRB`/`RGB`/`BRG`/`RBG`), interactive green LED count calibrator, and live packet latency readout.
- **Reactive Effects:** DXGI Desktop Duplication Screen Ambilight, WASAPI Loopback FFT Audio Visualizer, and LibreHardwareMonitor CPU/GPU Thermal curves.
- **Windows Integration:** Single-instance lock, Sleep/Wake USB auto-recovery, System Tray, `Ctrl + Alt + L` global Stealth Blackout hotkey, Local Web Remote (`localhost:5050`), and EN/TR language toggle.

## Build & Publish
```powershell
.\scripts\publish-release.ps1
```
