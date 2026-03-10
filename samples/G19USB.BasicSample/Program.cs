using System;
using G19USB;

Console.WriteLine("G19USB Basic Sample");
Console.WriteLine("===================\n");

int deviceCount = G19Device.GetDeviceCount();
Console.WriteLine($"Found {deviceCount} G19 device(s)\n");

if (deviceCount == 0)
{
    Console.WriteLine("No G19 device found. Please check:");
    Console.WriteLine("  1. Device is connected via USB");
    Console.WriteLine("  2. libusbK driver is installed (use Zadig — see README)");
    return;
}

using var device = new G19Device();

try
{
    Console.WriteLine("Opening device...");
    device.OpenDevice();
    Console.WriteLine("Device opened successfully!\n");

    // Subscribe to key events
    device.KeysChanged += (_, e) =>
    {
        if (e.Keys != G19Keys.None)
            Console.WriteLine($"[{e.Timestamp:HH:mm:ss.fff}] Keys: {G19Helpers.GetKeyString(e.Keys)}");
    };

    device.StartKeyboardMonitoring();
    Console.WriteLine("Keyboard monitoring started.\n");

    // Send a test pattern to the LCD
    Console.WriteLine("Displaying test pattern on LCD...");
    byte[] frame = G19Helpers.CreateTestPattern();
    device.UpdateLcd(frame);
    Console.WriteLine("Test pattern displayed.\n");

    // Light up M1 LED
    device.SetMKeyLEDs(G19Keys.M1);
    Console.WriteLine("M1 LED lit.\n");

    Console.WriteLine("Press any console key to exit...");
    Console.ReadKey(true);

    device.StopKeyboardMonitoring();
    Console.WriteLine("Done.");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
}
finally
{
    device.CloseDevice();
}
