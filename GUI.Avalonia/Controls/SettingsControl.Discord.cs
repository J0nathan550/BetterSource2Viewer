using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// The Discord sign in that unlocks removing the export watermark.
    /// </summary>
    sealed partial class SettingsControl
    {
        private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

        private TextBlock? discordStatus;
        private Button? discordSignInButton;
        private CheckBox? removeWatermarkCheckBox;
        private CancellationTokenSource? discordSignInCancellation;

        /// <summary>Adds the Discord group above the footer, when signing in is available.</summary>
        private void AddDiscordGroup(StackPanel page)
        {
            if (!WatermarkUnlock.IsAvailable)
            {
                return;
            }

            discordStatus = Label(string.Empty);
            discordSignInButton = new Button { Width = 208, Height = 30 };
            discordSignInButton.Click += OnDiscordSignInButtonClick;
            removeWatermarkCheckBox = Check("Remove the watermark from character exports", WatermarkUnlock.RemoveWatermark, v => WatermarkUnlock.RemoveWatermark = v);

            page.Children.Insert(page.Children.Count - 1, CanvasGroup("Discord", 150,
                (discordStatus, 15, 36),
                (discordSignInButton, 15, 72),
                (removeWatermarkCheckBox, 15, 110)));

            _ = RefreshDiscordGroupAsync();
        }

        private async Task RefreshDiscordGroupAsync()
        {
            if (WatermarkUnlock.HasSession)
            {
                ShowDiscordState("Checking the Discord account...", "Sign out", buttonEnabled: false);
                await WatermarkUnlock.VerifyAsync().ConfigureAwait(true);
            }

            ShowDiscordAccount();
        }

        private void ShowDiscordAccount()
        {
            if (WatermarkUnlock.UserName is not { } userName)
            {
                ShowDiscordState(WatermarkUnlock.HasSession
                    ? "The Discord account could not be checked, see the console."
                    : "Sign in with Discord to unlock supporter options.",
                    WatermarkUnlock.HasSession ? "Sign out" : "Sign in with Discord", buttonEnabled: true);
                return;
            }

            ShowDiscordState(WatermarkUnlock.IsWhitelisted
                ? $"Signed in as {userName}."
                : $"Signed in as {userName}, which is not on the supporter whitelist, or it could not be read.",
                "Sign out", buttonEnabled: true);

            removeWatermarkCheckBox!.IsVisible = WatermarkUnlock.IsWhitelisted;
            removeWatermarkCheckBox.IsChecked = WatermarkUnlock.RemoveWatermark;
        }

        private void ShowDiscordState(string status, string buttonText, bool buttonEnabled)
        {
            discordStatus!.Text = status;
            discordSignInButton!.Content = buttonText;
            discordSignInButton.IsEnabled = buttonEnabled;
            removeWatermarkCheckBox!.IsVisible = false;
        }

        private async void OnDiscordSignInButtonClick(object? sender, RoutedEventArgs e)
        {
            if (discordSignInCancellation != null)
            {
                await discordSignInCancellation.CancelAsync().ConfigureAwait(true);
                return;
            }

            if (WatermarkUnlock.HasSession)
            {
                WatermarkUnlock.SignOut();
                ShowDiscordAccount();
                return;
            }

            using var cancellation = new CancellationTokenSource(SignInTimeout);
            discordSignInCancellation = cancellation;
            ShowDiscordState("Waiting for Discord in the browser...", "Cancel", buttonEnabled: true);

            try
            {
                await WatermarkUnlock.SignInAsync(cancellation.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Cancelled from the button, or the browser was never finished with
            }
            catch (Exception ex)
            {
                Log.Error(nameof(SettingsControl), $"Discord sign in failed: {ex.Message}");
                await AppMessageDialogs.ShowMessageAsync($"Signing in with Discord failed: {ex.Message}", "Discord sign in", MessageIcon.Warning).ConfigureAwait(true);
            }
            finally
            {
                discordSignInCancellation = null;
            }

            ShowDiscordAccount();
        }
    }
}
