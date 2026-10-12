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
        private readonly List<(ParticleSlot Slot, ComboBox ComboBox)> particleRows = [];
        private readonly HashSet<ParticleSlot> pickedParticleSwaps = [];

#pragma warning disable CA2213 // Disposed with the tab control it is added to
        private TableLayoutPanel? particlesTable;
#pragma warning restore CA2213

        private bool updatingParticleSwaps;

        private void CreateParticlesTab()
        {
            particlesTable = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
            };
            particlesTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            particlesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var panel = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
            panel.Controls.Add(particlesTable);

#pragma warning disable CA2000 // Disposed with the tab control it is added to
            var tabPage = new ThemedTabPage { Text = "Ability effects" };
#pragma warning restore CA2000
            tabPage.Controls.Add(panel);
            loadoutTabControl.TabPages.Insert(loadoutTabControl.TabPages.IndexOf(effectsTabPage), tabPage);

            Themer.ThemeControl(tabPage);
        }

        /// <summary>
        /// Lists the hero's particles that items swap, each with every item's version to pick from, and a quick pick that
        /// takes every effect and sound of one item.
        /// </summary>
        private void BuildParticleRows(HeroDefinition hero)
        {
            if (particlesTable == null)
            {
                return;
            }

            particlesTable.SuspendLayout();

            foreach (var control in particlesTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            particlesTable.Controls.Clear();
            particlesTable.RowStyles.Clear();
            particlesTable.RowCount = 0;
            particleRows.Clear();
            pickedParticleSwaps.Clear();

            var slots = CharacterParticles.GetSlots(catalog, hero, Exists);

            void AddRow(Control? label, Control control)
            {
                var row = particlesTable.RowCount++;
                particlesTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                if (label != null)
                {
                    particlesTable.Controls.Add(label, 0, row);
                    particlesTable.Controls.Add(control, 1, row);
                }
                else
                {
                    particlesTable.Controls.Add(control, 0, row);
                    particlesTable.SetColumnSpan(control, 2);
                }
            }

            Label CreateLabel(string text, bool bold = false) => new()
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                MaximumSize = new Size(bold ? 0 : this.AdjustForDPI(170), 0),
                Text = text,
                Font = bold ? groupFont ??= new Font(Font, FontStyle.Bold) : Font,
                Margin = new Padding(3, bold ? 8 : 6, 6, 3),
            };

            if (slots.Count == 0)
            {
                AddRow(null, CreateLabel($"No item swaps the effects of {hero.DisplayName}'s abilities."));
                particlesTable.ResumeLayout(true);
                return;
            }

            var sourceComboBox = new SearchableComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownWidth = this.AdjustForDPI(420),
                MaxDropDownItems = 20,
            };

            sourceComboBox.ItemImage = choice => GetChoiceImage(choice, sourceComboBox.ScaledImageSize);

            foreach (var item in slots.SelectMany(static slot => slot.Choices).SelectMany(static choice => choice.Items).Select(static source => source.Item)
                .Concat(catalog.GetItems(hero).Where(item => item.AssetModifiers.Any(modifier => modifier.Type == "sound"
                    && soundRows.Any(row => row.Slot.Event.Equals(modifier.Asset, StringComparison.OrdinalIgnoreCase)))))
                .Distinct()
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                sourceComboBox.Items.Add(new ItemChoice(item, showDefIndex: false));
            }

            sourceComboBox.SelectedIndexChanged += (_, _) =>
            {
                if (sourceComboBox.SelectedItem is ItemChoice { Item: { } item })
                {
                    UseEffectsAndSoundsOf(item);
                }
            };

            var sourceLabel = CreateLabel("Use all of");
            toolTip.SetToolTip(sourceLabel, "Picks every effect and sound below that the item comes with, whether it is worn or not");
            AddRow(sourceLabel, sourceComboBox);

            string? group = null;

            foreach (var slot in slots)
            {
                if (slot.Group != group)
                {
                    group = slot.Group;
                    AddRow(null, CreateLabel(group, bold: true));
                }

                var label = CreateLabel(slot.DisplayName);
                toolTip.SetToolTip(label, slot.Target);

                var comboBox = new SearchableComboBox
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    DropDownWidth = this.AdjustForDPI(420),
                    MaxDropDownItems = 20,
                    Tag = slot,
                };

                comboBox.ItemImage = choice => GetChoiceImage(choice, comboBox.ScaledImageSize);

                foreach (var choice in slot.Choices)
                {
                    comboBox.Items.Add(choice);
                }

                comboBox.SelectedIndexChanged += ParticleComboBox_SelectedIndexChanged;
                AddRow(label, comboBox);
                particleRows.Add((slot, comboBox));
            }

            Themer.ThemeControl(particlesTable);
            particlesTable.ResumeLayout(true);
        }

        /// <summary>
        /// Picks the item's version of every effect and sound it swaps, its first style's where they differ by style.
        /// </summary>
        private void UseEffectsAndSoundsOf(EconItem item)
        {
            foreach (var (_, comboBox) in particleRows)
            {
                if (comboBox.Items.OfType<ParticleSwapChoice>()
                    .Where(choice => choice.Items.Any(source => source.Item == item))
                    .OrderBy(choice => choice.Items.First(source => source.Item == item).Style ?? -1)
                    .FirstOrDefault() is { } choice)
                {
                    comboBox.SelectedItem = choice;
                }
            }

            foreach (var (slot, comboBox) in soundRows)
            {
                var sound = item.AssetModifiers
                    .Where(modifier => modifier.Type == "sound" && slot.Event.Equals(modifier.Asset, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(static modifier => modifier.Style ?? -1)
                    .FirstOrDefault()?.Modifier;

                if (sound != null && slot.Choices.FirstOrDefault(choice => choice.Event.Equals(sound, StringComparison.OrdinalIgnoreCase)) is { } choice)
                {
                    comboBox.SelectedItem = choice;
                }
            }
        }

        /// <summary>
        /// Shows the versions the worn items play, except where one was picked by hand.
        /// </summary>
        private void UpdateParticleChoices(CharacterLoadout loadout)
        {
            updatingParticleSwaps = true;

            try
            {
                foreach (var (slot, comboBox) in particleRows)
                {
                    if (!pickedParticleSwaps.Contains(slot) || comboBox.SelectedIndex < 0)
                    {
                        comboBox.SelectedItem = CharacterParticles.GetWornChoice(loadout, slot);
                    }
                }
            }
            finally
            {
                updatingParticleSwaps = false;
            }
        }

        private void ParticleComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!updatingParticleSwaps && sender is ComboBox { Tag: ParticleSlot slot })
            {
                pickedParticleSwaps.Add(slot);
            }
        }

        private List<ParticleReplacement> GetParticleSwaps()
        {
            if (pickedParticleSwaps.Count == 0 || SelectedHero == null)
            {
                return [];
            }

            var loadout = CreateLoadout();

            return [.. particleRows
                .Where(row => pickedParticleSwaps.Contains(row.Slot) && row.ComboBox.SelectedItem is ParticleSwapChoice)
                .Select(row => CharacterParticles.GetReplacement(loadout, row.Slot, (ParticleSwapChoice)row.ComboBox.SelectedItem!))];
        }

        private void SaveParticleSwaps(SavedLoadout saved)
        {
            foreach (var (slot, comboBox) in particleRows)
            {
                if (pickedParticleSwaps.Contains(slot) && comboBox.SelectedItem is ParticleSwapChoice choice)
                {
                    saved.ParticleSwaps[slot.Target] = choice.Particle;
                }
            }
        }

        private void RestoreParticleSwaps(SavedLoadout saved)
        {
            foreach (var (slot, comboBox) in particleRows)
            {
                if (saved.ParticleSwaps?.TryGetValue(slot.Target, out var particle) == true
                    && slot.Choices.FirstOrDefault(choice => choice.Particle.Equals(particle, StringComparison.OrdinalIgnoreCase)) is { } choice)
                {
                    comboBox.SelectedItem = choice;
                    pickedParticleSwaps.Add(slot);
                }
            }
        }
    }
}
