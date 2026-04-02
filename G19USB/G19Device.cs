using System;

namespace G19USB
{
    /// <summary>
    /// High-level wrapper that opens the G19 LCD and keyboard interfaces through one shared USB device handle.
    /// Based on libg19: https://github.com/jgeboski/libg19
    /// </summary>
    /// <remarks>
    /// Use this type when one object should own the device lifecycle. The <see cref="LCD"/> and <see cref="Keyboard"/>
    /// properties expose the child helpers, but once <see cref="OpenDevice"/> succeeds they share the same
    /// LibUsbDotNet handle and should normally be opened and closed through this wrapper.
    /// </remarks>
    public class G19Device : IG19Device
    {
        private bool _disposed;
        private LibUsbDotNet.UsbDevice? _sharedUsbDevice;
        private LibUsbDotNet.IUsbDevice? _wholeUsbDevice;
        private bool _isOpen;

        /// <summary>
        /// Gets the LCD helper used by this wrapper.
        /// </summary>
        /// <remarks>
        /// After <see cref="OpenDevice"/> succeeds, this instance uses the same shared USB handle as
        /// <see cref="Keyboard"/>. Calling <see cref="LCD.CloseDevice"/> or <see cref="LCD.Dispose"/> directly also
        /// tears down that shared handle.
        /// </remarks>
        public LCD LCD { get; }

        /// <summary>
        /// Gets the keyboard helper used by this wrapper.
        /// </summary>
        /// <remarks>
        /// After <see cref="OpenDevice"/> succeeds, this instance uses the same shared USB handle as
        /// <see cref="LCD"/>. Calling <see cref="Keyboard.CloseDevice"/> or <see cref="Keyboard.Dispose"/> directly
        /// also tears down that shared handle.
        /// </remarks>
        public Keyboard Keyboard { get; }

        /// <summary>
        /// Gets whether this wrapper has opened the shared device and both child helpers currently report initialized endpoints.
        /// </summary>
        /// <remarks>
        /// This flag reflects the last successful open/close sequence. It is not a live connectivity probe and may remain
        /// <see langword="true"/> until <see cref="CloseDevice"/> is called even if the hardware later disconnects.
        /// </remarks>
        public bool IsAvailable => _isOpen && LCD?.IsAvailable == true && Keyboard?.IsAvailable == true;

        /// <summary>
        /// Raised when <see cref="Keyboard"/> reports a change in the decoded special-key flags.
        /// </summary>
        /// <remarks>
        /// This event is forwarded from <see cref="Keyboard.KeysChanged"/> and is raised on the keyboard polling tasks,
        /// not on a UI thread. UI consumers must marshal back to their own dispatcher or synchronization context before
        /// touching thread-affine state. <see cref="G19KeyEventArgs.Keys"/> is a bit field from the report that
        /// triggered the event, not a dedicated single-key press or release notification.
        /// </remarks>
        public event EventHandler<G19KeyEventArgs> KeysChanged
        {
            add { Keyboard.KeysChanged += value; }
            remove { Keyboard.KeysChanged -= value; }
        }

        /// <summary>
        /// Creates a wrapper that targets the default Logitech G19 vendor and product IDs.
        /// </summary>
        public G19Device()
        {
            LCD = new LCD();
            Keyboard = new Keyboard();
        }

        /// <summary>
        /// Creates a wrapper whose child <see cref="LCD"/> and <see cref="Keyboard"/> helpers target the specified USB IDs.
        /// </summary>
        /// <param name="vendorId">USB vendor ID passed to the child helpers.</param>
        /// <param name="productId">USB product ID passed to the child helpers.</param>
        /// <remarks>
        /// The combined <see cref="OpenDevice"/> path currently opens the shared LibUsbDotNet device with
        /// <see cref="G19Constants.VendorId"/> and <see cref="G19Constants.ProductId"/>. If you need a different
        /// VID/PID today, open the <see cref="LCD"/> and <see cref="Keyboard"/> helpers directly instead of the
        /// combined wrapper.
        /// </remarks>
        public G19Device(int vendorId, int productId)
        {
            LCD = new LCD(vendorId, productId);
            Keyboard = new Keyboard(vendorId, productId);
        }

        /// <summary>
        /// Opens the shared USB device, claims both interfaces, and initializes <see cref="LCD"/> and <see cref="Keyboard"/>.
        /// </summary>
        /// <remarks>
        /// This method is idempotent and returns immediately if the wrapper is already open. The current implementation
        /// always probes the default G19 VID/PID. If the device is visible to LibUsbDotNet but cannot be opened, the
        /// thrown <see cref="Exception"/> typically indicates that another application already owns the device or that
        /// libusbK is not installed on the composite parent device for both interfaces.
        /// </remarks>
        /// <exception cref="Exception">
        /// The device could not be found or opened, or one of its interfaces or endpoints could not be initialized.
        /// </exception>
        public void OpenDevice()
        {
            if (_isOpen)
                return;

            // Open the USB device once
            var finder = new LibUsbDotNet.Main.UsbDeviceFinder(G19Constants.VendorId, G19Constants.ProductId);
            _sharedUsbDevice = LibUsbDotNet.UsbDevice.OpenUsbDevice(finder);

            if (_sharedUsbDevice == null)
            {
                // Check if device exists for better error message
                bool deviceExists = false;
                foreach (LibUsbDotNet.Main.UsbRegistry reg in LibUsbDotNet.UsbDevice.AllDevices)
                {
                    if (reg.Vid == G19Constants.VendorId && reg.Pid == G19Constants.ProductId)
                    {
                        deviceExists = true;
                        break;
                    }
                }

                if (deviceExists)
                {
                    throw new Exception("G19 device found but could not be opened. This may happen if:\n" +
                        "1. The device is already in use by another application\n" +
                        "2. The libusbK driver is not installed correctly on the composite parent device\n" +
                        "3. You need to reinstall the libusbK driver using Zadig on the base device");
                }
                else
                {
                    throw new Exception("G19 device not found. Please check USB connection and drivers.");
                }
            }

            _wholeUsbDevice = _sharedUsbDevice as LibUsbDotNet.IUsbDevice;

            if (_wholeUsbDevice != null)
            {
                // Claim both interfaces
                _wholeUsbDevice.SetConfiguration(G19Constants.UsbConfiguration);
                _wholeUsbDevice.ClaimInterface(G19Constants.UsbInterfaceLcd);
                _wholeUsbDevice.ClaimInterface(G19Constants.UsbInterfaceKeys);
            }

            // Initialize LCD with shared device
            LCD.InitializeWithSharedDevice(_sharedUsbDevice, _wholeUsbDevice);

            // Initialize Keyboard with shared device
            Keyboard.InitializeWithSharedDevice(_sharedUsbDevice, _wholeUsbDevice);

            _isOpen = true;
        }

        /// <summary>
        /// Starts the background polling tasks that read the L-key and G/M-key endpoints.
        /// </summary>
        /// <remarks>
        /// This is a thin wrapper over <see cref="Keyboard.StartMonitoring"/> and is safe to call repeatedly.
        /// <see cref="Keyboard.IsMonitoring"/> reflects the requested monitoring state, not verified polling task health.
        /// The resulting <see cref="KeysChanged"/> callbacks run on background polling threads.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The keyboard endpoints are not open.</exception>
        public void StartKeyboardMonitoring()
        {
            Keyboard.StartMonitoring();
        }

        /// <summary>
        /// Requests keyboard monitoring to stop.
        /// </summary>
        /// <remarks>
        /// This forwards to <see cref="Keyboard.StopMonitoring"/>, which aborts pending reads and waits briefly for the
        /// polling tasks to exit. Calling it when monitoring is already stopped is harmless.
        /// </remarks>
        public void StopKeyboardMonitoring()
        {
            Keyboard.StopMonitoring();
        }

        /// <summary>
        /// Stops monitoring and releases the shared USB handle used by <see cref="LCD"/> and <see cref="Keyboard"/>.
        /// </summary>
        /// <remarks>
        /// Cleanup is best effort: endpoint disposal, interface release, and device-close exceptions are suppressed so
        /// the method can continue tearing the device down. It is safe to call this method multiple times.
        /// </remarks>
        public void CloseDevice()
        {
            if (!_isOpen)
                return;
            try
            {
                try { Keyboard?.StopMonitoring(); } catch { }

                // Close endpoints first
                try { LCD?.CloseEndpoints(); } catch { }
                try { Keyboard?.CloseEndpoints(); } catch { }

                if (_sharedUsbDevice != null)
                {
                    try
                    {
                        if (_sharedUsbDevice.IsOpen)
                        {
                            if (_wholeUsbDevice != null)
                            {
                                try { _wholeUsbDevice.ReleaseInterface(G19Constants.UsbInterfaceKeys); } catch { }
                                try { _wholeUsbDevice.ReleaseInterface(G19Constants.UsbInterfaceLcd); } catch { }
                            }
                            try { _sharedUsbDevice.Close(); } catch { }
                        }
                    }
                    finally
                    {
                        try { (_sharedUsbDevice as IDisposable)?.Dispose(); } catch { }
                        _sharedUsbDevice = null;
                        try { LibUsbDotNet.UsbDevice.Exit(); } catch { }
                    }
                }
            }
            catch { }
            finally
            {
                _isOpen = false;
            }
        }

        /// <summary>
        /// Sends one LCD frame by forwarding to <see cref="LCD.UpdateScreen(byte[])"/>.
        /// </summary>
        /// <param name="data">
        /// Either <see cref="G19Constants.LcdDataSize"/> bytes of raw RGB565 pixel payload, such as the output of
        /// <see cref="G19Helpers.ConvertBitmapToRGB565(System.Drawing.Bitmap)"/>, or
        /// <see cref="G19Constants.LcdFullSize"/> bytes containing <see cref="G19Constants.LcdHeader"/> followed by
        /// that payload.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="data"/> does not have a supported frame size.</exception>
        /// <exception cref="InvalidOperationException">The LCD endpoint is not open.</exception>
        public void UpdateLcd(byte[] data) => LCD.UpdateScreen(data);

        /// <summary>
        /// Sets the illuminated M-key LEDs.
        /// </summary>
        /// <param name="keys">
        /// Bitwise combination of <see cref="G19Keys.M1"/>, <see cref="G19Keys.M2"/>, <see cref="G19Keys.M3"/>, and
        /// <see cref="G19Keys.MR"/>. Other flags are ignored.
        /// </param>
        /// <exception cref="InvalidOperationException">The keyboard endpoints are not open.</exception>
        public void SetMKeyLEDs(G19Keys keys) => Keyboard.SetMKeyLEDs(keys);

        /// <summary>
        /// Releases the shared device connection held by this wrapper and disposes both child helpers.
        /// </summary>
        /// <remarks>
        /// Disposing <see cref="G19Device"/> performs full cleanup for the shared USB handle, the keyboard polling tasks,
        /// and the LCD write worker.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                CloseDevice();
            }
            finally
            {
                try
                {
                    LCD.Dispose();
                }
                finally
                {
                    try
                    {
                        Keyboard.Dispose();
                    }
                    finally
                    {
                        GC.SuppressFinalize(this);
                    }
                }
            }
        }

        /// <summary>
        /// Counts USB devices that match the default Logitech G19 vendor and product IDs.
        /// </summary>
        /// <returns>
        /// The number of registry entries in <see cref="LibUsbDotNet.UsbDevice.AllDevices"/> that match
        /// <see cref="G19Constants.VendorId"/> and <see cref="G19Constants.ProductId"/>.
        /// </returns>
        /// <remarks>
        /// This method does not verify that each device can be opened successfully with the currently installed driver.
        /// </remarks>
        public static int GetDeviceCount()
        {
            // Use LibUsbDotNet to count devices matching our VID/PID
            var finder = new LibUsbDotNet.Main.UsbDeviceFinder(G19Constants.VendorId, G19Constants.ProductId);
            int count = 0;

            foreach (LibUsbDotNet.Main.UsbRegistry device in LibUsbDotNet.UsbDevice.AllDevices)
            {
                if (finder.Check(device))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
