using System;
using Xunit;
using G19USB;

namespace G19USB.Tests
{
    public class G19KeyEventArgsTests
    {
        [Fact]
        public void Keys_Property_ReturnsConstructorValue()
        {
            var args = new G19KeyEventArgs(G19Keys.G1 | G19Keys.M2);
            Assert.Equal(G19Keys.G1 | G19Keys.M2, args.Keys);
        }

        [Fact]
        public void IsKeyPressed_ReturnsTrue_ForPressedKey()
        {
            var args = new G19KeyEventArgs(G19Keys.LUp | G19Keys.G3);
            Assert.True(args.IsKeyPressed(G19Keys.LUp));
            Assert.True(args.IsKeyPressed(G19Keys.G3));
        }

        [Fact]
        public void IsKeyPressed_ReturnsFalse_ForUnpressedKey()
        {
            var args = new G19KeyEventArgs(G19Keys.G1);
            Assert.False(args.IsKeyPressed(G19Keys.G2));
            Assert.False(args.IsKeyPressed(G19Keys.LHome));
            Assert.False(args.IsKeyPressed(G19Keys.M1));
        }

        [Fact]
        public void Timestamp_IsCloseToNow()
        {
            DateTime before = DateTime.Now;
            var args = new G19KeyEventArgs(G19Keys.None);
            DateTime after = DateTime.Now;

            Assert.InRange(args.Timestamp, before.AddSeconds(-1), after.AddSeconds(1));
        }

        [Fact]
        public void MultipleKeys_AllReportedCorrectly()
        {
            G19Keys pressed = G19Keys.G1 | G19Keys.G5 | G19Keys.M1 | G19Keys.LDown;
            var args = new G19KeyEventArgs(pressed);

            Assert.True(args.IsKeyPressed(G19Keys.G1));
            Assert.True(args.IsKeyPressed(G19Keys.G5));
            Assert.True(args.IsKeyPressed(G19Keys.M1));
            Assert.True(args.IsKeyPressed(G19Keys.LDown));

            Assert.False(args.IsKeyPressed(G19Keys.G2));
            Assert.False(args.IsKeyPressed(G19Keys.G6));
            Assert.False(args.IsKeyPressed(G19Keys.M2));
            Assert.False(args.IsKeyPressed(G19Keys.LUp));
        }

        [Fact]
        public void NoKeys_IsKeyPressed_AlwaysReturnsFalse()
        {
            var args = new G19KeyEventArgs(G19Keys.None);
            Assert.False(args.IsKeyPressed(G19Keys.G1));
            Assert.False(args.IsKeyPressed(G19Keys.LHome));
            Assert.False(args.IsKeyPressed(G19Keys.M3));
        }
    }
}
