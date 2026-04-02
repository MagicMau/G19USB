# G19USB

[![NuGet](https://img.shields.io/nuget/v/G19USB.svg)](https://www.nuget.org/packages/G19USB)
[![CI](https://github.com/MagicMau/G19USB/actions/workflows/ci.yml/badge.svg)](https://github.com/MagicMau/G19USB/actions/workflows/ci.yml)

G19USB is a Windows-only .NET library for talking directly to a Logitech **G19 / G19s** keyboard over USB. It exposes the hardware features that make the keyboard interesting in the first place:

- the **320×240 colour LCD**
- the **L-keys** beside the display
- the **G1-G12 programmable keys**
- the **M1/M2/M3/MR keys and M-key LEDs**
- the **keyboard backlight colour**
- the **LCD brightness control**

The library does **not** depend on Logitech's SDK. Instead, it uses direct USB access through **LibUsbDotNet** and the **libusbK** driver, so your application can own the device and drive it like a small external display with input buttons attached.

Typical uses include custom dashboards, build/status panels, monitoring tools, media/stream controls, telemetry screens, launcher UIs, and hardware test utilities.

## What G19USB actually gives you

At a glance, the public API is built around three main types:

- **`G19Device`** – the main facade; opens the USB device once and gives you both LCD and keyboard access.
- **`LCD`** – direct access to the display, brightness, and backlight colour.
- **`Keyboard`** – direct access to key monitoring and M-key LED control.

A few supporting types round things out:

- **`G19Keys`** – `[Flags]` enum covering L-keys, G-keys, and M-keys.
- **`G19KeyEventArgs`** – event payload with the current key state and timestamp.
- **`G19Helpers`** – helper methods for RGB565 frame generation and key formatting.
- **`G19Constants`** – LCD size, USB IDs, endpoint numbers, and frame size constants.

This means you can choose the level you want:

- use **`G19Device`** for most applications
- use **`LCD`** by itself for display-only tools
- use **`Keyboard`** by itself for input-only tools

## Platform, hardware, and driver prerequisites

Before you write a line of code, here are the non-negotiable bits:

- **Windows only.** The package targets **`net10.0-windows`**.
- You need a **Logitech G19 or G19s** connected over USB.
- Direct USB mode requires the **libusbK** driver.
- The driver must be installed on the keyboard's **composite parent device**, not just an individual interface node.
- Logitech software that already owns the device can conflict with direct USB access.

### Driver caveats that matter

G19USB talks straight to the USB device identified by **VID `046D` / PID `C229`**. The LCD and keyboard functionality live on separate USB interfaces, and `G19Device` claims both of them.

That is why the driver setup matters so much: if you install a driver on the wrong node, device discovery can look almost right while `OpenDevice()` still refuses to play along. USB has a sense of humour like that.

### Recommended driver setup with Zadig

1. **Close or uninstall Logitech software** that might already be using the keyboard.
   - At minimum, do not have Logitech Gaming Software driving the device while testing G19USB.
2. Download **Zadig** from <https://zadig.akeo.ie> and run it as Administrator.
3. In Zadig, open **Options** and:
   - enable **List All Devices**
   - disable **Ignore Hubs or Composite Parents**
4. In the device list, choose the **G19/G19s composite parent** entry with USB ID **`046D C229`**.
   - On many systems this appears as **`G19s Gaming Keyboard (Composite Parent)`**.
   - If several similar names appear, choose the one whose ID ends in **`C229`** and is the **composite parent**, not an `MI_00` / `MI_01` style interface entry.
5. In the driver dropdown, select **`libusbK`**.
6. Click **Replace Driver**.

> [!WARNING]
> Install the driver on the **composite parent device for the G19/G19s only**.
> Do **not** replace drivers for unrelated Logitech devices you still want Logitech software to manage.

> [!IMPORTANT]
> This repository and README assume **libusbK**. If the device is visible but `OpenDevice()` fails, the usual culprits are:
> - the wrong USB node got the driver
> - another application still owns the device
> - the driver install needs to be redone on the composite parent

To revert the driver, open **Device Manager**, locate the device under the libusbK node, and let Windows reinstall the default driver.

## Installation

### Use the NuGet package

```powershell
dotnet add package G19USB
```

Your application should target **Windows** on **.NET 10**:

```xml
<TargetFramework>net10.0-windows</TargetFramework>
```

The NuGet package gives you the managed library. You still need the **libusbK** driver on the hardware itself.

It also embeds this `README.md` as the package readme and ships `G19USB.xml` alongside the assembly, so IDEs and other tooling can surface both the overview and the XML API documentation directly from the installed package.

### Build from source

The repository includes the library, unit tests, and a small sample application.

```powershell
git clone https://github.com/MagicMau/G19USB.git
cd G19USB
dotnet build G19USB.slnx
dotnet test G19USB.slnx
dotnet run --project samples\G19USB.BasicSample
```

Notes:

- `global.json` pins the SDK to **10.0.101** with minor-version roll-forward.
- The test project covers constants and LCD encoding logic; it does **not** need real hardware.
- The sample app **does** need a connected G19/G19s with the correct driver installed.

## Architecture and mental model

The device is treated as one USB device with two interesting interfaces:

```text
Logitech G19/G19s (VID 046D, PID C229)
├─ Interface 0 -> LCD output
│  └─ Endpoint 0x02 (bulk OUT)
└─ Interface 1 -> keyboard input / LEDs
   ├─ Endpoint 0x81 (interrupt IN, L-keys)
   └─ Endpoint 0x83 (interrupt IN, G-keys + M-keys)
```

### How the library layers map to that

| Type | Role |
| --- | --- |
| `G19Device` | Main facade. Opens the USB device once, claims both interfaces, exposes `LCD` and `Keyboard`, forwards `KeysChanged`, and provides convenience methods like `UpdateLcd(...)` and `SetMKeyLEDs(...)`. |
| `LCD` | Handles LCD frame writes, brightness, and backlight colour. This is the type to use when your app is effectively a custom display renderer. |
| `Keyboard` | Polls the L-key and G/M-key interrupt endpoints on background tasks and raises `KeysChanged` when the overall key state changes. |
| `G19Helpers` | Converts `Bitmap` content to the device's RGB565 frame layout, creates solid-colour/test frames, and formats key flags for logging. |

### The important lifecycle rule

Open once, reuse the object, close when done.

For long-running apps, the normal shape is:

1. create `G19Device`, `LCD`, or `Keyboard`
2. call `OpenDevice()`
3. subscribe to events / push frames / set LEDs
4. if using keyboard input, start monitoring
5. stop monitoring before shutdown
6. call `CloseDevice()`
7. dispose the object

## Practical API overview

### Common operations

| Goal | Facade (`G19Device`) | Direct type |
| --- | --- | --- |
| Detect hardware | `G19Device.GetDeviceCount()` | n/a |
| Open both LCD + keyboard | `device.OpenDevice()` | n/a |
| Check readiness | `device.IsAvailable` | `lcd.IsAvailable`, `keyboard.IsAvailable` |
| Update the LCD | `device.UpdateLcd(frame)` | `lcd.UpdateScreen(frame)` |
| Set LCD brightness | `device.LCD.SetBrightness(0..100)` | `lcd.SetBrightness(0..100)` |
| Set keyboard backlight | `device.LCD.SetBacklightColor(r, g, b)` | `lcd.SetBacklightColor(r, g, b)` |
| Monitor keys | `device.KeysChanged` + `device.StartKeyboardMonitoring()` | `keyboard.KeysChanged` + `keyboard.StartMonitoring()` |
| Stop monitoring | `device.StopKeyboardMonitoring()` | `keyboard.StopMonitoring()` |
| Set M-key LEDs | `device.SetMKeyLEDs(keys)` | `keyboard.SetMKeyLEDs(keys)` |

> `new G19Device(vendorId, productId)` only changes the child `LCD` and `Keyboard` helpers. `device.OpenDevice()` still probes the built-in G19 VID/PID today, so if you need custom IDs you must open `new LCD(vendorId, productId)` and `new Keyboard(vendorId, productId)` directly.

### Key-related types

`G19Keys` is a flag enum split into three groups:

- **L-keys:** `LHome`, `LCancel`, `LMenu`, `LOk`, `LRight`, `LLeft`, `LDown`, `LUp`
- **G-keys:** `G1` through `G12`
- **M-keys:** `M1`, `M2`, `M3`, `MR`

`KeysChanged` gives you a **current full state snapshot**, not a single key-down/key-up token. That is deliberate and useful:

- `e.Keys == G19Keys.None` means no tracked G/M/L keys are currently pressed
- `e.IsKeyPressed(G19Keys.G1)` checks for one flag
- `(e.Keys & (G19Keys.G1 | G19Keys.G2)) != 0` checks a group
- `G19Helpers.GetKeyString(e.Keys)` formats combinations for logs

`device.KeysChanged` and `keyboard.KeysChanged` handlers run on background polling threads. If you touch WinForms, WPF, MAUI, Avalonia, or any other thread-affine UI from the event, marshal back to your own UI thread first.

## Code examples

### 1. Detect a device before opening it

`GetDeviceCount()` is a simple presence check for matching VID/PID devices.

```csharp
using System;
using G19USB;

int count = G19Device.GetDeviceCount();
Console.WriteLine($"Found {count} G19 device(s)");

if (count == 0)
{
    Console.WriteLine("No G19/G19s detected.");
    return;
}
```

`GetDeviceCount() > 0` means the device was found, not that it is guaranteed to be openable. A wrong driver or another process holding the device can still make `OpenDevice()` fail.

### 2. Full device lifecycle with `G19Device`

This is the normal starting point for most applications.

```csharp
using System;
using G19USB;

if (G19Device.GetDeviceCount() == 0)
{
    Console.WriteLine("No G19/G19s detected.");
    return;
}

using var device = new G19Device();

try
{
    device.OpenDevice();

    Console.WriteLine($"Device ready: {device.IsAvailable}");

    device.LCD.SetBrightness(100);
    device.LCD.SetBacklightColor(0, 96, 255);
    device.UpdateLcd(G19Helpers.CreateTestPattern());

    // KeysChanged runs on background polling tasks; marshal before touching UI controls.
    device.KeysChanged += (_, e) =>
    {
        Console.WriteLine($"[{e.Timestamp:HH:mm:ss.fff}] {G19Helpers.GetKeyString(e.Keys)}");
    };

    device.StartKeyboardMonitoring();
    device.SetMKeyLEDs(G19Keys.M1);

    Console.WriteLine("Press Enter to quit...");
    Console.ReadLine();
}
finally
{
    device.StopKeyboardMonitoring();
    device.CloseDevice();
}
```

### 3. LogitechDisplay-style rendering: draw to a bitmap, convert, upload

A very common pattern is to render into an in-memory bitmap and then convert the finished image to RGB565 before uploading it. That is effectively the same high-level flow used by larger display applications built around the G19.

```csharp
using System;
using System.Drawing;
using G19USB;

using var device = new G19Device();
device.OpenDevice();

try
{
    using var bitmap = new Bitmap(G19Constants.LcdWidth, G19Constants.LcdHeight);
    using var graphics = Graphics.FromImage(bitmap);
    using var headerFont = new Font("Segoe UI", 18, FontStyle.Bold);
    using var bodyFont = new Font("Segoe UI", 11);
    using var accentBrush = new SolidBrush(Color.DeepSkyBlue);
    using var textBrush = new SolidBrush(Color.White);

    graphics.Clear(Color.Black);
    graphics.FillRectangle(accentBrush, 0, 0, bitmap.Width, 32);
    graphics.DrawString("G19USB Demo", headerFont, Brushes.Black, 10, 4);
    graphics.DrawString($"Updated {DateTime.Now:HH:mm:ss}", bodyFont, textBrush, 10, 50);
    graphics.DrawString("Render -> ConvertBitmapToRGB565 -> UpdateLcd", bodyFont, textBrush, 10, 76);

    byte[] frame = G19Helpers.ConvertBitmapToRGB565(bitmap);
    device.UpdateLcd(frame);
}
finally
{
    device.CloseDevice();
}
```

Notes:

- `ConvertBitmapToRGB565(...)` returns the **raw 153,600-byte pixel buffer** expected by the LCD.
- If the bitmap is not exactly **320×240**, the helper resizes it for you.
- For predictable layout and text sharpness, it is still best to render at **320×240** yourself.

### 4. LCD-only workflow

If your program only needs the display and backlight, use `LCD` directly.

```csharp
using System.Drawing;
using G19USB;

using var lcd = new LCD();
lcd.OpenDevice();

try
{
    lcd.SetBrightness(75);
    lcd.SetBacklightColor(0, 96, 255);

    byte[] frame = G19Helpers.CreateSolidColor(Color.MidnightBlue);
    lcd.UpdateScreen(frame);
}
finally
{
    lcd.CloseDevice();
}
```

This is useful for:

- display-only dashboards
- smoke tests for driver setup
- utilities that never care about key input

### 5. Keyboard-only workflow

If you only care about G/M/L-key input and M-key LEDs, use `Keyboard` directly.

```csharp
using System;
using G19USB;

using var keyboard = new Keyboard();
keyboard.OpenDevice();

try
{
    // KeysChanged runs on background polling tasks; marshal before touching UI controls.
    keyboard.KeysChanged += (_, e) =>
    {
        if (e.Keys == G19Keys.None)
        {
            Console.WriteLine("All tracked keys released.");
            return;
        }

        Console.WriteLine($"Current keys: {G19Helpers.GetKeyString(e.Keys)}");

        if (e.IsKeyPressed(G19Keys.LOk))
            Console.WriteLine("LOk is pressed.");

        if ((e.Keys & (G19Keys.G1 | G19Keys.G2 | G19Keys.G3)) != 0)
            Console.WriteLine("At least one of G1/G2/G3 is pressed.");
    };

    keyboard.StartMonitoring();
    keyboard.SetMKeyLEDs(G19Keys.M2);

    Console.WriteLine("Press Enter to stop...");
    Console.ReadLine();
}
finally
{
    keyboard.StopMonitoring();
    keyboard.CloseDevice();
}
```

### 6. Handling key flags and detecting press/release edges

Because `KeysChanged` reports the current combined key state, the usual way to detect transitions is to compare the new state with the previous one. This is the same general pattern larger G19 apps use to turn the state snapshot into button-down and button-up actions.

```csharp
G19Keys previous = G19Keys.None;

device.KeysChanged += (_, e) =>
{
    G19Keys newlyPressed = e.Keys & ~previous;
    G19Keys newlyReleased = previous & ~e.Keys;

    if ((newlyPressed & G19Keys.LRight) != 0)
        Console.WriteLine("Next page");

    if ((newlyPressed & G19Keys.LLeft) != 0)
        Console.WriteLine("Previous page");

    if ((newlyPressed & G19Keys.G1) != 0)
        Console.WriteLine("G1 pressed");

    if ((newlyPressed & G19Keys.M1) != 0)
        device.SetMKeyLEDs(G19Keys.M1);

    if ((newlyPressed & G19Keys.M2) != 0)
        device.SetMKeyLEDs(G19Keys.M2);

    if ((newlyPressed & G19Keys.M3) != 0)
        device.SetMKeyLEDs(G19Keys.M3);

    if (newlyReleased != G19Keys.None)
        Console.WriteLine($"Released: {G19Helpers.GetKeyString(newlyReleased)}");

    previous = e.Keys;
};
```

That small bit of state tracking turns the event stream into something closer to a traditional UI button model.

### 7. Backlight colour and brightness control

Brightness and backlight colour live on `LCD`, even when you access them through `G19Device`.

```csharp
// Brightness is 0-100
device.LCD.SetBrightness(25);
device.LCD.SetBrightness(100);

// Backlight colour is RGB bytes (0-255)
device.LCD.SetBacklightColor(255, 0, 0);   // red
device.LCD.SetBacklightColor(0, 255, 0);   // green
device.LCD.SetBacklightColor(0, 64, 255);  // blue-ish
device.LCD.SetBacklightColor(0, 0, 0);     // off
```

Near-white requests are not sent literally. The current implementation remaps `SetBacklightColor(255, 255, 255)`-style values toward a greener mix so the backlight looks closer to white on the hardware.

Use this for simple state indication:

- green for healthy / connected
- amber for warning
- red for fault / alert
- dim brightness for idle screens

### 8. M-key LED control

You can control the illuminated M-key LEDs through the facade or the `Keyboard` instance.

```csharp
using G19USB;

// Light a single bank LED
device.SetMKeyLEDs(G19Keys.M1);

// Switch to another bank
device.SetMKeyLEDs(G19Keys.M3);

// Multiple M-key flags can be combined if you want to light more than one LED
device.SetMKeyLEDs(G19Keys.M1 | G19Keys.MR);
```

Only the M-key flags are meaningful here:

- `G19Keys.M1`
- `G19Keys.M2`
- `G19Keys.M3`
- `G19Keys.MR`

Passing G-key or L-key flags does not make sense for LED control.

## LCD frame format: what the screen actually expects

This is the part that trips people up most often.

### Raw pixel buffer requirements

The LCD panel expects a **320×240 RGB565** image.

- width: **320** pixels
- height: **240** pixels
- bytes per pixel: **2**
- raw pixel payload size: **`320 × 240 × 2 = 153600` bytes**

Those values are available as constants:

- `G19Constants.LcdWidth`
- `G19Constants.LcdHeight`
- `G19Constants.LcdDataSize`

### Important layout detail: column-major order

The device buffer is written in **column-major** order (`x` outer loop, `y` inner loop), not the row-major layout many image libraries use.

If you build the buffer manually, the pixel offset is:

```csharp
int offset = (x * G19Constants.LcdHeight + y) * 2;
```

The low byte is written first, then the high byte.

### Manual pixel example

```csharp
using G19USB;

byte[] frame = new byte[G19Constants.LcdDataSize];

int x = 10;
int y = 20;
int offset = (x * G19Constants.LcdHeight + y) * 2;

ushort red565 = 0xF800;
frame[offset] = (byte)(red565 & 0xFF);
frame[offset + 1] = (byte)(red565 >> 8);
```

In real applications, the helper methods are usually easier and less error-prone than writing RGB565 bytes by hand.

### Raw buffer vs full frame buffer

`LCD` exposes a few update paths, and they are slightly different:

| Method | What it accepts |
| --- | --- |
| `G19Device.UpdateLcd(byte[])` | Convenience wrapper around `LCD.UpdateScreen(byte[])`; normally used with a raw RGB565 pixel buffer. |
| `LCD.UpdateScreen(byte[])` | Accepts either **raw pixel data** (`153600` bytes) or a **full frame including the 512-byte header** (`154112` bytes). |
| `LCD.UpdateScreen(ReadOnlySpan<byte>)` | Accepts **raw pixel data only** (`153600` bytes). |
| `LCD.UpdateScreenAsync(ReadOnlyMemory<byte>, bool includesHeader = false)` | Accepts raw data or a full frame, depending on `includesHeader`. |

The full frame size is available as:

- `G19Constants.LcdHeaderSize` = `512`
- `G19Constants.LcdFullSize` = `154112`

### Advanced: send a full header + payload frame yourself

Most applications should pass the raw pixel buffer and let `LCD` handle the header.

If you already maintain a reusable full frame buffer, the `byte[]` overload accepts that too:

```csharp
using System;
using System.Drawing;
using G19USB;

byte[] pixels = G19Helpers.CreateSolidColor(Color.DarkGreen);
byte[] fullFrame = new byte[G19Constants.LcdFullSize];

Buffer.BlockCopy(G19Constants.LcdHeader, 0, fullFrame, 0, G19Constants.LcdHeaderSize);
Buffer.BlockCopy(pixels, 0, fullFrame, G19Constants.LcdHeaderSize, G19Constants.LcdDataSize);

using var lcd = new LCD();
lcd.OpenDevice();

try
{
    lcd.UpdateScreen(fullFrame);
}
finally
{
    lcd.CloseDevice();
}
```

## When the helpers are useful

`G19Helpers` exists to save you from dealing with the LCD format for every single frame.

### `G19Helpers.CreateTestPattern()`

Use it when:

- validating driver setup
- checking whether the LCD path works at all
- bringing up a new machine or a new keyboard

```csharp
byte[] frame = G19Helpers.CreateTestPattern();
device.UpdateLcd(frame);
```

### `G19Helpers.CreateSolidColor(Color)`

Use it when:

- clearing the display
- flashing the screen during tests
- quickly showing a state colour without any graphics pipeline

```csharp
using System.Drawing;
using G19USB;

byte[] black = G19Helpers.CreateSolidColor(Color.Black);
byte[] blue = G19Helpers.CreateSolidColor(Color.MidnightBlue);

device.UpdateLcd(black);
device.UpdateLcd(blue);
```

### `G19Helpers.ConvertBitmapToRGB565(Bitmap)`

Use it when:

- you already render to a `System.Drawing.Bitmap`
- you want text, shapes, charts, or small dashboard-style layouts
- you do not want to manually pack RGB565 pixels

```csharp
using System.Drawing;
using G19USB;

using var bitmap = new Bitmap(320, 240);
using var graphics = Graphics.FromImage(bitmap);

graphics.Clear(Color.Black);
graphics.DrawString("Hello G19", SystemFonts.DefaultFont, Brushes.White, 10, 10);

device.UpdateLcd(G19Helpers.ConvertBitmapToRGB565(bitmap));
```

### `G19Helpers.GetKeyString(G19Keys)`

Use it for logs, debugging, and quick diagnostics.

```csharp
keyboard.KeysChanged += (_, e) =>
{
    Console.WriteLine(G19Helpers.GetKeyString(e.Keys));
};
```

## Choosing between `G19Device`, `LCD`, and `Keyboard`

### Use `G19Device` when

- you want the full keyboard feature set
- you want one object to own the device lifecycle
- you want display output and key monitoring together

### Use `LCD` when

- your app is display-only
- you do not care about key input
- you want the smallest possible surface area for a rendering tool

### Use `Keyboard` when

- your app uses the G19 extra keys as input only
- you do not need the LCD at all
- you want to monitor keys or drive M-key LEDs without a display renderer

## Troubleshooting

### `GetDeviceCount()` returns 0

Check:

- the keyboard is connected and powered
- the correct device really is the G19/G19s with VID/PID `046D:C229`
- the driver install was done on the composite parent device

### `OpenDevice()` says the device was found but could not be opened

Usually one of these:

- Logitech software or another app is already using the device
- the driver was installed on the wrong node
- the driver install needs to be redone with **libusbK** on the composite parent

### The LCD shows garbage, the wrong colours, or a scrambled image

That almost always means the frame buffer is wrong:

- not `320×240`
- not **RGB565**
- not `153600` bytes of raw pixel data
- manually packed in row-major order instead of the device's column-major order

If in doubt, start with:

```csharp
device.UpdateLcd(G19Helpers.CreateTestPattern());
```

Then move on to `ConvertBitmapToRGB565(...)`.

### No key events arrive

Check:

- you called `OpenDevice()` first
- you called `StartKeyboardMonitoring()` (or `keyboard.StartMonitoring()`)
- you did not immediately close the device after subscribing

### `SetBrightness(...)` throws

Brightness must be between **0** and **100** inclusive.

## Sample project

The repository includes a minimal sample at:

- `samples\G19USB.BasicSample`

Run it with:

```powershell
dotnet run --project samples\G19USB.BasicSample
```

It demonstrates:

- device detection via `G19Device.GetDeviceCount()`
- `G19Device` lifecycle (`OpenDevice`, `CloseDevice`)
- `KeysChanged` subscription
- keyboard monitoring start/stop
- LCD update with `CreateTestPattern()`
- M-key LED control

## Attribution

Based on **libg19** by James Geboski: <https://github.com/jgeboski/libg19>

See [NOTICE](NOTICE) for full attribution details.

## License

[Apache License 2.0](LICENSE)
