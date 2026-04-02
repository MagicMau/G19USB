using System;

namespace G19USB
{
    /// <summary>
    /// Event data for <c>KeysChanged</c> notifications.
    /// </summary>
    /// <remarks>
    /// The timestamp is captured with <see cref="DateTime.Now"/> when the instance is created.
    /// </remarks>
    public class G19KeyEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the <see cref="G19Keys"/> flags reported by the key endpoint that triggered the event.
        /// </summary>
        /// <remarks>
        /// L-key reports and G/M-key reports are processed independently, so treat this value as the latest decoded state
        /// for that report rather than as a fully synchronized whole-device snapshot.
        /// </remarks>
        public G19Keys Keys { get; }

        /// <summary>
        /// Gets the local timestamp when these event arguments were created.
        /// </summary>
        public DateTime Timestamp { get; }

        /// <summary>
        /// Initialises a new instance with the current key state.
        /// </summary>
        /// <param name="keys">G19 key flags representing all currently pressed keys.</param>
        public G19KeyEventArgs(G19Keys keys)
        {
            Keys = keys;
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// Determines whether any of the specified flags are present in <see cref="Keys"/>.
        /// </summary>
        /// <param name="key">The flag or combination of flags to test.</param>
        /// <returns><see langword="true"/> when any bit in <paramref name="key"/> is also set in <see cref="Keys"/>.</returns>
        public bool IsKeyPressed(G19Keys key) => (Keys & key) != 0;
    }
}
