using SkiaSharp;

namespace GUI.Utils
{
    /// <summary>
    /// Recolors rasterized app icons purple for the BetterSource2Viewer themes.
    /// </summary>
    static class BetterIconTint
    {
        // Hue of the theme's primary color, rgb(212 187 252)
        private const float PurpleHue = 263f;

        // Saturation in percent below which a color reads as gray or white and is left alone
        private const float MinSaturation = 8f;

        /// <summary>Moves every colored pixel to the purple hue, keeping its saturation, lightness and alpha.</summary>
        public static void Apply(SKBitmap bitmap)
        {
            var pixels = bitmap.Pixels;

            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];

                if (color.Alpha == 0)
                {
                    continue;
                }

                color.ToHsl(out _, out var saturation, out var lightness);

                if (saturation < MinSaturation)
                {
                    continue;
                }

                pixels[i] = SKColor.FromHsl(PurpleHue, saturation, lightness, color.Alpha);
            }

            bitmap.Pixels = pixels;
        }
    }
}
