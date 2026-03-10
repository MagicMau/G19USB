using System;
using System.Linq;
using Xunit;
using G19USB;

namespace G19USB.Tests
{
    public class G19KeysTests
    {
        [Fact]
        public void None_IsZero()
        {
            Assert.Equal(0u, (uint)G19Keys.None);
        }

        [Theory]
        [InlineData(G19Keys.LHome,   1u)]
        [InlineData(G19Keys.LCancel, 2u)]
        [InlineData(G19Keys.LMenu,   4u)]
        [InlineData(G19Keys.LOk,     8u)]
        [InlineData(G19Keys.LRight,  16u)]
        [InlineData(G19Keys.LLeft,   32u)]
        [InlineData(G19Keys.LDown,   64u)]
        [InlineData(G19Keys.LUp,     128u)]
        [InlineData(G19Keys.G1,      256u)]
        [InlineData(G19Keys.G2,      512u)]
        [InlineData(G19Keys.G12,     1u << 19)]
        [InlineData(G19Keys.M1,      1u << 20)]
        [InlineData(G19Keys.M2,      1u << 21)]
        [InlineData(G19Keys.M3,      1u << 22)]
        [InlineData(G19Keys.MR,      1u << 23)]
        public void SpecificKey_HasExpectedBitValue(G19Keys key, uint expected)
        {
            Assert.Equal(expected, (uint)key);
        }

        [Fact]
        public void FlagsCombination_HasFlag_ReturnsTrue()
        {
            G19Keys combo = G19Keys.G1 | G19Keys.M1;
            Assert.True(combo.HasFlag(G19Keys.G1));
            Assert.True(combo.HasFlag(G19Keys.M1));
        }

        [Fact]
        public void FlagsCombination_HasFlag_ReturnsFalse_ForAbsentKey()
        {
            G19Keys combo = G19Keys.G1 | G19Keys.M1;
            Assert.False(combo.HasFlag(G19Keys.G2));
            Assert.False(combo.HasFlag(G19Keys.LHome));
        }

        [Fact]
        public void GKeys_DoNotOverlap_WithLKeysOrMKeys()
        {
            uint lKeysMask = 0xFF;          // bits 0-7
            uint mKeysMask = 0xF << 20;     // bits 20-23

            for (int i = 8; i <= 19; i++)
            {
                uint gBit = 1u << i;
                Assert.Equal(0u, gBit & lKeysMask);
                Assert.Equal(0u, gBit & mKeysMask);
            }
        }

        [Fact]
        public void AllDefinedKeys_HaveDistinctBits()
        {
            G19Keys[] allKeys =
            {
                G19Keys.LHome, G19Keys.LCancel, G19Keys.LMenu, G19Keys.LOk,
                G19Keys.LRight, G19Keys.LLeft, G19Keys.LDown, G19Keys.LUp,
                G19Keys.G1, G19Keys.G2, G19Keys.G3, G19Keys.G4,
                G19Keys.G5, G19Keys.G6, G19Keys.G7, G19Keys.G8,
                G19Keys.G9, G19Keys.G10, G19Keys.G11, G19Keys.G12,
                G19Keys.M1, G19Keys.M2, G19Keys.M3, G19Keys.MR,
            };

            Assert.Equal(24, allKeys.Length);
            // All values are distinct (each is a unique power of two)
            Assert.Equal(allKeys.Length, allKeys.Select(k => (uint)k).Distinct().Count());

            // Every key has exactly one bit set
            foreach (G19Keys key in allKeys)
            {
                uint v = (uint)key;
                Assert.True(v != 0 && (v & (v - 1)) == 0, $"{key} should have exactly one bit set");
            }
        }

        [Fact]
        public void MultiKeyCombo_AllFlagsReportedCorrectly()
        {
            G19Keys combo = G19Keys.LUp | G19Keys.G3 | G19Keys.M2;
            Assert.True(combo.HasFlag(G19Keys.LUp));
            Assert.True(combo.HasFlag(G19Keys.G3));
            Assert.True(combo.HasFlag(G19Keys.M2));
            Assert.False(combo.HasFlag(G19Keys.LDown));
            Assert.False(combo.HasFlag(G19Keys.G1));
            Assert.False(combo.HasFlag(G19Keys.M1));
        }
    }
}
