using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;

namespace GUI.Forms
{
    sealed partial class CharacterSelectForm
    {
        private readonly List<(ParticleSlot Slot, ComboBox ComboBox)> particleRows = [];
        private readonly HashSet<ParticleSlot> pickedParticleSwaps = [];
        private readonly Grid particlesTable = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        private bool updatingParticleSwaps;

        private TabItem CreateParticlesTab() => new() { Header = "Ability effects", Content = ScrollPage(particlesTable) };

        /// <summary>
        /// Lists the hero's particles that items swap, each with every item's version to pick from, and a quick pick that
        /// takes every effect and sound of one item.
        /// </summary>
        private void BuildParticleRows(HeroDefinition hero)
        {
            particlesTable.Children.Clear();
            particlesTable.RowDefinitions.Clear();
            particleRows.Clear();
            pickedParticleSwaps.Clear();

            var slots = CharacterParticles.GetSlots(catalog, hero, Exists);

            void AddRow(Control? label, Control control)
            {
                var row = particlesTable.RowDefinitions.Count;

                if (label != null)
                {
                    AddCell(particlesTable, label, row, 0);
                    AddCell(particlesTable, control, row, 1);
                }
                else
                {
                    AddCell(particlesTable, control, row, 0, columnSpan: 2);
                }
            }

            static TextBlock CreateLabel(string text, bool bold = false) => new()
            {
                MaxWidth = bold ? double.PositiveInfinity : 170,
                TextWrapping = TextWrapping.Wrap,
                Text = text,
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(3, bold ? 8 : 6, 6, 3),
            };

            SearchableComboBox CreateComboBox()
            {
                var comboBox = new SearchableComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    DropDownWidth = 420,
                    MaxDropDownItems = 20,
                    Margin = new(3),
                };

                comboBox.ItemImage = choice => GetChoiceImage(choice, comboBox);

                return comboBox;
            }

            if (slots.Count == 0)
            {
                AddRow(null, CreateLabel($"No item swaps the effects of {hero.DisplayName}'s abilities."));
                return;
            }

            var sourceComboBox = CreateComboBox();

            foreach (var item in slots.SelectMany(static slot => slot.Choices).SelectMany(static choice => choice.Items).Select(static source => source.Item)
                .Concat(catalog.GetItems(hero).Where(item => item.AssetModifiers.Any(modifier => modifier.Type == "sound"
                    && soundRows.Any(row => row.Slot.Event.Equals(modifier.Asset, StringComparison.OrdinalIgnoreCase)))))
                .Distinct()
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                sourceComboBox.Items.Add(new ItemChoice(item, showDefIndex: false));
            }

            sourceComboBox.SelectionChanged += (_, _) =>
            {
                if (sourceComboBox.SelectedItem is ItemChoice { Item: { } item })
                {
                    UseEffectsAndSoundsOf(item);
                }
            };

            var sourceLabel = CreateLabel("Use all of");
            ToolTip.SetTip(sourceLabel, "Picks every effect and sound below that the item comes with, whether it is worn or not");
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
                ToolTip.SetTip(label, slot.Target);

                var comboBox = CreateComboBox();
                comboBox.Tag = slot;

                foreach (var choice in slot.Choices)
                {
                    comboBox.Items.Add(choice);
                }

                comboBox.SelectionChanged += ParticleComboBox_SelectionChanged;
                AddRow(label, comboBox);
                particleRows.Add((slot, comboBox));
            }
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

        private void ParticleComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
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
