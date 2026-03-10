using System;

namespace G19USB
{
    /// <summary>
    /// Complete G19 device interface combining LCD and Keyboard functionality.
    /// Based on libg19: https://github.com/jgeboski/libg19
    /// </summary>
    public class G19Device : IG19Device
    {
        private bool _disposed;
        private LibUsbDotNet.UsbDevice? _sharedUsbDevice;
        private LibUsbDotNet.IUsbDevice? _wholeUsbDevice;
        private bool _isOpen;

        /// <summary>
        /// Gets the LCD interface for display operations
        /// </summary>
        public LCD LCD { get; }

        /// <summary>
        /// Gets the Keyboard interface for key reading operations
        /// </summary>
        public Keyboard Keyboard { get; }

        /// <summary>
        /// Gets whether the device is fully available (both LCD and Keyboard)
        /// </summary>
        public bool IsAvailable => _isOpen && LCD?.IsAvailable == true && Keyboard?.IsAvailable == true;

        /// <summary>
        /// Event fired when keys are pressed or released
        /// </summary>
        public event EventHandler<G19KeyEventArgs> KeysChanged
        {
            add { Keyboard.KeysChanged += value; }
            remove { Keyboard.KeysChanged -= value; }
        }

        /// <summary>
        /// Create a new G19 device with default VID/PID
        /// </summary>
        public G19Device()
        {
            LCD = new LCD();
            Keyboard = new Keyboard();
        }

        /// <summary>
        /// Create a new G19 device with custom VID/PID
        /// </summary>
        /// <param name="vendorId">USB Vendor ID</param>
        /// <param name="productId">USB Product ID</param>
        public G19Device(int vendorId, int productId)
        {
            LCD = new LCD(vendorId, productId);
            Keyboard = new Keyboard(vendorId, productId);
        }

        /// <summary>
        /// Open both LCD and Keyboard devices with shared USB handle
        /// </summary>
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
                        "2. The WinUSB driver is not installed correctly on ALL interfaces\n" +
                        "3. You need to reinstall the WinUSB driver using Zadig on the base device");
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
        /// Start monitoring keyboard input
        /// </summary>
        public void StartKeyboardMonitoring()
        {
            Keyboard.StartMonitoring();
        }

        /// <summary>
        /// Stop monitoring keyboard input
        /// </summary>
        public void StopKeyboardMonitoring()
        {
            Keyboard.StopMonitoring();
        }

        /// <summary>
        /// Close both LCD and Keyboard devices
        /// </summary>
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
        /// Sends RGB565 pixel data to the G19 LCD display.
        /// </summary>
        public void UpdateLcd(byte[] data) => LCD.UpdateScreen(data);

        /// <summary>
        /// Sets the illuminated M-key LEDs to reflect the active macro bank.
        /// </summary>
        public void SetMKeyLEDs(G19Keys keys) => Keyboard.SetMKeyLEDs(keys);

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            CloseDevice();
            (Keyboard as IDisposable)?.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Get the count of G19 devices connected to the system
        /// </summary>
        /// <returns>Number of G19 devices found</returns>
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
