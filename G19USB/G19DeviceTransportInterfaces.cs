using System;
using System.Threading.Tasks;

namespace G19USB;

/// <summary>
/// Optional capability for sending a caller-built header-plus-payload LCD frame without a
/// second full-payload copy in the transport.
/// </summary>
public interface ICompleteFrameG19Device
{
    /// <summary>
    /// Sends exactly one complete <see cref="G19Constants.LcdFullSize"/>-byte LCD frame.
    /// </summary>
    /// <remarks>
    /// The array must remain unchanged until the call returns.
    /// </remarks>
    void UpdateLcdCompleteFrame(byte[] lcdFrame);
}

/// <summary>
/// Optional capability for asynchronously retaining only the newest pending LCD frame.
/// </summary>
public interface ILatestFrameG19Device
{
    /// <summary>
    /// Queues a raw payload or complete frame through the bounded latest-frame path.
    /// </summary>
    ValueTask UpdateLcdLatestAsync(ReadOnlyMemory<byte> lcdData, bool includesHeader = false);
}
