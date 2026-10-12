using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
            App = System.Drawing.Color.FromArgb(21, 18, 24),
            AppMiddle = System.Drawing.Color.FromArgb(33, 30, 36),
            AppSoft = System.Drawing.Color.FromArgb(44, 41, 47),
            Border = System.Drawing.Color.FromArgb(73, 69, 78),
            Contrast = System.Drawing.Color.FromArgb(231, 224, 232),
            ContrastSoft = System.Drawing.Color.FromArgb(149, 142, 153),
            ControlBoxHighlight = System.Drawing.Color.FromArgb(55, 52, 58),
            HoverAccent = System.Drawing.Color.FromArgb(81, 60, 115),
            Accent = System.Drawing.Color.FromArgb(212, 187, 252),
            Attention = System.Drawing.Color.FromArgb(186, 26, 26),
        };

        private static readonly ThemeColors BetterLightTheme = new()
        {
            App = System.Drawing.Color.FromArgb(222, 216, 224),
            AppMiddle = System.Drawing.Color.FromArgb(243, 236, 244),
            AppSoft = System.Drawing.Color.FromArgb(254, 247, 255),
            Border = System.Drawing.Color.FromArgb(203, 196, 207),
            Contrast = System.Drawing.Color.FromArgb(29, 26, 32),
            ContrastSoft = System.Drawing.Color.FromArgb(73, 69, 78),
            ControlBoxHighlight = System.Drawing.Color.FromArgb(203, 196, 207),
            HoverAccent = System.Drawing.Color.FromArgb(236, 220, 255),
            Accent = System.Drawing.Color.FromArgb(105, 84, 141),
            Attention = System.Drawing.Color.FromArgb(186, 26, 26),
        };

        private static readonly Lazy<WindowIcon> BetterIcon = new(static () => new(AssetLoader.Open(new Uri("avares://Source2Viewer.Avalonia/Assets/better-s2v.ico"))));
        private static readonly Lazy<Bitmap> BetterLogo = new(static () => new(AssetLoader.Open(new Uri("avares://Source2Viewer.Avalonia/Assets/better-s2v-icon.png"))));

        public static bool IsBetterTheme => CurrentTheme is AppTheme.BetterLight or AppTheme.BetterDark;

        /// <summary>Window title for the current theme.</summary>
        public static string AppTitle => IsBetterTheme ? BetterAppTitle : DefaultAppTitle;

        private static string GetBetterDisplayName(AppTheme theme) => theme switch
        {
            AppTheme.BetterSystem => $"{BetterAppTitle} (System)",
            AppTheme.BetterLight => $"{BetterAppTitle} Light",
            AppTheme.BetterDark => $"{BetterAppTitle} Dark",
            _ => theme.ToString(),
        };

        /// <summary>Resolves BetterSource2Viewer (System) to its light or dark theme.</summary>
        private static AppTheme ResolveBetterTheme(AppTheme theme, bool systemDark) => theme == AppTheme.BetterSystem
            ? (systemDark ? AppTheme.BetterDark : AppTheme.BetterLight)
            : theme;

        /// <summary>
        /// Shows the BetterSource2Viewer icon and title bar logo while one of its themes is selected,
        /// and refreshes them and the title whenever the theme changes.
        /// </summary>
        internal static void AttachBetterBranding(Window window, ThemedIcon logo, Action refreshTitle)
        {
            var defaultIcon = window.Icon;

            void Apply()
            {
                var better = IsBetterTheme;
                window.Icon = better ? BetterIcon.Value : defaultIcon;
                logo.FixedSource = better ? BetterLogo.Value : null;
            }

            Apply();

            ThemeChanged += (_, _) =>
            {
                Apply();
                refreshTitle();
            };
        }
    }
}
