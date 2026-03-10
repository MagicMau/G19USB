using Xunit;
using G19USB;

namespace G19USB.Tests
{
    public class G19ConstantsTests
    {
        [Fact]
        public void VendorId_IsLogitech()
        {
            Assert.Equal(0x046D, G19Constants.VendorId);
        }

        [Fact]
        public void ProductId_IsG19s()
        {
            Assert.Equal(0xC229, G19Constants.ProductId);
        }

        [Fact]
        public void LcdWidth_Is320()
        {
            Assert.Equal(320, G19Constants.LcdWidth);
        }

        [Fact]
        public void LcdHeight_Is240()
        {
            Assert.Equal(240, G19Constants.LcdHeight);
        }

        [Fact]
        public void LcdHeaderSize_Is512()
        {
            Assert.Equal(512, G19Constants.LcdHeaderSize);
        }

        [Fact]
        public void LcdDataSize_IsWidthTimesHeightTimesTwo()
        {
            Assert.Equal(G19Constants.LcdWidth * G19Constants.LcdHeight * 2, G19Constants.LcdDataSize);
            Assert.Equal(153600, G19Constants.LcdDataSize);
        }

        [Fact]
        public void LcdFullSize_IsHeaderPlusData()
        {
            Assert.Equal(G19Constants.LcdHeaderSize + G19Constants.LcdDataSize, G19Constants.LcdFullSize);
            Assert.Equal(154112, G19Constants.LcdFullSize);
        }

        [Fact]
        public void LcdHeader_LengthIs512()
        {
            Assert.Equal(512, G19Constants.LcdHeader.Length);
        }

        [Fact]
        public void LcdHeader_FirstByteIs0x10()
        {
            Assert.Equal(0x10, G19Constants.LcdHeader[0]);
        }

        [Fact]
        public void LcdHeader_Index16Is0x10()
        {
            Assert.Equal(0x10, G19Constants.LcdHeader[16]);
        }

        [Fact]
        public void EndpointLcdOut_Is0x02()
        {
            Assert.Equal(0x02, G19Constants.EndpointLcdOut);
        }

        [Fact]
        public void EndpointLKeysIn_Is0x81()
        {
            Assert.Equal(0x81, G19Constants.EndpointLKeysIn);
        }

        [Fact]
        public void EndpointGKeysIn_Is0x83()
        {
            Assert.Equal(0x83, G19Constants.EndpointGKeysIn);
        }
    }
}
