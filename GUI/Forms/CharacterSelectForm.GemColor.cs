using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;

namespace GUI.Forms
{
    partial class CharacterSelectForm
    {
        // Picked by hand, null follows the equipped items: their own color, and on when they have one
        private Color? pickedGemColor;
        private bool? pickedGemColorEnabled;
        private bool tintOtherEffects = true;

        private GemColorChoice? itemGemColor;
        private bool updatingGemColor;
        private readonly Dictionary<Color, Image> gemColorSwatches = [];

#pragma warning disable CA2213 // Disposed with the tab control they are added to
        private CheckBox? gemColorCheckBox;
        private CheckBox? gemTintCheckBox;
        private ThemedButton? gemColorButton;
        private SearchableComboBox? gemColorComboBox;
        private Label? gemColorValueLabel;
#pragma warning restore CA2213

        /// <summary>The color the effects get, see <see cref="CharacterExportOptions.GemColor"/>, or null to leave them as they are.</summary>
        private Color? GetGemColor() => (pickedGemColorEnabled ?? itemGemColor != null) ? CurrentGemColor : null;

        private Color CurrentGemColor => pickedGemColor ?? itemGemColor?.Color ?? (catalog.GemColors.Count > 0 ? catalog.GemColors[0].Choice.Color : Color.Red);

        private bool IsGemColorPicked => pickedGemColor != null || pickedGemColorEnabled != null || !tintOtherEffects;

        /// <summary>See <see cref="CharacterExportOptions.TintOtherEffects"/>.</summary>
        private bool GetTintOtherEffects() => tintOtherEffects && GetGemColor() != null;

        private void CreateGemColorTab()
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var intro = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(this.AdjustForDPI(480), 0),
                Margin = new Padding(3, 6, 3, 6),
                Text = "Prismatic gems color the effects and materials of some items, and some arcanas have a color of their own without one: " +
                    "Terrorblade's is red and Techies' orange. The game only gives them that color with the item equipped, so it is written into " +
                    "every particle and material that takes it, the hero's own ability effects included, e.g. Rictus Ripper's eye.",
            };

            gemColorCheckBox = new CheckBox { AutoSize = true, Text = "Write the color into the effects and materials", Margin = new Padding(3, 3, 3, 6) };
            gemColorCheckBox.CheckedChanged += (_, _) =>
            {
                if (updatingGemColor)
                {
                    return;
                }

                // Back to what the equipped items do, it follows them again
                pickedGemColorEnabled = gemColorCheckBox.Checked == (itemGemColor != null) ? null : gemColorCheckBox.Checked;
                UpdateGemColorControls();
            };

            gemColorButton = new ThemedButton
            {
                Style = false,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(this.AdjustForDPI(64), this.AdjustForDPI(24)),
                Margin = new Padding(3, 3, 6, 3),
            };
            gemColorButton.Click += (_, _) =>
            {
                using var picker = new BetterColorPicker(CurrentGemColor, static _ => { });

                if (picker.ShowDialog(this) == DialogResult.OK)
                {
                    PickGemColor(Color.FromArgb(255, picker.PickedColor));
                }
            };
            toolTip.SetToolTip(gemColorButton, "Pick any color");

            gemColorComboBox = new SearchableComboBox
            {
                Width = this.AdjustForDPI(240),
                DropDownWidth = this.AdjustForDPI(280),
                MaxDropDownItems = 20,
                Margin = new Padding(3, 3, 6, 3),
                ImageSize = new Size(20, 14),
            };
            gemColorComboBox.ItemImage = choice => choice is GemColorChoice gem ? GetGemColorSwatch(gem.Color) : null;
            gemColorComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (!updatingGemColor && gemColorComboBox.SelectedItem is GemColorChoice choice)
                {
                    PickGemColor(choice.Color);
                }
            };
            toolTip.SetToolTip(gemColorComboBox, "The colors prismatic gems come in");

            gemColorValueLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };

            gemTintCheckBox = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Text = "Also tint the hero's effects that take no gem color, e.g. Metamorphosis",
                Margin = new Padding(12, 6, 3, 3),
            };
            gemTintCheckBox.CheckedChanged += (_, _) =>
            {
                if (!updatingGemColor)
                {
                    tintOtherEffects = gemTintCheckBox.Checked;
                }
            };
            toolTip.SetToolTip(gemTintCheckBox, "Gives their colors the picked color's hue, white, grey and black stay as they are. " +
                "The game shows them in their own colors even with the arcana.");

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(12, 0, 3, 3) };
            row.Controls.AddRange([gemColorButton, gemColorComboBox, gemColorValueLabel]);

            table.Controls.Add(intro, 0, 0);
            table.Controls.Add(gemColorCheckBox, 0, 1);
            table.Controls.Add(row, 0, 2);
            table.Controls.Add(gemTintCheckBox, 0, 3);

            var panel = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
            panel.Controls.Add(table);

#pragma warning disable CA2000 // Disposed with the tab control it is added to
            var tabPage = new ThemedTabPage { Text = "Prismatic color" };
#pragma warning restore CA2000
            tabPage.Controls.Add(panel);
            loadoutTabControl.TabPages.Insert(loadoutTabControl.TabPages.IndexOf(effectsTabPage), tabPage);

            Themer.ThemeControl(tabPage);
            Disposed += (_, _) => DisposeGemColorSwatches();
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
            if (gemColorComboBox == null)
            {
                return;
            }

            itemGemColor = CharacterGemColor.GetItemColor(catalog, loadout.Items);

            gemColorComboBox.BeginUpdate();
            gemColorComboBox.Items.Clear();

            if (itemGemColor != null)
            {
                gemColorComboBox.Items.Add(itemGemColor with { Name = $"{itemGemColor.Name} (the item's own color)" });
            }

            foreach (var (_, choice) in catalog.GemColors)
            {
                gemColorComboBox.Items.Add(choice);
            }

            gemColorComboBox.EndUpdate();

            UpdateGemColorControls();
        }

        private void PickGemColor(Color color)
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
            if (gemColorCheckBox == null || gemColorButton == null || gemColorComboBox == null || gemColorValueLabel == null || gemTintCheckBox == null)
            {
                return;
            }

            var color = CurrentGemColor;
            var enabled = GetGemColor() != null;

            updatingGemColor = true;

            try
            {
                gemColorCheckBox.Checked = enabled;
                gemColorButton.BackColor = color;
                gemColorButton.Enabled = enabled;
                gemColorComboBox.Enabled = enabled;
                gemTintCheckBox.Enabled = enabled;
                gemTintCheckBox.Checked = tintOtherEffects;
                gemColorComboBox.SelectedItem = gemColorComboBox.Items.OfType<GemColorChoice>().FirstOrDefault(choice => choice.Color.ToArgb() == color.ToArgb());
                gemColorValueLabel.Text = $"R:{color.R} G:{color.G} B:{color.B}  #{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            finally
            {
                updatingGemColor = false;
            }
        }

        private Image GetGemColorSwatch(Color color)
        {
            if (!gemColorSwatches.TryGetValue(color, out var swatch))
            {
                var size = gemColorComboBox?.ScaledImageSize ?? new Size(16, 16);
                var bitmap = new Bitmap(size.Width, size.Height);

                using (var graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(color))
                {
                    graphics.FillRectangle(brush, 0, 0, size.Width, size.Height);
                }

                swatch = bitmap;
                gemColorSwatches[color] = swatch;
            }

            return swatch;
        }

        private void SaveGemColor(SavedLoadout saved)
        {
            saved.GemColor = pickedGemColor?.ToArgb();
            saved.GemColorEnabled = pickedGemColorEnabled;
            saved.GemColorTintsOthers = tintOtherEffects ? null : false;
        }

        private void RestoreGemColor(SavedLoadout saved)
        {
            pickedGemColor = saved.GemColor is { } argb ? Color.FromArgb(argb) : null;
            pickedGemColorEnabled = saved.GemColorEnabled;
            tintOtherEffects = saved.GemColorTintsOthers ?? true;
        }

        private void DisposeGemColorSwatches()
        {
            foreach (var swatch in gemColorSwatches.Values)
            {
                swatch.Dispose();
            }

            gemColorSwatches.Clear();
        }
    }
}
