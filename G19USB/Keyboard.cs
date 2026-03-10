using LibUsbDotNet;
using LibUsbDotNet.Main;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace G19USB
{
    /// <summary>
    /// G19 Keyboard interface for reading G-keys, M-keys, and L-keys (LCD navigation).
    /// Based on libg19: https://github.com/jgeboski/libg19
    /// </summary>
    public class Keyboard : IDisposable
    {
        private UsbDevice? _usbDevice;
        private IUsbDevice? _wholeUsbDevice;
        private UsbDeviceFinder _usbFinder;

        private UsbEndpointReader? _lKeyReader;  // For L-keys (LCD navigation) - endpoint 0x81
        private UsbEndpointReader? _gKeyReader;  // For G-keys and M-keys - endpoint 0x83

        private readonly byte[] _lKeyBuffer = new byte[G19Constants.LKeyBufferSize];
        private readonly byte[] _gKeyBuffer = new byte[G19Constants.GKeyBufferSize];

        // No G-keys detected and no M-key activity - likely key up, clear all G-keys
        private const G19Keys allGKeys = G19Keys.G1 | G19Keys.G2 | G19Keys.G3 | G19Keys.G4 | G19Keys.G5
            | G19Keys.G6 | G19Keys.G7 | G19Keys.G8 | G19Keys.G9 | G19Keys.G10 | G19Keys.G11 | G19Keys.G12;
        private const G19Keys allMKeys = G19Keys.M1 | G19Keys.M2 | G19Keys.M3 | G19Keys.MR;

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _lKeyPollingTask;
        private Task? _gKeyPollingTask;

        private G19Keys _lastKeyState = G19Keys.None;
        private bool _disposed;

        // Note: Keys are mapped to specific bits in the G19Keys enum,
        // but arrive in separate buffers from different endpoints

        /// <summary>
        /// Gets whether the keyboard device is available and connected
        /// </summary>
        public bool IsAvailable { get; private set; }

        /// <summary>
        /// Gets whether key monitoring is currently active
        /// </summary>
        public bool IsMonitoring { get; private set; }

        /// <summary>
        /// Event fired when keys are pressed or released
        /// </summary>
        public event EventHandler<G19KeyEventArgs>? KeysChanged;

        /// <summary>
        /// Event fired for each raw G-key endpoint buffer read (for diagnostics)
        /// </summary>
        public event EventHandler<G19GKeyBufferEventArgs>? GKeyBufferReceived;

        /// <summary>
        /// Create new instance with the default G19 VID/PID
        /// </summary>
        public Keyboard() : this(G19Constants.VendorId, G19Constants.ProductId) { }

        /// <summary>
        /// Create a new instance of the Keyboard class
        /// </summary>
        /// <param name="vendorId">Device vendor ID</param>
        /// <param name="productId">Device product ID</param>
        public Keyboard(int vendorId, int productId)
        {
            _usbFinder = new UsbDeviceFinder(vendorId, productId);
        }

        /// <summary>
        /// Open connection to the device
        /// </summary>
        public void OpenDevice()
        {
            if (IsAvailable)
                return;

            _usbDevice = UsbDevice.OpenUsbDevice(_usbFinder);

            if (_usbDevice == null)
            {
                // Try to find the device in AllDevices for better error reporting
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
                        "3. You need to reinstall the WinUSB driver using Zadig on the base device (not just interface 0)\n" +
                        "4. Try selecting 'Options > List All Devices' in Zadig and install WinUSB on 'G19s Gaming Keyboard' (without interface suffix)");
                }
                else
                {
                    throw new Exception("G19 Keyboard device not found. Please check USB connection and drivers.");
                }
            }

            _wholeUsbDevice = _usbDevice as IUsbDevice;

            if (_wholeUsbDevice != null)
            {
                // Set configuration and claim the keyboard interface (interface #1)
                _wholeUsbDevice.SetConfiguration(G19Constants.UsbConfiguration);
                _wholeUsbDevice.ClaimInterface(G19Constants.UsbInterfaceKeys);
            }

            // Open interrupt endpoints for reading keys
            // L-Keys: endpoint 0x81 (interrupt IN)
            _lKeyReader = _usbDevice.OpenEndpointReader((ReadEndpointID)G19Constants.EndpointLKeysIn);

            // G-Keys and M-Keys: endpoint 0x83 (interrupt IN)
            _gKeyReader = _usbDevice.OpenEndpointReader((ReadEndpointID)G19Constants.EndpointGKeysIn);

            IsAvailable = _lKeyReader != null && _gKeyReader != null;

            if (!IsAvailable)
            {
                CloseDevice();
                throw new Exception("Failed to open keyboard input endpoints.");
            }
        }

        /// <summary>
        /// Initialize Keyboard with a shared USB device (for use with G19Device)
        /// </summary>
        internal void InitializeWithSharedDevice(UsbDevice sharedDevice, IUsbDevice? wholeDevice)
        {
            if (IsAvailable)
                return;

            _usbDevice = sharedDevice;
            _wholeUsbDevice = wholeDevice;

            // Open interrupt endpoints for reading keys
            // L-Keys: endpoint 0x81 (interrupt IN)
            _lKeyReader = _usbDevice.OpenEndpointReader((ReadEndpointID)G19Constants.EndpointLKeysIn);

            // G-Keys and M-Keys: endpoint 0x83 (interrupt IN)
            _gKeyReader = _usbDevice.OpenEndpointReader((ReadEndpointID)G19Constants.EndpointGKeysIn);

            IsAvailable = _lKeyReader != null && _gKeyReader != null;

            if (!IsAvailable)
            {
                throw new Exception("Failed to open keyboard input endpoints.");
            }
        }

        /// <summary>
        /// Close only the endpoints, not the device (for shared device mode)
        /// </summary>
        internal void CloseEndpoints()
        {
            StopMonitoring();

            _lKeyReader?.Dispose();
            _lKeyReader = null;

            _gKeyReader?.Dispose();
            _gKeyReader = null;

            IsAvailable = false;
        }

        /// <summary>
        /// Start monitoring keyboard input
        /// </summary>
        public void StartMonitoring()
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException("Device must be opened before starting monitoring.");
            }

            if (IsMonitoring)
                return;

            _cancellationTokenSource = new CancellationTokenSource();

            // Start polling tasks for both L-keys and G-keys
            _lKeyPollingTask = Task.Factory.StartNew(() => PollLKeys(_cancellationTokenSource.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _gKeyPollingTask = Task.Factory.StartNew(() => PollGKeys(_cancellationTokenSource.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            IsMonitoring = true;
        }

        /// <summary>
        /// Stop monitoring keyboard input
        /// </summary>
        public void StopMonitoring()
        {
            if (!IsMonitoring)
                return;

            // Signal cancellation first
            try { _cancellationTokenSource?.Cancel(); } catch { }

            // Abort any pending blocking reads to help tasks exit promptly when the device disconnects
            try { _lKeyReader?.Abort(); } catch { }
            try { _gKeyReader?.Abort(); } catch { }

            // Wait briefly for polling tasks to finish. Ignore errors/timeouts and continue cleanup.
            try
            {
                Task.WaitAll(new[] { _lKeyPollingTask!, _gKeyPollingTask! }, TimeSpan.FromSeconds(2));
            }
            catch { }

            // If tasks are still running, attempt to leave them and continue cleanup; they will exit when reads abort.
            try { _cancellationTokenSource?.Dispose(); } catch { }
            _cancellationTokenSource = null;

            IsMonitoring = false;
        }

        /// <summary>
        /// Poll L-keys (LCD navigation keys) continuously
        /// </summary>
        private void PollLKeys(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var errorCode = ErrorCode.IoTimedOut;
                    try
                    {
                        errorCode = _lKeyReader!.Read(_lKeyBuffer, G19Constants.KeyReadTimeout, out int bytesRead);

                        if (errorCode == ErrorCode.Success && bytesRead >= G19Constants.LKeyBufferSize)
                        {
                            uint keyData = _lKeyBuffer[0];
                            ProcessKeyData((G19Keys)keyData);
                        }
                        else if (errorCode == ErrorCode.IoCancelled)
                        {
                            // Read was cancelled, likely due to StopMonitoring; exit loop
                            break;
                        }
                        else if (errorCode != ErrorCode.IoTimedOut)
                        {
                            // Non-timeout errors may indicate device removal; log and break to exit polling
                            System.Diagnostics.Debug.WriteLine($"L-Key read error: {errorCode}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        // An exception during Read likely indicates device disconnect; log and exit loop
                        System.Diagnostics.Debug.WriteLine($"Exception reading L-keys: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception in PollLKeys: {ex.Message}");
            }
        }

        /// <summary>
        /// Poll G-keys and M-keys continuously
        /// </summary>
        private void PollGKeys(CancellationToken cancellationToken)
        {
            try
            {
                byte[] combined = new byte[32 * G19Constants.GKeyBufferSize];
                int combinedLength = 0;

                byte[] nextBuf = new byte[G19Constants.GKeyBufferSize];

                while (!cancellationToken.IsCancellationRequested)
                {
                    combinedLength = 0;

                    var errorCode = ErrorCode.IoTimedOut;
                    try
                    {
                        errorCode = _gKeyReader!.Read(_gKeyBuffer, G19Constants.KeyReadTimeout, out int bytesRead);

                        if (errorCode == ErrorCode.Success && bytesRead >= G19Constants.GKeyBufferSize)
                        {
                            // If we have keys pressed, we must process even "idle" reports to detect key releases.
                            if (!IsIdleGKeyReport(_gKeyBuffer, bytesRead) || _lastKeyState != G19Keys.None)
                            {
                                Buffer.BlockCopy(_gKeyBuffer, 0, combined, combinedLength, bytesRead);
                                combinedLength += bytesRead;
                            }

                            // Read subsequent chunks until termination or timeout
                            while (true)
                            {
                                var rc = _gKeyReader!.Read(nextBuf, G19Constants.KeyReadTimeout, out int rcBytes);
                                if (rc != ErrorCode.Success)
                                {
                                    if (rc == ErrorCode.IoTimedOut)
                                        break; // end of packet
                                    break;
                                }

                                if (rcBytes < G19Constants.GKeyBufferSize)
                                {
                                    // Partial read - pad with zeros
                                    for (int i = rcBytes; i < G19Constants.GKeyBufferSize; i++)
                                        nextBuf[i] = 0x00;
                                    if (!IsIdleGKeyReport(nextBuf, rcBytes) || _lastKeyState != G19Keys.None)
                                    {
                                        Buffer.BlockCopy(nextBuf, 0, combined, combinedLength, G19Constants.GKeyBufferSize);
                                        combinedLength += G19Constants.GKeyBufferSize;
                                    }
                                    break;
                                }

                                if (IsIdleGKeyReport(nextBuf, rcBytes))
                                    break;

                                Buffer.BlockCopy(nextBuf, 0, combined, combinedLength, rcBytes);
                                combinedLength += rcBytes;

                                // Safety cap to avoid runaway reads
                                if (combinedLength >= combined.Length)
                                    break;
                            }

                            if (combinedLength == 0)
                                continue;

#if DEBUG
                            // Build contiguous packet for diagnostics
                            OnGKeyBufferReceived(new G19GKeyBufferEventArgs(G19Keys.None, combined, combinedLength));
#endif

                            // Decode the combined byte stream
                            // G-key pattern: [03] 02 <keycode_low> <keycode_high> [40 03 <id...> 00]
                            // M-key down: 02 02 00 <mcode> where mcode is 10/20/40/80
                            //         or: 03 02 00 <mcode> 40 02
                            // M-key up: 02 02
                            // Note: 03 prefix and 40-03-ids suffix are optional markers
                            int len = combinedLength;
                            int idx = 0;
                            G19Keys detectedGKeys = G19Keys.None;
                            G19Keys detectedMKey = G19Keys.None;
                            bool mKeyUp = true;

                            // ignore packets of length 2
                            if (len == 2 && combined[0] != 0x03)
                                continue;

#if DEBUG
                            string hex = BitConverter.ToString(combined, idx, combinedLength - idx);
                            System.Diagnostics.Trace.TraceInformation($"G-key packet at {idx}: {hex}");
#endif

                            // if length > 2 and starts with 00 or 03, skip it
                            if (len > 2 && (combined[0] == 0x00 || combined[0] == 0x03))
                            {
                                idx++;
                            }

                            while (idx < len)
                            {
#if DEBUG
                                hex = BitConverter.ToString(combined, idx, combinedLength - idx);
                                System.Diagnostics.Trace.TraceInformation($"G-key packet at {idx}: {hex}");
#endif

                                if (idx + 3 < len && combined[idx] == 0x02 &&
                                    ((combined[idx + 1] == 0x00
                                        && (combined[idx + 3] == 0x00 || combined[idx + 3] == 0x40))
                                    || (combined[idx + 1] == 0x02 && combined[idx + 2] == 0x00
                                    && combined[idx + 3] > 0x00 && (idx + 5 >= len || (
                                        combined[idx + 3] != 0x40 && combined[idx + 4] != 0x03)))))
                                {
                                    // M-key down
                                    int mIdx = combined[idx + 1] == 0x00 ? idx + 2 : idx + 3;
                                    (detectedMKey, mKeyUp) = RecognizeMKey(combined[mIdx]);
                                    if (detectedMKey != G19Keys.None)
                                    {
                                        idx += 4;
                                        continue;
                                    }
                                }
                                if (idx + 2 < len && combined[idx + 1] == 0x00
                                    && combined[idx + 2] > 0x00 && (idx + 4 >= len || (
                                        combined[idx + 2] != 0x40 && combined[idx + 3] != 0x03)))
                                {
                                    // M-key down
                                    (detectedMKey, mKeyUp) = RecognizeMKey(combined[idx + 2]);
                                    if (detectedMKey != G19Keys.None)
                                    {
                                        idx += 3;
                                        continue;
                                    }
                                }

                                if (idx + 1 < len && combined[idx] == 0x02 && combined[idx + 1] == 0x02
                                    && !(idx + 4 < len && combined[idx + 3] == 0x40 && combined[idx + 4] == 0x03))
                                {
                                    idx++;
                                    continue;
                                }

                                // 03-XX is a key down marker; skip it
                                if (idx + 1 < len && combined[idx] == 0x03)
                                {
                                    // if (combined[idx + 1] < 0x3A)
                                    //     idx += 2;
                                    // else
                                    // {
                                    //     var gKey = (G19Keys)(1u << (combined[idx + 1] - 0x3A + 20));
                                    //     detectedGKeys |= gKey;
                                    // }
                                    idx += 2;
                                }

                                // Look for 02 <keycode_low> <keycode_high> pattern (G-keys)
                                if (idx + 5 < len && combined[idx] == 0x02
                                    && combined[idx + 3] == 0x40 && combined[idx + 4] == 0x03)
                                {
                                    byte keycodeLow = combined[idx + 1];
                                    byte keycodeHigh = combined[idx + 2];
                                    ushort keycode = (ushort)(keycodeLow | (keycodeHigh << 8));

                                    // If keycode is non-zero, it's a G-key press
                                    if (keycode != 0)
                                    {
                                        detectedGKeys = DecodeGKeysFromKeycode(keycode);
                                        idx += 5;
                                        while (idx < len && combined[idx] != 0x00)
                                            idx++;
                                        idx++;
                                        continue;
                                    }
                                }
                                if (idx + 4 < len && combined[idx] == 0x02
                                    && combined[idx + 2] == 0x40 && combined[idx + 3] == 0x03)
                                {
                                    // no key code for some reason, just decode whatever comes
                                    // after 40-03
                                    idx += 4;
                                    while (idx < len && combined[idx] != 0x00)
                                    {
                                        var gKey = (G19Keys)(1u << (combined[idx] - 0x3A + 20));
                                        detectedGKeys |= gKey;
                                        idx++;
                                    }
                                    idx++;
                                    continue;
                                }

                                idx += (len - idx) % 2 == 0 ? 2 : 1;
                            }

                            // Compute combined final state for M-keys and G-keys and apply once
                            var finalState = G19Keys.None;

                            if (detectedMKey != G19Keys.None)
                            {
                                finalState = (finalState & ~allMKeys) | detectedMKey;
                            }
                            else if (mKeyUp)
                            {
                                finalState &= ~allMKeys;
                            }

                            if (detectedGKeys != G19Keys.None)
                            {
                                finalState |= detectedGKeys;
                            }
                            else if (detectedMKey == G19Keys.None && !mKeyUp && (finalState & allGKeys) != G19Keys.None)
                            {
                                finalState &= ~allGKeys;
                            }

                            ProcessKeyData(finalState);
                        }
                        else if (errorCode == ErrorCode.IoCancelled)
                        {
                            // Read was cancelled, likely due to StopMonitoring; exit loop
                            break;
                        }
                        else if (errorCode != ErrorCode.IoTimedOut)
                        {
                            System.Diagnostics.Trace.TraceError($"G-Key read error: {errorCode}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceError($"Exception reading G-keys: {ex.Message}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Unhandled exception in PollGKeys: {ex.Message}");
            }
        }

        private G19Keys DecodeGKeysFromKeycode(ushort keycode)
        {
            G19Keys detectedGKeys = G19Keys.None;
            // Decode keycode bits to G-key enum
            // G1=0x0001, G2=0x0002, G3=0x0004, G4=0x0008, G5=0x0010, G6=0x0020, G7=0x0040, G8=0x0080
            // G9=0x0100, G10=0x0200, G11=0x0400, G12=0x0800
            for (int bit = 0; bit < 12; bit++)
            {
                if ((keycode & (1 << bit)) != 0)
                {
                    // Bit 0 = G1 (bit 8 in G19Keys), bit 1 = G2 (bit 9), etc.
                    detectedGKeys |= (G19Keys)(1u << (8 + bit));
                }
            }
            return detectedGKeys;
        }

        private (G19Keys detectedMKey, bool mKeyUp) RecognizeMKey(byte mCode)
        {
            switch (mCode)
            {
                case 0x10: return (G19Keys.M1, false);
                case 0x20: return (G19Keys.M2, false);
                case 0x40: return (G19Keys.M3, false);
                case 0x80: return (G19Keys.MR, false);
            }
            return (G19Keys.None, true);
        }

        /// <summary>
        /// Process key data and raise events if keys have changed
        /// </summary>
        private void ProcessKeyData(G19Keys currentKeys)
        {
            // Only fire event if keys have changed
            if (currentKeys != _lastKeyState)
            {
                _lastKeyState = currentKeys;
                OnKeysChanged(currentKeys);
            }
        }

        private static bool IsIdleGKeyReport(byte[] buffer, int bytesRead)
        {
            if (bytesRead != G19Constants.GKeyBufferSize)
                return false;

            return (buffer[0] == 0x00 && buffer[1] == 0x00) ||
                (buffer[0] == 0x03 && buffer[1] == 0x00);
        }

        private static byte[] CopyChunk(byte[] buffer, int bytesRead)
        {
            var chunk = new byte[G19Constants.GKeyBufferSize];
            Array.Copy(buffer, 0, chunk, 0, Math.Min(bytesRead, G19Constants.GKeyBufferSize));
            return chunk;
        }

        private byte[] ReadNextChunk()
        {
            var chunk = new byte[G19Constants.GKeyBufferSize];
            var errorCode = _gKeyReader!.Read(chunk, G19Constants.KeyReadTimeout, out int bytesRead);
            if (errorCode != ErrorCode.Success && errorCode != ErrorCode.IoTimedOut)
            {
                System.Diagnostics.Trace.TraceError($"G-Key follow-up read error: {errorCode}");
            }

            if (bytesRead < G19Constants.GKeyBufferSize)
            {
                for (int i = bytesRead; i < G19Constants.GKeyBufferSize; i++)
                {
                    chunk[i] = 0;
                }
            }

            return chunk;
        }

        /// <summary>
        /// Raise the KeysChanged event
        /// </summary>
        protected virtual void OnKeysChanged(G19Keys keys)
        {
            KeysChanged?.Invoke(this, new G19KeyEventArgs(keys));
        }

        /// <summary>
        /// Raise the GKeyBufferReceived event
        /// </summary>
        protected virtual void OnGKeyBufferReceived(G19GKeyBufferEventArgs args)
        {
            GKeyBufferReceived?.Invoke(this, args);
        }

        /// <summary>
        /// Set the state of the M-Key LEDs
        /// </summary>
        /// <param name="keys">M-key flags to illuminate</param>
        public void SetMKeyLEDs(G19Keys keys)
        {
            if (!IsAvailable)
            {
                throw new InvalidOperationException("Device must be opened before setting M-key LEDs.");
            }

            byte[] setupPacket = new byte[8];
            byte[] data = new byte[2];

            data[0] = 0x10;
            data[1] = 0x00;

            // Set LED bits based on which M-keys are specified
            if ((keys & G19Keys.M1) != 0)
                data[1] |= 0x10 << 3;
            if ((keys & G19Keys.M2) != 0)
                data[1] |= 0x10 << 2;
            if ((keys & G19Keys.M3) != 0)
                data[1] |= 0x10 << 1;
            if ((keys & G19Keys.MR) != 0)
                data[1] |= 0x10 << 0;

            var packet = new UsbSetupPacket(
                G19Constants.RequestTypeMKeys,
                G19Constants.RequestMKeys,
                G19Constants.ValueMKeys,
                G19Constants.IndexMKeys,
                (short)data.Length);

            int bytesTransferred;
            _usbDevice!.ControlTransfer(ref packet, data, data.Length, out bytesTransferred);
        }

        /// <summary>
        /// Close the connection to the device
        /// </summary>
        public void CloseDevice()
        {
            StopMonitoring();

            _lKeyReader?.Dispose();
            _lKeyReader = null;

            _gKeyReader?.Dispose();
            _gKeyReader = null;

            if (_usbDevice != null)
            {
                if (_usbDevice.IsOpen)
                {
                    if (_wholeUsbDevice != null)
                    {
                        _wholeUsbDevice.ReleaseInterface(G19Constants.UsbInterfaceKeys);
                    }
                    _usbDevice.Close();
                }
                // UsbDevice implements IDisposable - calling both Close and Dispose
                (_usbDevice as IDisposable)?.Dispose();
                _usbDevice = null;
                UsbDevice.Exit();
            }

            IsAvailable = false;
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            CloseDevice();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
