using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;

namespace GUI.Forms
{
    partial class CharacterSelectForm
    {
        /// <summary>Whether each activity modifier was ticked or unticked by hand, by name.</summary>
        private readonly Dictionary<string, bool> pickedAnimations = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The activities each model has sequences for each activity modifier in, see <see cref="CharacterAnimations.GetModifiedActivities"/>.</summary>
        private readonly Dictionary<string, Dictionary<string, List<string>>> modifiedActivities = new(StringComparer.OrdinalIgnoreCase);

        private List<AnimationChoice> animationChoices = [];
#pragma warning disable CA2213 // Disposed with the tab control it is added to
        private TableLayoutPanel? animationsTable;
#pragma warning restore CA2213
        private bool updatingAnimations;

        private void CreateAnimationsTab()
        {
            animationsTable = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Top,
            };
            animationsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var panel = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
            panel.Controls.Add(animationsTable);

#pragma warning disable CA2000 // Disposed with the tab control it is added to
            var tabPage = new ThemedTabPage { Text = "Animations" };
#pragma warning restore CA2000
            tabPage.Controls.Add(panel);
            loadoutTabControl.TabPages.Insert(loadoutTabControl.TabPages.IndexOf(effectsTabPage), tabPage);

            Themer.ThemeControl(tabPage);
        }

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
            if (animationsTable == null)
            {
                return;
            }

            animationsTable.SuspendLayout();

            foreach (var control in animationsTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            animationsTable.Controls.Clear();
            animationsTable.RowStyles.Clear();
            animationsTable.RowCount = 0;

            void AddRow(Control control)
            {
                var row = animationsTable.RowCount++;
                animationsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                animationsTable.Controls.Add(control, 0, row);
            }

            Label CreateLabel(string text, bool bold = false, int left = 3) => new()
            {
                AutoSize = true,
                MaximumSize = new Size(Math.Max(this.AdjustForDPI(200), animationsTable.Parent?.ClientSize.Width - this.AdjustForDPI(30) ?? 0), 0),
                Text = text,
                Font = bold ? groupFont ??= new Font(Font, FontStyle.Bold) : Font,
                Margin = new Padding(left, bold ? 8 : 0, 3, 3),
            };

            AddRow(CreateLabel("Ticked activity modifiers make the hero play the animations made for them, e.g. a totem's attack, whichever items are worn. " +
                "The worn items' ones are ticked unless picked by hand."));

            if (animationChoices.Count == 0)
            {
                AddRow(CreateLabel($"No item sets activity modifiers on {loadout.Hero.DisplayName}.", left: 3));
                animationsTable.ResumeLayout(true);
                return;
            }

            var heroModel = loadout.HeroModel ?? loadout.Hero.Model;
            var activities = heroModel != null ? GetModifiedActivities(heroModel) : [];
            var worn = loadout.WornActivityModifiers.Select(static modifier => modifier.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var modelName = heroModel != null ? Path.GetFileName(heroModel) : "the hero's model";
            var details = new List<Label>();

            updatingAnimations = true;

            try
            {
                foreach (var group in animationChoices.GroupBy(choice => activities.ContainsKey(choice.Name)).OrderByDescending(static group => group.Key))
                {
                    AddRow(CreateLabel(group.Key ? $"Animations of {modelName}" : $"Nothing on {modelName} plays these", bold: true));

                    foreach (var choice in group)
                    {
                        var isWorn = worn.Contains(choice.Name);
                        var items = choice.Items.Select(static item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        var itemsText = items.Count > 2 ? $"{string.Join(", ", items.Take(2))} and {items.Count - 2} more" : string.Join(", ", items);

                        var checkBox = new CheckBox
                        {
                            AutoSize = true,
                            Text = isWorn ? $"{choice.Name} (worn)" : choice.Name,
                            Checked = pickedAnimations.TryGetValue(choice.Name, out var ticked) ? ticked : isWorn,
                            Tag = choice,
                            Margin = new Padding(12, 3, 3, 0),
                        };

                        checkBox.CheckedChanged += AnimationCheckBox_CheckedChanged;
                        toolTip.SetToolTip(checkBox, string.Join("\n", items));
                        AddRow(checkBox);

                        var text = activities.TryGetValue(choice.Name, out var modified)
                            ? $"{string.Join(", ", modified.Select(CharacterAnimations.GetActivityText))} - {itemsText}"
                            : itemsText;

                        var label = CreateLabel(text, left: this.AdjustForDPI(32));
                        details.Add(label);
                        AddRow(label);
                    }
                }
            }
            finally
            {
                updatingAnimations = false;
            }

            Themer.ThemeControl(animationsTable);

            // Theming sets every label's colour, the details are dimmer than the names
            foreach (var label in details)
            {
                label.ForeColor = Themer.CurrentThemeColors.ContrastSoft;
            }

            animationsTable.ResumeLayout(true);
        }

        private void AnimationCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (updatingAnimations || sender is not CheckBox { Tag: AnimationChoice choice } checkBox || SelectedHero == null)
            {
                return;
            }

            var worn = CharacterLoadout.Create(catalog, SelectedHero, GetEquippedItems()).WornActivityModifiers
                .Any(modifier => modifier.Name.Equals(choice.Name, StringComparison.OrdinalIgnoreCase));

            // Back to what the worn items set, it follows them again
            if (checkBox.Checked == worn)
            {
                pickedAnimations.Remove(choice.Name);
            }
            else
            {
                pickedAnimations[choice.Name] = checkBox.Checked;
            }
        }
    }
}
