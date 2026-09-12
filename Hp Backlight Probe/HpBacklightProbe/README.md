<div align="center">

# ⌨️ HP Keyboard Backlight Controller

**A dark, glass-morphism desktop app and CLI for controlling keyboard backlight power and brightness on HP laptops with single-backlit keyboards — no OMEN Gaming Hub required.**

![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-2f2f2f?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-8.0-2f2f2f?style=flat-square)
![License](https://img.shields.io/badge/license-MIT-2f2f2f?style=flat-square)
![Status](https://img.shields.io/badge/status-Phase%201-C9A24B?style=flat-square)

</div>

---

## 💻 Device Compatibility

> [!IMPORTANT]
> **Compatibility Requirement:** This tool is designed **specifically for HP laptops featuring single-zone / monochrome backlit keyboards** (e.g., **HP Victus**, **HP Pavilion**, **HP Envy**, and single-backlit **HP OMEN** models) that interface via HP's BIOS WMI service (`root\wmi → hpqBIntM`).

- **🔍 Automatic Device Detection:** The application automatically queries Windows SMBIOS and WMI hardware descriptors on launch to detect your specific laptop model (e.g., *Victus by HP Gaming Laptop 15-fa...*, *HP Pavilion 15*, etc.) and displays your exact device name in the UI header and status panel.
- **✅ Supported:** Any HP laptop with a single-color (white/monochrome) backlit keyboard supported by HP's standard BIOS WMI firmware interface.
- **❌ Not Supported:** Non-HP laptops (Lenovo, Dell, Asus, Acer, etc.) or keyboards with multi-zone/per-key RGB controllers that require separate proprietary USB/HID software.

---

## About

HP's OMEN Gaming Hub is heavyweight, telemetry-hungry, and sometimes doesn't even ship the backlight toggle for Victus models. This project talks directly to the same firmware interface HP's own software uses — `root\wmi → hpqBIntM`, method `hpqBIOSInt128` — so you can turn your keyboard backlight on/off and set brightness from a lightweight app or a one-line terminal command.

It comes in two forms that share the same underlying controller:



---

## Features

- 🔍 **Automatic Device Detection** — dynamically identifies and displays your exact laptop model (*Victus, Pavilion, OMEN, Envy, etc.*)
- 🎛️ **Master Power & Brightness Control** — live keyboard glow preview, 0–100% slider, and 1-click presets
- ✨ **Dynamic Lighting Effects Engine:**
  - 💡 **Static:** Constant backlight with manual slider brightness control.
  - 🫁 **Breathing:** Slowly and smoothly fades the keyboard backlight up and down (adjustable speed & brightness range).
  - 💓 **Pulse:** Sharp rhythmic brightness spike followed by smooth exponential decay (adjustable speed & intensity).
  - ⌨️ **Typing Pulse (Reactive):** Uses a lightweight global keyboard hook to detect typing activity — briefly flares up the backlight on every keystroke and decays back to base brightness.
  - 🌊 **Slow Fade:** Smooth ease-in-out transitions to target brightness levels (e.g. 0%, 30%, 70%, 100%).
  - ⚡ **Notification Flash:** Flashes the backlight a configurable number of times (1x, 2x, 3x, 5x) for visual alerts.
- 🖥️ **CLI Commands** for scripting, hotkeys, and automation (`on`, `off`, `toggle`, `set <0-100>`, `status`, `probe`)
- 🔍 **Hardware / Firmware Panel** — BIOS version, board product ID, keyboard type, raw WMI registers
- 🩺 **Safe Diagnostic Probe** restricted to non-destructive registers

---

## Prerequisites

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- **Administrator privileges** — Windows restricts `root\wmi\hpqBIntM` writes to elevated processes. Run your terminal (or your IDE) as Administrator.

---

## 🚀 How to Run (Step-by-Step)

Follow these steps to launch the graphical app or control the keyboard backlight via CLI commands.

### Step 1: Open PowerShell as Administrator
1. Press <kbd>Win</kbd> + <kbd>X</kbd> on your keyboard and select **Terminal (Admin)** or **Windows PowerShell (Admin)**.  
   *(Alternatively: Press <kbd>Win</kbd>, search for **PowerShell**, right-click **Windows PowerShell**, and select **Run as administrator**).*
2. When the User Account Control (UAC) prompt appears, click **Yes**.

> [!IMPORTANT]
> **Administrator privileges are required** because Windows restricts direct WMI hardware/BIOS communication (`root\wmi\hpqBIntM`) to elevated processes.

---

### Step 2: Navigate to the Project Directory
In your elevated PowerShell window, use the `cd` (Change Directory) command followed by the path where `HpBacklightProbe.csproj` is located.

> [!TIP]
> You can quickly copy the folder path by navigating to it in File Explorer, clicking the address bar at the top (or pressing <kbd>Ctrl</kbd> + <kbd>L</kbd>), and copying the path.

**Example command:**
```powershell
cd "C:\Users\moham\Downloads\HpBacklightProbe2-classy\HpBacklightProbe"
```
*(If your folder path contains spaces, make sure to wrap it in quotation marks).*

To confirm you are in the right folder, run:
```powershell
Test-Path HpBacklightProbe.csproj
# Output should return: True
```

---

### Step 3: Run the Application

#### 🖥️ Option A: Launch the Desktop GUI
To open the dark, glass-morphism desktop interface:
```powershell
dotnet run
```
*(Or explicitly: `dotnet run -- gui`)*

#### ⌨️ Option B: Run CLI Commands (Headless / Terminal)
You can directly control your keyboard backlight using command-line arguments:

```powershell
# View hardware info and current backlight status
dotnet run -- status

# Turn the keyboard backlight ON
dotnet run -- on

# Turn the keyboard backlight OFF
dotnet run -- off

# Toggle the backlight (switches ON if OFF, OFF if ON)
dotnet run -- toggle

# Set brightness to a specific percentage (0 to 100)
dotnet run -- set 75

# Run a safe, read-only diagnostic probe dump
dotnet run -- probe

# View the full CLI help menu
dotnet run -- help
```

---

### Step 4 (Optional): Build a Standalone Executable (.exe)
If you want to run the tool directly as a `.exe` without typing `dotnet run` every time:

```powershell
# Build a release binary into the .\publish folder
dotnet publish -c Release -r win-x64 --self-contained false -o .\publish
```

Then you can run it directly:
```powershell
.\publish\HpBacklightProbe.exe         # Launches GUI
.\publish\HpBacklightProbe.exe on      # Turns backlight ON
.\publish\HpBacklightProbe.exe off     # Turns backlight OFF
.\publish\HpBacklightProbe.exe toggle  # Toggles backlight
.\publish\HpBacklightProbe.exe set 100 # Sets brightness to 100%
```

---

## ⚠️ Safety Notes

`hpqBIOSInt128` isn't a normal Windows API call — each invocation triggers a real **SMI (System Management Interrupt)** inside BIOS firmware, which can block every CPU core on the machine below the OS level. An unrecognized command/sub-command pair can cause firmware to hang waiting on a handshake that never arrives, freezing the entire machine (not just this app) until a hard power-off.

This repo only sends `command`/`commandType` values that are documented in [Technical Details](#technical-details) or already proven safe by `GetInfo()` / the GUI. **If you're extending this project, do not add unverified/guessed register values to `HpWmiClient.Query()`** — test one register at a time, on a machine you can afford to hard-reboot, with work saved first.

---

## Technical Details

| | |
|---|---|
| **WMI Namespace** | `root\wmi` |
| **Class** | `hpqBIntM` |
| **Method** | `hpqBIOSInt128` |
| **Signature** | `"SECU"` (`0x55434553`, little-endian) |
| **Command Domain** | `0x20009` (Lighting & Backlight) |

**Sub-commands:**

| Code | Meaning |
|---|---|
| `0x01` | Capabilities / lighting-supported query |
| `0x02` | Color/state table read |
| `0x03` | Color/state table write |
| `0x04` | Brightness read |
| `0x05` | Brightness write |

---

## Roadmap

- [ ] Per-zone RGB support for multi-zone keyboards
- [ ] System tray quick-toggle
- [ ] Global hotkey binding
- [ ] Auto-start with Windows (optional, off by default)

---

## Contributing

Issues and PRs are welcome. If you're adding new WMI register calls, please read the [Safety Notes](#️-safety-notes) first — freezing a contributor's laptop is not a good first-issue experience.

## Disclaimer

This is an independent, reverse-engineered tool and is **not affiliated with or endorsed by HP Inc.** Use at your own risk. See [LICENSE](LICENSE) for the full "as-is" terms.

## License

[MIT](LICENSE)
