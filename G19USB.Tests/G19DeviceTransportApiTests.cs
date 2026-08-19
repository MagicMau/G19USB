using System;
using Xunit;

namespace G19USB.Tests;

public sealed class G19DeviceTransportApiTests
{
    [Fact]
    public void G19Device_exposes_complete_and_latest_frame_capabilities()
    {
        using var device = new G19Device();

        Assert.IsAssignableFrom<ICompleteFrameG19Device>(device);
        Assert.IsAssignableFrom<ILatestFrameG19Device>(device);
    }

    [Fact]
    public void Latest_frame_api_rejects_invalid_frame_sizes_before_opening_usb()
    {
        using var device = new G19Device();
        var latest = Assert.IsAssignableFrom<ILatestFrameG19Device>(device);

        Assert.Throws<InvalidOperationException>(() =>
            latest.UpdateLcdLatestAsync(new byte[1]).AsTask().GetAwaiter().GetResult());
    }
}
