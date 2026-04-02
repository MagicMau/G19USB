using System.Reflection;
using Xunit;

namespace G19USB.Tests
{
    public class G19DeviceTests
    {
        [Fact]
        public void Dispose_DisposesBothChildHelpers()
        {
            var device = new G19USB.G19Device();

            device.Dispose();

            Assert.True(GetDisposedFlag(device.LCD));
            Assert.True(GetDisposedFlag(device.Keyboard));
        }

        private static bool GetDisposedFlag(object instance)
        {
            FieldInfo field = instance.GetType().GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (bool)field.GetValue(instance)!;
        }
    }
}
