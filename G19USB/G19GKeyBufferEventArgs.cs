using System;

namespace G19USB
{
    /// <summary>
    /// Event arguments containing the raw G-key endpoint buffer and decoded key state.
    /// </summary>
    public sealed class G19GKeyBufferEventArgs : EventArgs
    {
        /// <summary>
        /// The decoded G/M key state parsed from the buffer.
        /// </summary>
        public G19Keys Keys { get; }

        /// <summary>
        /// Raw bytes read from the G-key endpoint.
        /// </summary>
        public byte[] Buffer { get; }

        /// <summary>
        /// Number of bytes actually read from the device.
        /// </summary>
        public int BytesRead { get; }

        /// <summary>
        /// Timestamp when the buffer was received.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Initialises a new instance with the decoded key state, raw buffer, and byte count.
        /// </summary>
        /// <param name="keys">Decoded G/M key flags.</param>
        /// <param name="buffer">Raw bytes read from the G-key endpoint.</param>
        /// <param name="bytesRead">Number of bytes actually read.</param>
        public G19GKeyBufferEventArgs(G19Keys keys, byte[] buffer, int bytesRead)
        {
            Keys = keys;
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            BytesRead = bytesRead;
            Timestamp = DateTime.Now;
        }
    }
}
