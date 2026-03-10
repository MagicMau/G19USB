using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace G19USB
{
    /// <summary>
    /// Helper utilities for working with the G19 device
    /// </summary>
    public static class G19Helpers
    {
        /// <summary>
        /// Convert a System.Drawing.Bitmap to RGB565 format for the G19 LCD
        /// </summary>
        /// <param name="bitmap">Source bitmap (should be 320x240)</param>
        /// <returns>Byte array in RGB565 format</returns>
        public static byte[] ConvertBitmapToRGB565(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (bitmap.Width != G19Constants.LcdWidth || bitmap.Height != G19Constants.LcdHeight)
            {
                // Resize to correct dimensions
                bitmap = ResizeBitmap(bitmap, G19Constants.LcdWidth, G19Constants.LcdHeight);
            }

            byte[] rgb565Data = new byte[G19Constants.LcdDataSize];

            // Lock the bitmap data for faster access
            BitmapData bmpData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                byte[] pixels = new byte[bmpData.Stride * bitmap.Height];
                Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);

                // Convert BGRA to RGB565 - iterate x first, then y (column-major order)
                for (int x = 0, pos = 0; x < G19Constants.LcdWidth; x++)
                {
                    for (int y = 0; y < G19Constants.LcdHeight; y++)
                    {
                        int i = (y * G19Constants.LcdWidth + x) * 4;

                        // BGRA format
                        int b = pixels[i];
                        int g = pixels[i + 1] >> 2;
                        int r = pixels[i + 2];

                        rgb565Data[pos++] = JoinGreenAndBlue(g, b);
                        rgb565Data[pos++] = JoinRedAndGreen(r, g);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }

            return rgb565Data;
        }

        /// <summary>
        /// Join green (6-bit) and blue (5-bit) into a byte for RGB565 format
        /// </summary>
        private static byte JoinGreenAndBlue(int g, int b)
        {
            return (byte)((g << 5) | (b >> 3));
        }

        /// <summary>
        /// Join red (5-bit) and green (6-bit) into a byte for RGB565 format
        /// </summary>
        private static byte JoinRedAndGreen(int r, int g)
        {
            return (byte)((r & 0xF8) | (g >> 3));
        }

        /// <summary>
        /// Resize a bitmap to the specified dimensions
        /// </summary>
        private static Bitmap ResizeBitmap(Bitmap source, int width, int height)
        {
            var resized = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.DrawImage(source, 0, 0, width, height);
            }
            return resized;
        }

        /// <summary>
        /// Create a solid color bitmap in RGB565 format
        /// </summary>
        public static byte[] CreateSolidColor(Color color)
        {
            byte[] data = new byte[G19Constants.LcdDataSize];

            // Convert color to RGB565
            ushort rgb565 = (ushort)(
                ((color.R & 0xF8) << 8) |
                ((color.G & 0xFC) << 3) |
                (color.B >> 3)
            );

            byte low = (byte)(rgb565 & 0xFF);
            byte high = (byte)(rgb565 >> 8);

            // Fill entire buffer with this color
            for (int i = 0; i < data.Length; i += 2)
            {
                data[i] = low;
                data[i + 1] = high;
            }

            return data;
        }

        /// <summary>
        /// Create a test pattern for the LCD
        /// </summary>
        public static byte[] CreateTestPattern()
        {
            using (var bitmap = new Bitmap(G19Constants.LcdWidth, G19Constants.LcdHeight))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                // Draw a gradient background
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    Color.Navy,
                    Color.DarkBlue,
                    45f))
                {
                    graphics.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
                }

                // Draw some test text
                using (var font = new Font("Arial", 16, FontStyle.Bold))
                using (var brush = new SolidBrush(Color.White))
                {
                    graphics.DrawString("G19 USB Test", font, brush, 10, 10);
                    graphics.DrawString("320x240 RGB565", font, brush, 10, 40);
                    graphics.DrawString($"Resolution Test", font, brush, 10, 70);
                }

                // Draw color bars
                int barWidth = bitmap.Width / 7;
                Color[] colors = { Color.Red, Color.Green, Color.Blue, Color.Cyan, Color.Magenta, Color.Yellow, Color.White };
                for (int i = 0; i < colors.Length; i++)
                {
                    using (var brush = new SolidBrush(colors[i]))
                    {
                        graphics.FillRectangle(brush, i * barWidth, bitmap.Height - 40, barWidth, 40);
                    }
                }

                return ConvertBitmapToRGB565(bitmap);
            }
        }

        /// <summary>
        /// Get a human-readable string for key combinations
        /// </summary>
        public static string GetKeyString(G19Keys keys)
        {
            if (keys == G19Keys.None)
                return "None";

            var parts = new System.Collections.Generic.List<string>();

            // Check each key flag
            foreach (G19Keys key in Enum.GetValues<G19Keys>())
            {
                if (key != G19Keys.None && (keys & key) != 0)
                {
                    parts.Add(key.ToString());
                }
            }

            return string.Join(" + ", parts);
        }
    }
}
