using System;

namespace G19USB
{
    /// <summary>
    /// Represents a combined G19 LCD and special-key session.
    /// </summary>
    /// <remarks>
    /// Call <see cref="OpenDevice"/> before starting monitoring, updating the LCD, or setting M-key LEDs.
    /// </remarks>
    public interface IG19Device : IDisposable
    {
        /// <summary>
        /// Raised when the implementation detects a change in the special-key flags reported by one of its key endpoints.
        /// </summary>
        /// <remarks>
        /// <see cref="G19KeyEventArgs.Keys"/> is a bit field, not a dedicated single-key press or release notification.
        /// Implementations raise this event on background polling threads, so UI consumers must marshal to their own
        /// thread before updating thread-affine state.
        /// </remarks>
        event EventHandler<G19KeyEventArgs> KeysChanged;

        /// <summary>
        /// Gets whether the implementation currently considers its required USB endpoints open.
        /// </summary>
        /// <remarks>
        /// Implementations may not actively probe connectivity between calls, so this value usually reflects the last
        /// successful open or close operation rather than live hardware health.
        /// </remarks>
        bool IsAvailable { get; }

        /// <summary>
        /// Opens the USB connection to the device and initializes the LCD and keyboard interfaces.
        /// </summary>
        /// <remarks>
        /// Implementations typically throw if the device is not present, is already owned by another application, or the
        /// required libusbK driver is not installed for the relevant interfaces or composite parent device.
        /// </remarks>
        void OpenDevice();

        /// <summary>
        /// Begins polling the device-specific G-key, M-key, and L-key endpoints and raising <see cref="KeysChanged"/> events.
        /// </summary>
        /// <remarks>
        /// Implementations typically use background tasks and require <see cref="OpenDevice"/> to have succeeded first.
        /// The resulting <see cref="KeysChanged"/> callbacks run on those polling threads.
        /// </remarks>
        void StartKeyboardMonitoring();

        /// <summary>
        /// Requests key polling to stop.
        /// </summary>
        /// <remarks>
        /// Implementations may use best-effort cancellation and briefly wait for background polling to drain before returning.
        /// </remarks>
        void StopKeyboardMonitoring();

        /// <summary>
        /// Closes the USB connection and releases all claimed endpoints and interfaces.
        /// </summary>
        void CloseDevice();

        /// <summary>
        /// Sends one LCD frame to the device.
        /// </summary>
        /// <param name="data">
        /// Either <see cref="G19Constants.LcdDataSize"/> bytes of raw RGB565 pixel payload, such as the output of
        /// <see cref="G19Helpers.ConvertBitmapToRGB565(System.Drawing.Bitmap)"/>, or
        /// <see cref="G19Constants.LcdFullSize"/> bytes containing <see cref="G19Constants.LcdHeader"/> followed by
        /// that payload.
        /// </param>
        void UpdateLcd(byte[] data);

        /// <summary>
        /// Illuminates the M-key LEDs that correspond to the specified <see cref="G19Keys"/> flags.
        /// </summary>
        /// <param name="keys">
        /// A bitwise combination of <see cref="G19Keys.M1"/>, <see cref="G19Keys.M2"/>, <see cref="G19Keys.M3"/>, and/or
        /// <see cref="G19Keys.MR"/>. Implementations may ignore all other flags.
        /// </param>
        void SetMKeyLEDs(G19Keys keys);
    }
}
