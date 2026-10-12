using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;
using SkiaSharp;

namespace GUI.Utils
{
    /// <summary>
    /// The BetterSource2Viewer themes, kept apart from the rest of <see cref="Themer"/> so upstream changes merge cleanly.
    /// </summary>
    public static partial class Themer
    {
        public const string DefaultAppTitle = "Source 2 Viewer";
        public const string BetterAppTitle = "BetterSource2Viewer";

        private static readonly ThemeColors BetterDarkTheme = new()
        {
            App = Color.FromArgb(21, 18, 24),
            AppMiddle = Color.FromArgb(33, 30, 36),
            AppSoft = Color.FromArgb(44, 41, 47),

            Border = Color.FromArgb(73, 69, 78),

            Contrast = Color.FromArgb(231, 224, 232),
            ContrastSoft = Color.FromArgb(149, 142, 153),

            ControlBoxHighlight = Color.FromArgb(55, 52, 58),
            ControlBoxHighlightCloseButton = Color.FromArgb(186, 26, 26),

            HoverAccent = Color.FromArgb(81, 60, 115),
            Accent = Color.FromArgb(212, 187, 252),
            Attention = Color.FromArgb(186, 26, 26),

            ColorMode = SystemColorMode.Dark,
        };

        private static readonly ThemeColors BetterLightTheme = new()
        {
            App = Color.FromArgb(222, 216, 224),
            AppMiddle = Color.FromArgb(243, 236, 244),
            AppSoft = Color.FromArgb(254, 247, 255),

            Border = Color.FromArgb(203, 196, 207),

            Contrast = Color.FromArgb(29, 26, 32),
            ContrastSoft = Color.FromArgb(73, 69, 78),

            ControlBoxHighlight = Color.FromArgb(203, 196, 207),
            ControlBoxHighlightCloseButton = Color.FromArgb(186, 26, 26),

            HoverAccent = Color.FromArgb(236, 220, 255),
            Accent = Color.FromArgb(105, 84, 141),
            Attention = Color.FromArgb(186, 26, 26),

            ColorMode = SystemColorMode.Classic,
        };

        private static readonly Lazy<Icon?> BetterIcon = new(static () =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GUI.Utils.better-s2v.ico");
            return stream == null ? null : new Icon(stream);
        });

        public static bool IsBetterTheme => CurrentTheme is AppTheme.BetterLight or AppTheme.BetterDark;

        /// <summary>Window title for the current theme.</summary>
        public static string AppTitle => IsBetterTheme ? BetterAppTitle : DefaultAppTitle;

        /// <summary>Display name of a theme in settings.</summary>
        public static string GetDisplayName(AppTheme theme) => theme switch
        {
            AppTheme.BetterSystem => $"{BetterAppTitle} (System)",
            AppTheme.BetterLight => $"{BetterAppTitle} Light",
            AppTheme.BetterDark => $"{BetterAppTitle} Dark",
            _ => theme.ToString(),
        };

        /// <summary>Applies the one-time switch and picks the BetterSource2Viewer colors when one of its themes is selected.</summary>
        private static void InitializeBetterTheme()
        {
            BetterThemeMigration.ApplyOnce();

            var theme = (AppTheme)Settings.Config.Theme;

            if (theme is not (AppTheme.BetterSystem or AppTheme.BetterLight or AppTheme.BetterDark))
            {
                return;
            }

            if (theme == AppTheme.BetterSystem)
            {
                theme = Application.SystemColorMode == SystemColorMode.Dark ? AppTheme.BetterDark : AppTheme.BetterLight;
            }

            CurrentTheme = theme;
            CurrentThemeColors = theme == AppTheme.BetterDark ? BetterDarkTheme : BetterLightTheme;
        }

        /// <summary>Recolors a rasterized app icon purple while one of the BetterSource2Viewer themes is selected.</summary>
        private static void TintBetterIcon(SKBitmap bitmap, bool tintIcon)
        {
            if (tintIcon && IsBetterTheme)
            {
                BetterIconTint.Apply(bitmap);
            }
        }

        /// <summary>Shows the BetterSource2Viewer icon and title bar logo while one of its themes is selected.</summary>
        public static void ApplyBetterBranding(Form form, PictureBox logo)
        {
            if (!IsBetterTheme)
            {
                return;
            }

            if (BetterIcon.Value is { } icon)
            {
                form.Icon = icon;
            }

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GUI.Utils.better-s2v-icon.png");

            if (stream == null)
            {
                return;
            }

            using var source = new Bitmap(stream);
            var image = new Bitmap(logo.Width, logo.Height);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, 0, 0, logo.Width, logo.Height);
            }

            logo.Image?.Dispose();
            logo.Image = image;
        }
    }
}
