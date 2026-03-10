# G19USB

[![NuGet](https://img.shields.io/nuget/v/G19USB.svg)](https://www.nuget.org/packages/G19USB)
[![CI](https://github.com/MagicMau/G19USB/actions/workflows/ci.yml/badge.svg)](https://github.com/MagicMau/G19USB/actions/workflows/ci.yml)

A .NET library for controlling the **Logitech G19 / G19s** keyboard's 320×240 colour LCD display and programmable G/M/L-keys over direct USB, using the libusbK driver. No Logitech Gaming Software required.

---

## Driver Setup

MirrorG19s (and this library) talks directly to the G19s LCD over USB. You need to replace the default Logitech driver for **the G19s composite parent interface** with the libusbK driver using Zadig.

1. **Uninstall Logitech Gaming Software** (LGS) if installed — it conflicts with the libusbK driver.
2. Download **Zadig** from <https://zadig.akeo.ie> and run it.
3. In Zadig, open **Options** and:
   - Check **List All Devices**
   - Uncheck **Ignore Hubs or Composite Parents**
4. In the device dropdown, select **G19s Gaming Keyboard (Composite Parent)** with USB ID `046D C229`.
   > There may be two entries with similar names — pick the one whose USB ID ends in **C229**.
5. In the driver dropdown (right of the green arrow) select **libusbK** (not WinUSB).
6. Click **Replace Driver**.

> ⚠️ Only replace the driver for the G19s composite parent (ID `046D C229`). Do **not** replace drivers for other Logitech devices you still rely on Logitech software for.
>
> To revert, open Device Manager, find the device under the libusbK node, right-click → **Update driver → Let Windows find one automatically**.

---

## Installation

```
dotnet add package G19USB
```

Requires **.NET 10** (Windows only).

---

## Quick Start

```csharp
using G19USB;

using var device = new G19Device();
device.OpenDevice();

// Subscribe to key events
device.KeysChanged += (_, e) =>
{
    if (e.Keys != G19Keys.None)
        Console.WriteLine($"Keys pressed: {G19Helpers.GetKeyString(e.Keys)}");
};
device.StartKeyboardMonitoring();

// Send a frame to the 320×240 LCD (153600-byte RGB565 pixel buffer)
byte[] frame = G19Helpers.CreateTestPattern();
device.UpdateLcd(frame);

// Light up the M1 LED
device.SetMKeyLEDs(G19Keys.M1);

Console.ReadKey(true);
device.StopKeyboardMonitoring();
device.CloseDevice();
```

---

## API Overview

| Type           | Description                                                                                              |
| -------------- | -------------------------------------------------------------------------------------------------------- |
| `IG19Device`   | Interface: `KeysChanged` event, `OpenDevice/CloseDevice`, `UpdateLcd`, `SetMKeyLEDs`                     |
| `G19Device`    | Concrete implementation combining `LCD` and `Keyboard` sub-components                                    |
| `LCD`          | Async write queue for bulk-USB LCD updates; `UpdateScreen(byte[])`, `SetBacklightColor`, `SetBrightness` |
| `Keyboard`     | Interrupt-IN polling for L-keys (0x81) and G/M-keys (0x83); fires `KeysChanged`                          |
| `G19Keys`      | `[Flags]` enum: `LHome`…`LUp` (bits 0–7), `G1`–`G12` (bits 8–19), `M1/M2/M3/MR` (bits 20–23)             |
| `G19Constants` | USB VID/PID (`0x046D`/`0xC229`), LCD dimensions (320×240), endpoint addresses, LCD header                |
| `G19Helpers`   | `ConvertBitmapToRGB565`, `CreateSolidColor`, `CreateTestPattern`, `GetKeyString`                         |

---

## Building from Source

```powershell
git clone https://github.com/MagicMau/G19USB.git
cd G19USB
dotnet build G19USB\G19USB.csproj -c Release
```

Run the basic sample (requires a connected G19/G19s with libusbK driver):

```powershell
dotnet run --project samples\G19USB.BasicSample
```

---

## Attribution

Based on **libg19** by James Geboski: <https://github.com/jgeboski/libg19>

See [NOTICE](NOTICE) for full attribution details.

---

## License

[Apache License 2.0](LICENSE)
