using System;

namespace G19USB
{
    /// <summary>
    /// Diagnostic event data for raw reads from the G/M-key endpoint.
    /// </summary>
    public sealed class G19GKeyBufferEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the decoded G/M key flags associated with <see cref="Buffer"/>, if the publisher supplied them.
        /// </summary>
        /// <remarks>
        /// <see cref="Keyboard.GKeyBufferReceived"/> currently raises this event before decoding and therefore passes
        /// <see cref="G19Keys.None"/>.
        /// </remarks>
        public G19Keys Keys { get; }

        /// <summary>
        /// Gets the raw bytes read from the G/M-key endpoint.
        /// </summary>
        /// <remarks>
        /// Only the first <see cref="BytesRead"/> bytes are meaningful. The backing array is stored by reference rather
        /// than cloned, so copy it if you need to keep the data after the event handler returns.
        /// </remarks>
        public byte[] Buffer { get; }

        /// <summary>
        /// Gets how many bytes in <see cref="Buffer"/> are meaningful for this event.
        /// </summary>
        public int BytesRead { get; }

        /// <summary>
        /// Gets the local timestamp when these event arguments were created.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Initialises a new instance with the decoded key state, raw buffer, and byte count.
        /// </summary>
        /// <param name="keys">Decoded G/M key flags, if available.</param>
        /// <param name="buffer">Raw bytes read from the G/M-key endpoint. The array is stored by reference.</param>
        /// <param name="bytesRead">Number of meaningful bytes in <paramref name="buffer"/>.</param>
        public G19GKeyBufferEventArgs(G19Keys keys, byte[] buffer, int bytesRead)
        {
            Keys = keys;
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            BytesRead = bytesRead;
            Timestamp = DateTime.Now;
        }
    }
}
