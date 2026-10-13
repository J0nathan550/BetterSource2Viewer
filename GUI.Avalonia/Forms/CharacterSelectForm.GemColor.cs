using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Types.Exporter.CharacterAssets;
using DrawingColor = System.Drawing.Color;

namespace GUI.Forms
{
    sealed partial class CharacterSelectForm
    {
        // Picked by hand, null follows the equipped items: their own color, and on when they have one
        private DrawingColor? pickedGemColor;
        private bool? pickedGemColorEnabled;
        private bool tintOtherEffects = true;

        private GemColorChoice? itemGemColor;
        private bool updatingGemColor;

        private readonly CheckBox gemColorCheckBox = new() { Content = "Write the color into the effects and materials", Margin = new(3, 3, 3, 6) };
        private readonly Button gemColorButton = new() { Width = 64, Height = 24, Margin = new(0, 0, 6, 0) };
        private readonly ComboBox gemColorComboBox = new() { Width = 240, Margin = new(0, 0, 6, 0) };
        private readonly TextBlock gemColorValueLabel = new() { VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox gemTintCheckBox = new() { Content = "Also tint the hero's effects that take no gem color, e.g. Metamorphosis", IsChecked = true, Margin = new(12, 6, 3, 3) };

        /// <summary>The color the effects get, see <see cref="CharacterExportOptions.GemColor"/>, or null to leave them as they are.</summary>
        private DrawingColor? GetGemColor() => (pickedGemColorEnabled ?? itemGemColor != null) ? CurrentGemColor : null;

        private DrawingColor CurrentGemColor => pickedGemColor ?? itemGemColor?.Color ?? (catalog.GemColors.Count > 0 ? catalog.GemColors[0].Choice.Color : DrawingColor.Red);

        private bool IsGemColorPicked => pickedGemColor != null || pickedGemColorEnabled != null || !tintOtherEffects;

        /// <summary>See <see cref="CharacterExportOptions.TintOtherEffects"/>.</summary>
        private bool GetTintOtherEffects() => tintOtherEffects && GetGemColor() != null;

        private TabItem CreateGemColorTab()
        {
            gemColorCheckBox.IsCheckedChanged += (_, _) =>
            {
                if (updatingGemColor)
                {
                    return;
                }

                // Back to what the equipped items do, it follows them again
                var isChecked = gemColorCheckBox.IsChecked == true;
                pickedGemColorEnabled = isChecked == (itemGemColor != null) ? null : isChecked;
                UpdateGemColorControls();
            };

            gemColorButton.Click += async (_, _) =>
            {
                if (await ColorPickerDialog.ShowAsync(CurrentGemColor, "Prismatic color").ConfigureAwait(true) is { } picked)
                {
                    PickGemColor(picked);
                }
            };
            ToolTip.SetTip(gemColorButton, "Pick any color");

            gemColorComboBox.SelectionChanged += (_, _) =>
            {
                if (!updatingGemColor && gemColorComboBox.SelectedItem is ComboBoxItem { Tag: GemColorChoice choice })
                {
                    PickGemColor(choice.Color);
                }
            };
            ToolTip.SetTip(gemColorComboBox, "The colors prismatic gems come in");

            gemTintCheckBox.IsCheckedChanged += (_, _) =>
            {
                if (!updatingGemColor)
                {
                    tintOtherEffects = gemTintCheckBox.IsChecked == true;
                }
            };
            ToolTip.SetTip(gemTintCheckBox, "Gives their colors the picked color's hue, white, grey and black stay as they are. " +
                "The game shows them in their own colors even with the arcana.");

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Prismatic gems color the effects and materials of some items, and some arcanas have a color of their own without one: " +
                    "Terrorblade's is red and Techies' orange. The game only gives them that color with the item equipped, so it is written into " +
                    "every particle and material that takes it, the hero's own ability effects included, e.g. Rictus Ripper's eye.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new(3, 6, 3, 6),
            });
            panel.Children.Add(gemColorCheckBox);
            panel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new(12, 0, 3, 3),
                Children = { gemColorButton, gemColorComboBox, gemColorValueLabel },
            });
            panel.Children.Add(gemTintCheckBox);

            return new TabItem { Header = "Prismatic color", Content = ScrollPage(panel) };
        }

        /// <summary>
        /// Forgets the color picked for the previous hero.
        /// </summary>
        private void ResetGemColor()
        {
            pickedGemColor = null;
            pickedGemColorEnabled = null;
            tintOtherEffects = true;
        }

        /// <summary>
        /// Offers the equipped items' own color first, and turns the color on for them unless it was picked by hand.
        /// </summary>
        private void UpdateGemColor(CharacterLoadout loadout)
        {
            itemGemColor = CharacterGemColor.GetItemColor(catalog, loadout.Items);

            updatingGemColor = true;

            try
            {
                gemColorComboBox.Items.Clear();

                if (itemGemColor != null)
                {
                    gemColorComboBox.Items.Add(CreateGemColorItem(itemGemColor with { Name = $"{itemGemColor.Name} (the item's own color)" }));
                }

                foreach (var (_, choice) in catalog.GemColors)
                {
                    gemColorComboBox.Items.Add(CreateGemColorItem(choice));
                }
            }
            finally
            {
                updatingGemColor = false;
            }

            UpdateGemColorControls();
        }

        private static ComboBoxItem CreateGemColorItem(GemColorChoice choice) => new()
        {
            Tag = choice,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new Border { Width = 16, Height = 16, Background = new SolidColorBrush(Color.FromRgb(choice.Color.R, choice.Color.G, choice.Color.B)) },
                    new TextBlock { Text = choice.Name, VerticalAlignment = VerticalAlignment.Center },
                },
            },
        };

        private void PickGemColor(DrawingColor color)
        {
            pickedGemColor = color.ToArgb() == itemGemColor?.Color.ToArgb() ? null : color;

            // Picking a color means wanting it
            if (itemGemColor == null)
            {
                pickedGemColorEnabled = true;
            }
            else if (pickedGemColorEnabled == false)
            {
                pickedGemColorEnabled = null;
            }

            UpdateGemColorControls();
        }

        private void UpdateGemColorControls()
        {
            var color = CurrentGemColor;
            var enabled = GetGemColor() != null;

            updatingGemColor = true;

            try
            {
                gemColorCheckBox.IsChecked = enabled;
                gemColorButton.Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
                gemColorButton.IsEnabled = enabled;
                gemColorComboBox.IsEnabled = enabled;
                gemTintCheckBox.IsEnabled = enabled;
                gemTintCheckBox.IsChecked = tintOtherEffects;
                gemColorComboBox.SelectedItem = gemColorComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(item => item.Tag is GemColorChoice choice && choice.Color.ToArgb() == color.ToArgb());
                gemColorValueLabel.Text = $"R:{color.R} G:{color.G} B:{color.B}  #{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            finally
            {
                updatingGemColor = false;
            }
        }

        private void SaveGemColor(SavedLoadout saved)
        {
            saved.GemColor = pickedGemColor?.ToArgb();
            saved.GemColorEnabled = pickedGemColorEnabled;
            saved.GemColorTintsOthers = tintOtherEffects ? null : false;
        }

        private void RestoreGemColor(SavedLoadout saved)
        {
            pickedGemColor = saved.GemColor is { } argb ? DrawingColor.FromArgb(argb) : null;
            pickedGemColorEnabled = saved.GemColorEnabled;
            tintOtherEffects = saved.GemColorTintsOthers ?? true;
        }
    }
}
