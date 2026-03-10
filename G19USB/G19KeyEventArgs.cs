using System;

namespace G19USB
{
    /// <summary>
    /// Event arguments for G19 key press events
    /// </summary>
    public class G19KeyEventArgs : EventArgs
    {
        /// <summary>
        /// The keys that are currently pressed
        /// </summary>
        public G19Keys Keys { get; }

        /// <summary>
        /// Timestamp of the key event
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
        /// Check if a specific key is pressed
        /// </summary>
        public bool IsKeyPressed(G19Keys key) => (Keys & key) != 0;
    }
}
