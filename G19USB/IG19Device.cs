using System;

namespace G19USB
{
    /// <summary>
    /// Represents a Logitech G19/G19s keyboard device, providing LCD display, key monitoring,
    /// M-key LED control, and device lifecycle management.
    /// </summary>
    public interface IG19Device : IDisposable
    {
        /// <summary>
        /// Raised when the state of any G-key, M-key, or L-key changes.
        /// </summary>
        event EventHandler<G19KeyEventArgs> KeysChanged;

        /// <summary>
        /// Gets whether the device is open and fully operational.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Opens the USB connection to the device and initialises the LCD and keyboard interfaces.
        /// </summary>
        void OpenDevice();

        /// <summary>
        /// Begins polling G-keys, M-keys, and L-keys and raising <see cref="KeysChanged"/> events.
        /// </summary>
        void StartKeyboardMonitoring();

        /// <summary>
        /// Stops key polling. No further <see cref="KeysChanged"/> events will be raised.
        /// </summary>
        void StopKeyboardMonitoring();

        /// <summary>
        /// Closes the USB connection and releases all endpoints.
        /// </summary>
        void CloseDevice();

        /// <summary>
        /// Sends a 153,600-byte RGB565 pixel buffer to the G19 LCD (320×240, column-major).
        /// </summary>
        /// <param name="data">Raw RGB565 pixel data (exactly <c>320 × 240 × 2 = 153,600</c> bytes).</param>
        void UpdateLcd(byte[] data);

        /// <summary>
        /// Illuminates the M-key LEDs that correspond to the specified <see cref="G19Keys"/> flags.
        /// </summary>
        /// <param name="keys">A bitwise combination of <see cref="G19Keys.M1"/>, <see cref="G19Keys.M2"/>,
        /// <see cref="G19Keys.M3"/>, and/or <see cref="G19Keys.MR"/>.</param>
        void SetMKeyLEDs(G19Keys keys);
    }
}
