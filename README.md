# ASTERIQ

**Unified HOTAS Management System for Star Citizen**

Asteriq is a single application that replaces the need for separate tools like vJoy, HidHide, and JoystickGremlin when setting up HOTAS controllers for Star Citizen. Configure your physical devices, assign them to virtual joystick slots, map inputs, and export bindings — all in one place.

---

## Features

- **Device Management** — Assign physical HOTAS devices to vJoy virtual joystick slots with stable, session-persistent identity
- **Input Mapping** — Map axes, buttons, and hats from physical devices to virtual outputs with curves and modifiers
- **Star Citizen Bindings** — Browse and export your SC keybindings; never manually edit actionmaps.xml
- **HidHide Integration** — Hide physical devices from other applications while Asteriq forwards their input through virtual devices
- **FUI Theme System** — Dark futuristic interface with multiple manufacturer-inspired colour themes (Drake, Aegis, Anvil, Origin, and more)
- **SDL2 Input** — Reliable input via SDL2, not DirectInput

---

## Requirements

- Windows 10/11 (64-bit)
- [.NET 8 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- [vJoy 2.x](https://github.com/jshafer817/vJoy) — virtual joystick driver
- [HidHide](https://github.com/nefarius/HidHide) — device hiding driver

---

## Getting Started

1. Install the requirements above
2. Download the latest release from the [Releases](../../releases) page
3. Run `Asteriq.exe`
4. Go to **Devices** → assign your physical devices to vJoy slots
5. Go to **Mappings** → configure axis and button mappings
6. Go to **Keybindings** → load your Star Citizen bindings and export

---

## Building from Source

```bash
git clone git@github.com:MazzeeStudio/Asteriq.git
cd Asteriq

dotnet build src/Asteriq/Asteriq.csproj
dotnet run --project src/Asteriq

# Single-file release build
dotnet publish src/Asteriq/Asteriq.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

---

## Support

If Asteriq has been useful to you, consider supporting development:

- [Buy Me a Coffee](https://buymeacoffee.com/nerosilentr)
- New to Star Citizen? Use referral code **STAR-RBDQ-Z4JG** and earn 50,000 Bonus aUEC — [Enlist here](https://www.robertsspaceindustries.com/enlist?referral=STAR-RBDQ-Z4JG)

---

## License

[PolyForm Noncommercial License 1.0.0](LICENSE) — free for personal and non-commercial use. You may not sell this software or use it as the basis of a commercial product or service.

Copyright (c) 2025 MazzeeStudio
