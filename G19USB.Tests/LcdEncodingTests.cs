using Xunit;

namespace G19USB.Tests
{
    /// <summary>
    /// Specification tests for the G19s LCD RGB565 column-major pixel encoding.
    /// These tests verify the encoding formula directly without touching any hardware
    /// or USB code.
    ///
    /// Encoding formula (from LCD.cs):
    ///   Source: PBGRA bitmap — B at byte[0], G at byte[1], R at byte[2]
    ///   Destination: column-major (x outer, y inner), 2 bytes per pixel (RGB565)
    ///     dstOffset        = (x * Height + y) * 2
    ///     g6               = G >> 2
    ///     buffer[dstOffset]     = (byte)((g6 &lt;&lt; 5) | (b >> 3))
    ///     buffer[dstOffset + 1] = (byte)((r &amp; 0xF8) | (g6 >> 3))
    /// </summary>
    public class LcdEncodingTests
    {
        private const int Width = 320;
        private const int Height = 240;

        // ── Column-major offset arithmetic ──────────────────────────────────

        [Fact]
        public void ColumnMajorOrder_Pixel_0_0_IsAtOffset0()
        {
            int x = 0, y = 0;
            int dstOffset = (x * Height + y) * 2;
            Assert.Equal(0, dstOffset);
        }

        [Fact]
        public void ColumnMajorOrder_Pixel_1_0_IsAtOffset480()
        {
            // x=1 column starts after a full column of 240 pixels × 2 bytes
            int x = 1, y = 0;
            int dstOffset = (x * Height + y) * 2;
            Assert.Equal(480, dstOffset);
        }

        [Fact]
        public void ColumnMajorOrder_Pixel_0_1_IsAtOffset2()
        {
            // Moving down one row within the first column advances by 2 bytes
            int x = 0, y = 1;
            int dstOffset = (x * Height + y) * 2;
            Assert.Equal(2, dstOffset);
        }

        // ── RGB565 encoding: pure primaries ─────────────────────────────────

        [Fact]
        public void RGB565Encoding_PureRed()
        {
            byte b = 0, g = 0, r = 255;
            byte g6 = (byte)(g >> 2);

            byte lo = (byte)((g6 << 5) | (b >> 3));
            byte hi = (byte)((r & 0xF8) | (g6 >> 3));

            Assert.Equal(0x00, lo);
            Assert.Equal(0xF8, hi);
        }

        [Fact]
        public void RGB565Encoding_PureGreen()
        {
            byte b = 0, g = 255, r = 0;
            byte g6 = (byte)(g >> 2);   // 63

            byte lo = (byte)((g6 << 5) | (b >> 3));   // (63<<5)&0xFF = 0xE0
            byte hi = (byte)((r & 0xF8) | (g6 >> 3)); // 0 | 7 = 0x07

            Assert.Equal(0xE0, lo);
            Assert.Equal(0x07, hi);
        }

        [Fact]
        public void RGB565Encoding_PureBlue()
        {
            byte b = 255, g = 0, r = 0;
            byte g6 = (byte)(g >> 2);   // 0

            byte lo = (byte)((g6 << 5) | (b >> 3));   // 0 | 31 = 0x1F
            byte hi = (byte)((r & 0xF8) | (g6 >> 3)); // 0 | 0  = 0x00

            Assert.Equal(0x1F, lo);
            Assert.Equal(0x00, hi);
        }

        [Fact]
        public void RGB565Encoding_White()
        {
            byte b = 255, g = 255, r = 255;
            byte g6 = (byte)(g >> 2);   // 63

            byte lo = (byte)((g6 << 5) | (b >> 3));   // 0xE0 | 0x1F = 0xFF
            byte hi = (byte)((r & 0xF8) | (g6 >> 3)); // 0xF8 | 7    = 0xFF

            Assert.Equal(0xFF, lo);
            Assert.Equal(0xFF, hi);
        }

        [Fact]
        public void RGB565Encoding_Black()
        {
            byte b = 0, g = 0, r = 0;
            byte g6 = (byte)(g >> 2);

            byte lo = (byte)((g6 << 5) | (b >> 3));
            byte hi = (byte)((r & 0xF8) | (g6 >> 3));

            Assert.Equal(0x00, lo);
            Assert.Equal(0x00, hi);
        }

        // ── Sanity: G channel round-trips through 6-bit quantisation ────────

        [Fact]
        public void GreenChannel_Uses6Bits_MaxValueIs63()
        {
            byte g6 = (byte)(255 >> 2);
            Assert.Equal(63, g6);
        }

        [Fact]
        public void RedAndBlue_Use5Bits_MaskWorks()
        {
            // Red: top 5 bits of r end up in hi byte
            Assert.Equal(0xF8, 255 & 0xF8);   // 5-bit mask keeps top 5 bits
            // Blue: top 5 bits of b end up in lo byte
            Assert.Equal(31, 255 >> 3);        // 255 >> 3 = 31 (0x1F)
        }
    }
}
