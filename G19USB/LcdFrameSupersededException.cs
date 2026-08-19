using System;

namespace G19USB;

/// <summary>
/// Indicates that a pending latest-frame LCD submission was replaced by a newer frame before
/// the USB worker started transmitting it.
/// </summary>
public sealed class LcdFrameSupersededException : InvalidOperationException
{
    /// <summary>
    /// Creates an exception describing a safely replaced pending frame.
    /// </summary>
    public LcdFrameSupersededException()
        : base("The pending LCD frame was superseded by a newer frame.")
    {
    }
}
