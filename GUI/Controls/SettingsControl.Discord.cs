using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;

namespace GUI.Controls
{
    /// <summary>
    /// The Discord sign in that unlocks removing the export watermark, kept apart from the rest of
    /// <see cref="SettingsControl"/> so upstream changes merge cleanly.
    /// </summary>
    partial class SettingsControl
    {
        private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

#pragma warning disable CA2213 // Disposed with the Discord group they are added to
        private Label? discordStatusLabel;
        private ThemedButton? discordSignInButton;
        private CheckBox? removeWatermarkCheckBox;
#pragma warning restore CA2213

#pragma warning disable CA2213 // Disposed by the sign in that created it
        private CancellationTokenSource? discordSignInCancellation;
#pragma warning restore CA2213

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (WatermarkUnlock.IsAvailable)
            {
                AddDiscordGroup();
            }
        }

        private void AddDiscordGroup()
        {
            discordStatusLabel = new Label
            {
                AutoSize = true,
                Location = new Point(15, 36),
            };

            discordSignInButton = new ThemedButton
            {
                ClickedBackColor = Color.Gray,
                CornerRadius = 5,
                FlatStyle = FlatStyle.Flat,
                LabelFormatFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis,
                Location = new Point(15, 68),
                Size = new Size(208, 30),
                UseVisualStyleBackColor = true,
            };
            discordSignInButton.Click += OnDiscordSignInButtonClick;

            removeWatermarkCheckBox = new CheckBox
            {
                AutoSize = true,
                Location = new Point(15, 110),
                Text = "Remove the watermark from character exports",
                UseVisualStyleBackColor = true,
            };
            removeWatermarkCheckBox.CheckedChanged += (_, _) => WatermarkUnlock.RemoveWatermark = removeWatermarkCheckBox.Checked;

            var group = new ThemedGroupBox
            {
                BorderColor = Color.Black,
                BorderWidth = 2,
                CornerRadius = 5,
                Dock = DockStyle.Top,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(16, 18, 16, 16),
                Size = new Size(501, 150),
                TabStop = false,
                Text = "Discord",
            };
            group.Controls.AddRange([discordStatusLabel, discordSignInButton, removeWatermarkCheckBox]);

            // Docked controls stack from the last one added, so this lands between the last group and the footer
            Controls.Add(group);
            Controls.SetChildIndex(group, Controls.IndexOf(footerPanel) + 1);
            Themer.ThemeControl(group);

            Disposed += (_, _) => discordSignInCancellation?.Cancel();

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
            if (IsDisposed)
            {
                return;
            }

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

            removeWatermarkCheckBox!.Visible = WatermarkUnlock.IsWhitelisted;
            removeWatermarkCheckBox.Checked = WatermarkUnlock.RemoveWatermark;
        }

        private void ShowDiscordState(string status, string buttonText, bool buttonEnabled)
        {
            discordStatusLabel!.Text = status;
            discordSignInButton!.Text = buttonText;
            discordSignInButton.Enabled = buttonEnabled;
            removeWatermarkCheckBox!.Visible = false;
        }

        private async void OnDiscordSignInButtonClick(object? sender, EventArgs e)
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
