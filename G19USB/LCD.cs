using LibUsbDotNet;
using LibUsbDotNet.Main;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace G19USB
{
    /// <summary>
    /// G19 LCD Display interface for updating the 320x240 color LCD screen.
    /// Based on libg19: https://github.com/jgeboski/libg19
    /// </summary>
    public class LCD : IDisposable
    {
        private UsbDevice? _usbDevice;
        private IUsbDevice? _wholeUsbDevice;
        private UsbDeviceFinder _usbFinder;

        private UsbEndpointWriter? _writer;
        // Synchronize connect/disconnect/write operations
        private readonly object _deviceLock = new object();
        // Reconnect attempts when writes fail (useful after sleep/resume)
        private const int DefaultReconnectAttempts = 3;
        private const int DefaultReconnectDelayMs = 500;
        private readonly byte[] _lcdBuffer = new byte[G19Constants.LcdFullSize];
        private readonly GCHandle _lcdBufferHandle;
        private int _lcdBufferInFlight;
        private readonly ArrayPool<byte> _framePool = ArrayPool<byte>.Shared;
        private readonly BlockingCollection<PendingWrite> _writeQueue;
        private readonly CancellationTokenSource _writeCts;
        private readonly Task _writeWorker;
        private bool _disposed;

        /// <summary>
        /// Gets whether the LCD device is available and connected
        /// </summary>
        public bool IsAvailable { get; private set; }

        /// <summary>
        /// Create new instance with the default G19 VID/PID
        /// </summary>
        public LCD() : this(G19Constants.VendorId, G19Constants.ProductId) { }

        /// <summary>
        /// Create a new instance of the LCD class
        /// </summary>
        /// <param name="vendorId">Device vendor ID</param>
        /// <param name="productId">Device product ID</param>
        public LCD(int vendorId, int productId)
        {
            _usbFinder = new UsbDeviceFinder(vendorId, productId);
            _lcdBufferHandle = GCHandle.Alloc(_lcdBuffer, GCHandleType.Pinned);
            _writeQueue = new BlockingCollection<PendingWrite>(new ConcurrentQueue<PendingWrite>());
            _writeCts = new CancellationTokenSource();
            _writeWorker = Task.Factory.StartNew(
                () => ProcessWriteQueue(_writeCts.Token), 
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // Pre-initialize buffer with header
            Array.Copy(G19Constants.LcdHeader, _lcdBuffer, G19Constants.LcdHeaderSize);
        }

        /// <summary>
        /// Open connection to the device
        /// </summary>
        public void OpenDevice()
        {
            ThrowIfDisposed();

            if (IsAvailable)
                return;

            _usbDevice = UsbDevice.OpenUsbDevice(_usbFinder);

            if (_usbDevice == null)
            {
                // Try to find the device in AllDevices for better error reporting
                bool deviceExists = false;
                foreach (LibUsbDotNet.Main.UsbRegistry reg in UsbDevice.AllDevices)
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
                        "3. You need to reinstall the WinUSB driver using Zadig on the base device (not just interface 0)");
                }
                else
                {
                    throw new Exception("G19 LCD device not found. Please check USB connection and drivers.");
                }
            }

            _wholeUsbDevice = _usbDevice as IUsbDevice;

            if (_wholeUsbDevice != null)
            {
                // Set configuration and claim the LCD interface (interface #0)
                _wholeUsbDevice.SetConfiguration(G19Constants.UsbConfiguration);
                _wholeUsbDevice.ClaimInterface(G19Constants.UsbInterfaceLcd);
            }

            // Open bulk endpoint for LCD output (endpoint 0x02)
            _writer = _usbDevice.OpenEndpointWriter((WriteEndpointID)G19Constants.EndpointLcdOut);

            IsAvailable = _writer != null;

            if (!IsAvailable)
            {
                CloseDevice();
                throw new Exception("Failed to open LCD output endpoint.");
            }
        }

        /// <summary>
        /// Initialize LCD with a shared USB device (for use with G19Device)
        /// </summary>
        internal void InitializeWithSharedDevice(UsbDevice sharedDevice, IUsbDevice? wholeDevice)
        {
            ThrowIfDisposed();

            if (IsAvailable)
                return;

            _usbDevice = sharedDevice;
            _wholeUsbDevice = wholeDevice;

            // Open bulk endpoint for LCD output (endpoint 0x02)
            _writer = _usbDevice.OpenEndpointWriter((WriteEndpointID)G19Constants.EndpointLcdOut);

            IsAvailable = _writer != null;

            if (!IsAvailable)
            {
                throw new Exception("Failed to open LCD output endpoint.");
            }
        }

        /// <summary>
        /// Close only the endpoints, not the device (for shared device mode)
        /// </summary>
        internal void CloseEndpoints()
        {
            ThrowIfDisposed();

            _writer?.Dispose();
            _writer = null;
            IsAvailable = false;
        }

        /// <summary>
        /// Close the connection to the device
        /// </summary>
        public void CloseDevice()
        {
            _writer?.Dispose();
            _writer = null;

            if (_usbDevice != null)
            {
                if (_usbDevice.IsOpen)
                {
                    if (_wholeUsbDevice != null)
                    {
                        _wholeUsbDevice.ReleaseInterface(G19Constants.UsbInterfaceLcd);
                    }
                    _usbDevice.Close();
                }
                (_usbDevice as IDisposable)?.Dispose();
                _usbDevice = null;
                UsbDevice.Exit();
            }

            IsAvailable = false;
        }

        /// <summary>
        /// Update the LCD screen with bitmap data.
        /// Accepts either:
        /// - raw RGB565 pixel data only (2 bytes per pixel, 320x240 = 153600 bytes), or
        /// - a full buffer including the 512-byte header followed by RGB565 data (LcdFullSize).
        /// If a full buffer is provided we write it directly to the endpoint to avoid an extra copy.
        /// </summary>
        /// <param name="lcdData">The pixel data or full LCD buffer.</param>
        public void UpdateScreen(byte[] lcdData)
        {
            if (lcdData == null)
            {
                throw new ArgumentNullException(nameof(lcdData));
            }

            UpdateScreenAsync(lcdData, lcdData.Length == G19Constants.LcdFullSize).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Update the LCD screen with raw RGB565 pixel data provided as a span.
        /// </summary>
        /// <param name="lcdData">Raw pixel data (320x240 RGB565).</param>
        public void UpdateScreen(ReadOnlySpan<byte> lcdData)
        {
            ThrowIfDisposed();

            if (!IsAvailable)
            {
                throw new InvalidOperationException("Device must be opened before updating screen.");
            }

            if (lcdData.Length != G19Constants.LcdDataSize)
            {
                throw new ArgumentException($"LCD data must be exactly {G19Constants.LcdDataSize} bytes (320x240 RGB565).", nameof(lcdData));
            }

            QueueRawPixelsAsync(lcdData).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Asynchronously update the LCD screen with either raw pixel data or a full header+payload buffer.
        /// </summary>
        /// <param name="lcdData">Pixel data or full LCD buffer.</param>
        /// <param name="includesHeader">Set to true when <paramref name="lcdData"/> already includes the LCD header.</param>
        public ValueTask UpdateScreenAsync(ReadOnlyMemory<byte> lcdData, bool includesHeader = false)
        {
            ThrowIfDisposed();

            if (!IsAvailable)
            {
                throw new InvalidOperationException("Device must be opened before updating screen.");
            }

            if (lcdData.IsEmpty)
            {
                throw new ArgumentNullException(nameof(lcdData));
            }

            if (includesHeader)
            {
                if (lcdData.Length != G19Constants.LcdFullSize)
                {
                    throw new ArgumentException($"LCD data must be exactly {G19Constants.LcdFullSize} bytes when the header is included.", nameof(lcdData));
                }

                return QueueFullFrameAsync(lcdData);
            }

            if (lcdData.Length != G19Constants.LcdDataSize)
            {
                throw new ArgumentException($"LCD data must be exactly {G19Constants.LcdDataSize} bytes (320x240 RGB565) when no header is supplied.", nameof(lcdData));
            }

            return QueueRawPixelsAsync(lcdData.Span);
        }

        private ValueTask QueueRawPixelsAsync(ReadOnlySpan<byte> pixelData)
        {
            byte[] targetBuffer;
            bool returnToPool = false;
            bool usesInternalBuffer = false;

            if (Interlocked.CompareExchange(ref _lcdBufferInFlight, 1, 0) == 0)
            {
                pixelData.CopyTo(_lcdBuffer.AsSpan(G19Constants.LcdHeaderSize, G19Constants.LcdDataSize));
                targetBuffer = _lcdBuffer;
                usesInternalBuffer = true;
            }
            else
            {
                targetBuffer = _framePool.Rent(G19Constants.LcdFullSize);
                Buffer.BlockCopy(G19Constants.LcdHeader, 0, targetBuffer, 0, G19Constants.LcdHeaderSize);
                pixelData.CopyTo(targetBuffer.AsSpan(G19Constants.LcdHeaderSize, G19Constants.LcdDataSize));
                returnToPool = true;
            }

            return EnqueueWrite(targetBuffer, 0, G19Constants.LcdFullSize, returnToPool, usesInternalBuffer, timeout: G19Constants.LcdUpdateTimeout);
        }

        private ValueTask QueueFullFrameAsync(ReadOnlyMemory<byte> fullFrame)
        {
            if (MemoryMarshal.TryGetArray(fullFrame, out ArraySegment<byte> segment) && segment.Array != null && segment.Count == G19Constants.LcdFullSize)
            {
                return EnqueueWrite(segment.Array, segment.Offset, segment.Count, returnToPool: false, usesInternalBuffer: false, timeout: G19Constants.LcdUpdateTimeout);
            }

            byte[] buffer = _framePool.Rent(G19Constants.LcdFullSize);
            fullFrame.Span.CopyTo(buffer.AsSpan(0, G19Constants.LcdFullSize));
            return EnqueueWrite(buffer, 0, G19Constants.LcdFullSize, returnToPool: true, usesInternalBuffer: false, timeout: G19Constants.LcdUpdateTimeout);
        }

        private ValueTask EnqueueWrite(byte[] buffer, int offset, int length, bool returnToPool, bool usesInternalBuffer, int timeout)
        {
            if (_disposed)
            {
                if (returnToPool)
                {
                    _framePool.Return(buffer);
                }

                if (usesInternalBuffer)
                {
                    Interlocked.Exchange(ref _lcdBufferInFlight, 0);
                }

                throw new ObjectDisposedException(nameof(LCD));
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingWrite(buffer, offset, length, timeout, returnToPool, usesInternalBuffer, tcs);

            try
            {
                _writeQueue.Add(pending);
            }
            catch (InvalidOperationException)
            {
                if (returnToPool)
                {
                    _framePool.Return(buffer);
                }

                if (usesInternalBuffer)
                {
                    Interlocked.Exchange(ref _lcdBufferInFlight, 0);
                }

                throw new ObjectDisposedException(nameof(LCD));
            }

            return pending.AsValueTask();
        }

        /// <summary>
        /// Set the LCD brightness level
        /// </summary>
        /// <param name="brightness">Brightness level (0-100)</param>
        public void SetBrightness(byte brightness)
        {
            ThrowIfDisposed();

            if (!IsAvailable)
            {
                throw new InvalidOperationException("Device must be opened before setting brightness.");
            }

            if (brightness > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(brightness), "Brightness must be between 0 and 100.");
            }

            byte[] data = new byte[1] { brightness };

            var packet = new UsbSetupPacket(
                G19Constants.RequestTypeLcd,
                G19Constants.RequestLcd,
                0x00,
                0x00,
                (short)data.Length);
            ControlTransferWithReconnect(ref packet, data);
        }

        /// <summary>
        /// Apply gamma correction to a color value for more linear brightness perception
        /// </summary>
        /// <param name="value">Input value (0-255)</param>
        /// <param name="gamma">Gamma to apply</param>
        /// <returns>Gamma corrected value (0-255)</returns>
        private static byte ApplyGammaCorrection(byte value, double gamma)
        {
            // Clamp gamma to a reasonable range
            if (gamma <= 0.01)
                gamma = 1.0;

            double normalized = value / 255.0;
            double corrected = Math.Pow(normalized, gamma);
            int outVal = (int)Math.Round(corrected * 255.0);
            if (outVal < 0) outVal = 0;
            if (outVal > 255) outVal = 255;
            return (byte)outVal;
        }

        // Per-channel gamma values. These can be tuned if white balance appears off.
        private const double GammaR = 2.2;
        private const double GammaG = 1.9;
        private const double GammaB = 2.2;

        /// <summary>
        /// Set the keyboard backlight color
        /// </summary>
        /// <param name="red">Red component (0-255)</param>
        /// <param name="green">Green component (0-255)</param>
        /// <param name="blue">Blue component (0-255)</param>
        public void SetBacklightColor(byte red, byte green, byte blue)
        {
            ThrowIfDisposed();

            if (!IsAvailable)
            {
                throw new InvalidOperationException("Device must be opened before setting backlight.");
            }

            // Detect near-white mixes where green is very high and red/blue
            // are reasonably close to green. In those cases remap red/blue
            // to a target value that tends to look whiter on the backlight
            // (example: (200,255,200) -> (150,255,150)). Primary colors
            // (pure red/green/blue) are unaffected because green won't be
            // both high and similar to red/blue.
            const byte GreenHighThreshold = 230; // green must be high to consider remapping
            const int WhiteSimilarityTolerance = 70; // how close R/B must be to G
            const byte RemapRedBlueForWhite = 150; // target for R and B when remapping

            byte outR = red;
            byte outG = green;
            byte outB = blue;

            if (outG >= GreenHighThreshold &&
                Math.Abs(outG - outR) <= WhiteSimilarityTolerance &&
                Math.Abs(outG - outB) <= WhiteSimilarityTolerance)
            {
                // Remap red and blue to target to produce a better white.
                outR = RemapRedBlueForWhite;
                outB = RemapRedBlueForWhite;
            }

            // Send (possibly remapped) raw RGB channels directly to the device.
            byte[] data = new byte[4];
            data[0] = 255;  // Always 255 for color mode
            data[1] = outR;
            data[2] = outG;
            data[3] = outB;

            var packet = new UsbSetupPacket(
                G19Constants.RequestTypeBacklight,
                G19Constants.RequestBacklight,
                G19Constants.ValueBacklight,
                G19Constants.IndexBacklight,
                (short)data.Length);

            ControlTransferWithReconnect(ref packet, data);
        }

        /// <summary>
        /// Attempt to reconnect the device by closing and reopening it.
        /// Must be called under _deviceLock.
        /// </summary>
        /// <returns>True if reconnect succeeded.</returns>
        private bool TryReconnect()
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                CloseDevice();
                // Small delay before reopening the device
                System.Threading.Thread.Sleep(DefaultReconnectDelayMs);
                OpenDevice();
                return IsAvailable;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Perform a bulk write and retry reconnecting on failure.
        /// This avoids allocating a new delegate per call.
        /// </summary>
        private void BulkWriteWithReconnect(byte[] buffer, int offset, int length, int timeout)
        {
            lock (_deviceLock)
            {
                int attempts = 0;
                while (true)
                {
                    if (_writer == null)
                    {
                        // Try to reconnect before the first attempt
                        if (!TryReconnect())
                        {
                            attempts++;
                            if (attempts > DefaultReconnectAttempts)
                                throw new Exception("LCD writer not available and reconnect failed.");
                            System.Threading.Thread.Sleep(DefaultReconnectDelayMs);
                            continue;
                        }
                    }

                    if (_writer == null)
                        throw new InvalidOperationException("LCD writer unavailable after reconnect.");

                    ErrorCode ec = _writer.Write(buffer, offset, length, timeout, out _);
                    if (ec == ErrorCode.None)
                        return;

                    attempts++;
                    if (attempts > DefaultReconnectAttempts)
                        throw new Exception($"Failed to update LCD: {ec} - {UsbDevice.LastErrorString}");

                    // Try reconnect then retry
                    if (!TryReconnect())
                        System.Threading.Thread.Sleep(DefaultReconnectDelayMs);
                }
            }
        }

        private void ProcessWriteQueue(CancellationToken token)
        {
            try
            {
                foreach (PendingWrite pending in _writeQueue.GetConsumingEnumerable(token))
                {
                    try
                    {
                        BulkWriteWithReconnect(pending.Buffer, pending.Offset, pending.Length, pending.Timeout);
                        pending.Complete(success: true, exception: null);
                    }
                    catch (Exception ex)
                    {
                        pending.Complete(success: false, exception: ex);
                    }
                    finally
                    {
                        if (pending.ReturnToPool)
                        {
                            _framePool.Return(pending.Buffer);
                        }

                        if (pending.UsesInternalBuffer)
                        {
                            Interlocked.Exchange(ref _lcdBufferInFlight, 0);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Queue shutdown requested.
            }
            finally
            {
                while (_writeQueue.TryTake(out PendingWrite? pending) && pending != null)
                {
                    pending.Complete(success: false, exception: new ObjectDisposedException(nameof(LCD)));

                    if (pending.ReturnToPool)
                    {
                        _framePool.Return(pending.Buffer);
                    }

                    if (pending.UsesInternalBuffer)
                    {
                        Interlocked.Exchange(ref _lcdBufferInFlight, 0);
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LCD));
            }
        }

        private sealed class PendingWrite
        {
            private readonly TaskCompletionSource<bool> _tcs;

            public PendingWrite(byte[] buffer, int offset, int length, int timeout, bool returnToPool, bool usesInternalBuffer, TaskCompletionSource<bool> tcs)
            {
                Buffer = buffer;
                Offset = offset;
                Length = length;
                Timeout = timeout;
                ReturnToPool = returnToPool;
                UsesInternalBuffer = usesInternalBuffer;
                _tcs = tcs;
            }

            public byte[] Buffer { get; }
            public int Offset { get; }
            public int Length { get; }
            public int Timeout { get; }
            public bool ReturnToPool { get; }
            public bool UsesInternalBuffer { get; }

            public void Complete(bool success, Exception? exception)
            {
                if (success)
                {
                    _tcs.TrySetResult(true);
                }
                else if (exception != null)
                {
                    _tcs.TrySetException(exception);
                }
                else
                {
                    _tcs.TrySetCanceled();
                }
            }

            public ValueTask AsValueTask()
            {
                return new ValueTask(_tcs.Task);
            }
        }

        /// <summary>
        /// Perform a control transfer and retry reconnecting on failure.
        /// Checks the boolean result and throws on failure after retries.
        /// </summary>
        private void ControlTransferWithReconnect(ref UsbSetupPacket packet, byte[] data)
        {
            lock (_deviceLock)
            {
                int attempts = 0;
                while (true)
                {
                    if (_usbDevice == null)
                    {
                        if (!TryReconnect())
                        {
                            attempts++;
                            if (attempts > DefaultReconnectAttempts)
                                throw new Exception("USB device not available and reconnect failed.");
                            System.Threading.Thread.Sleep(DefaultReconnectDelayMs);
                            continue;
                        }
                    }

                    if (_usbDevice == null)
                        throw new InvalidOperationException("USB device unavailable after reconnect.");

                    bool ok = _usbDevice.ControlTransfer(ref packet, data, data.Length, out _);
                    if (ok)
                        return;

                    attempts++;
                    if (attempts > DefaultReconnectAttempts)
                        throw new Exception($"Control transfer failed: {UsbDevice.LastErrorString}");

                    if (!TryReconnect())
                        System.Threading.Thread.Sleep(DefaultReconnectDelayMs);
                }
            }
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _writeQueue.CompleteAdding();
            try
            {
                _writeWorker.Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && ex.InnerException is OperationCanceledException)
            {
                // Worker cancelled during shutdown; safe to ignore.
            }

            _writeQueue.Dispose();
            _writeCts.Cancel();
            _writeCts.Dispose();

            CloseDevice();

            if (_lcdBufferHandle.IsAllocated)
            {
                _lcdBufferHandle.Free();
            }

            Interlocked.Exchange(ref _lcdBufferInFlight, 0);
            GC.SuppressFinalize(this);
        }
    }
}
