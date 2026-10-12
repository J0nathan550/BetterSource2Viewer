using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;

namespace GUI.Forms
{
    sealed partial class CharacterSelectForm
    {
        /// <summary>Whether each activity modifier was ticked or unticked by hand, by name.</summary>
        private readonly Dictionary<string, bool> pickedAnimations = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The activities each model has sequences for each activity modifier in, see <see cref="CharacterAnimations.GetModifiedActivities"/>.</summary>
        private readonly Dictionary<string, Dictionary<string, List<string>>> modifiedActivities = new(StringComparer.OrdinalIgnoreCase);

        private readonly StackPanel animationsTable = new();
        private List<AnimationChoice> animationChoices = [];
        private bool updatingAnimations;

        private TabItem CreateAnimationsTab() => new() { Header = "Animations", Content = ScrollPage(animationsTable) };

        /// <summary>
        /// Forgets the activity modifiers picked for the previous hero and lists the new hero's.
        /// </summary>
        private void ResetAnimationChoices(HeroDefinition hero)
        {
            pickedAnimations.Clear();
            animationChoices = CharacterAnimations.GetChoices(catalog, hero);
        }

        /// <summary>
        /// The activity modifiers to export with, or null when none was picked by hand and the worn items' apply.
        /// </summary>
        private List<ActivityModifier>? GetPickedActivityModifiers(CharacterLoadout loadout)
            => pickedAnimations.Count > 0 ? CharacterAnimations.Resolve(animationChoices, loadout.WornActivityModifiers, pickedAnimations) : null;

        private Dictionary<string, List<string>> GetModifiedActivities(string model)
        {
            if (!modifiedActivities.TryGetValue(model, out var activities))
            {
                try
                {
                    activities = CharacterAnimations.GetModifiedActivities(guiContext, model);
                }
                catch (Exception e)
                {
                    Log.Warn(nameof(CharacterSelectForm), $"Failed to read the sequences of \"{model}\": {e.Message}");
                    activities = [];
                }

                modifiedActivities[model] = activities;
            }

            return activities;
        }

        /// <summary>
        /// Lists every activity modifier the hero's items set, ticked when the worn items set it unless ticked or unticked
        /// by hand, the ones the hero's model has animations for first.
        /// </summary>
        private void BuildAnimationRows(CharacterLoadout loadout)
        {
            animationsTable.Children.Clear();

            static TextBlock CreateLabel(string text, bool bold = false, double left = 3) => new()
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                Margin = new(left, bold ? 8 : 0, 3, 3),
            };

            animationsTable.Children.Add(CreateLabel("Ticked activity modifiers make the hero play the animations made for them, e.g. a totem's attack, " +
                "whichever items are worn. The worn items' ones are ticked unless picked by hand."));

            if (animationChoices.Count == 0)
            {
                animationsTable.Children.Add(CreateLabel($"No item sets activity modifiers on {loadout.Hero.DisplayName}."));
                return;
            }

            var heroModel = loadout.HeroModel ?? loadout.Hero.Model;
            var activities = heroModel != null ? GetModifiedActivities(heroModel) : [];
            var worn = loadout.WornActivityModifiers.Select(static modifier => modifier.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var modelName = heroModel != null ? Path.GetFileName(heroModel) : "the hero's model";

            updatingAnimations = true;

            try
            {
                foreach (var group in animationChoices.GroupBy(choice => activities.ContainsKey(choice.Name)).OrderByDescending(static group => group.Key))
                {
                    animationsTable.Children.Add(CreateLabel(group.Key ? $"Animations of {modelName}" : $"Nothing on {modelName} plays these", bold: true));

                    foreach (var choice in group)
                    {
                        var isWorn = worn.Contains(choice.Name);
                        var items = choice.Items.Select(static item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var itemsText = items.Count > 2 ? $"{string.Join(", ", items.Take(2))} and {items.Count - 2} more" : string.Join(", ", items);

                        var checkBox = new CheckBox
                        {
                            Content = isWorn ? $"{choice.Name} (worn)" : choice.Name,
                            IsChecked = pickedAnimations.TryGetValue(choice.Name, out var ticked) ? ticked : isWorn,
                            Tag = choice,
                            Margin = new(12, 3, 3, 0),
                        };

                        checkBox.IsCheckedChanged += AnimationCheckBox_CheckedChanged;
                        ToolTip.SetTip(checkBox, string.Join("\n", items));
                        animationsTable.Children.Add(checkBox);

                        var text = activities.TryGetValue(choice.Name, out var modified)
                            ? $"{string.Join(", ", modified.Select(CharacterAnimations.GetActivityText))} - {itemsText}"
                            : itemsText;

                        var label = CreateLabel(text, left: 40);
                        label.Opacity = 0.75;
                        animationsTable.Children.Add(label);
                    }
                }
            }
            finally
            {
                updatingAnimations = false;
            }
        }

        private void AnimationCheckBox_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (updatingAnimations || sender is not CheckBox { Tag: AnimationChoice choice } checkBox || SelectedHero == null)
            {
                return;
            }

            var ticked = checkBox.IsChecked == true;
            var worn = CharacterLoadout.Create(catalog, SelectedHero, GetEquippedItems()).WornActivityModifiers
                .Any(modifier => modifier.Name.Equals(choice.Name, StringComparison.OrdinalIgnoreCase));

            // Back to what the worn items set, it follows them again
            if (ticked == worn)
            {
                pickedAnimations.Remove(choice.Name);
            }
            else
            {
                pickedAnimations[choice.Name] = ticked;
            }
        }
    }
}
