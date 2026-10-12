using System.IO;

namespace GUI.Utils
{
    /// <summary>
    /// Switches everyone to the BetterSource2Viewer themes once, keeping their light or dark choice.
    /// </summary>
    /// <remarks>
    /// Tracked with a marker file rather than the settings version, so upstream settings migrations never collide with it.
    /// </remarks>
    static class BetterThemeMigration
    {
        private const string MarkerFileName = "better-theme-applied";

        public static void ApplyOnce()
        {
            var marker = Path.Combine(Settings.SettingsFolder, MarkerFileName);

            if (string.IsNullOrEmpty(Settings.SettingsFolder) || File.Exists(marker))
            {
                return;
            }

            Settings.Config.Theme = (int)((Themer.AppTheme)Settings.Config.Theme switch
            {
                Themer.AppTheme.Light or Themer.AppTheme.BetterLight => Themer.AppTheme.BetterLight,
                Themer.AppTheme.Dark or Themer.AppTheme.Gray or Themer.AppTheme.BetterDark => Themer.AppTheme.BetterDark,
                _ => Themer.AppTheme.BetterSystem,
            });

            try
            {
                Settings.Save();
                File.WriteAllText(marker, string.Empty);
            }
            catch (Exception e)
            {
                Log.Error(nameof(BetterThemeMigration), $"Failed to save the theme switch: {e}");
            }
        }
    }
}
