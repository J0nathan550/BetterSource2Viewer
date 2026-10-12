using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Forms
{
    partial class CharacterSelectForm
    {
        /// <summary>The voice lines picked by hand for each <see cref="VoiceLayer"/>, by its sound event.</summary>
        private readonly Dictionary<string, HashSet<string>> pickedVoiceLayerLines = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The lines each layer offers for the voice the rows were built for, by its sound event.</summary>
        private readonly Dictionary<string, List<string>> voiceLayerChoices = new(StringComparer.OrdinalIgnoreCase);

        private readonly List<(VoiceLayer Layer, string Line, CheckBox CheckBox)> voiceLayerRows = [];

#pragma warning disable CA2213 // Disposed with the sounds table it is added to
        private TableLayoutPanel? voiceLayersTable;
#pragma warning restore CA2213

        private HeroResponseRules? voiceLayerRules;
        private Dictionary<string, List<string>>? voiceLineSounds;
        private string? voiceLayerCriteria;
        private bool voiceLayerRowsBuilt;
        private bool updatingVoiceLayers;

        /// <summary>
        /// Adds the place for the hero's <see cref="VoiceLayers"/> under its sounds, filled in by <see cref="UpdateVoiceLayerRows"/>.
        /// </summary>
        private void BuildVoiceLayerRows(HeroDefinition hero)
        {
            pickedVoiceLayerLines.Clear();
            voiceLayerChoices.Clear();
            voiceLayerRows.Clear();
            voiceLayersTable = null;
            voiceLayerRowsBuilt = false;
            voiceLineSounds = null;

            if (!VoiceLayers.All.Any(layer => layer.Hero == hero.Name) || (voiceLayerRules = HeroResponseRules.Load(package, hero)) == null)
            {
                return;
            }

            voiceLayersTable = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            voiceLayersTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            voiceLayersTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            soundsTable.SuspendLayout();
            var row = soundsTable.RowCount++;
            soundsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            soundsTable.Controls.Add(voiceLayersTable, 0, row);
            soundsTable.SetColumnSpan(voiceLayersTable, 2);
            soundsTable.ResumeLayout(true);
        }

        /// <summary>
        /// Lists the lines of the voice the hero speaks with that each of its <see cref="VoiceLayers"/> can speak, ticked
        /// as picked by hand or else when spoken by default.
        /// </summary>
        private void UpdateVoiceLayerRows()
        {
            if (voiceLayersTable == null || voiceLayerRules == null || SelectedHero is not { } hero)
            {
                return;
            }

            var criteria = (voiceComboBox?.SelectedItem as VoiceChoice)?.Criteria;

            if (voiceLayerRowsBuilt && string.Equals(criteria, voiceLayerCriteria, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            voiceLayerCriteria = criteria;
            voiceLayerRowsBuilt = true;

            voiceLayersTable.SuspendLayout();

            foreach (var control in voiceLayersTable.Controls.Cast<Control>().ToList())
            {
                control.Dispose();
            }

            voiceLayersTable.Controls.Clear();
            voiceLayersTable.RowStyles.Clear();
            voiceLayersTable.RowCount = 0;
            voiceLayerRows.Clear();
            voiceLayerChoices.Clear();

            void AddRow(Control? first, Control second)
            {
                var row = voiceLayersTable.RowCount++;
                voiceLayersTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                if (first != null)
                {
                    voiceLayersTable.Controls.Add(first, 0, row);
                    voiceLayersTable.Controls.Add(second, 1, row);
                }
                else
                {
                    voiceLayersTable.Controls.Add(second, 0, row);
                    voiceLayersTable.SetColumnSpan(second, 2);
                }
            }

            Label CreateLabel(string text, bool bold, int left) => new()
            {
                AutoSize = true,
                Text = text,
                Font = bold ? groupFont ??= new Font(Font, FontStyle.Bold) : Font,
                Margin = new Padding(left, bold ? 8 : 4, 3, 3),
            };

            var voiceLines = criteria != null
                ? voiceLayerRules.MapVoice(criteria, hero.ChatWheel, (voiceComboBox?.SelectedItem as VoiceChoice)?.ChatWheelSwaps ?? new Dictionary<string, string>()).Lines
                : new Dictionary<string, string>();

            updatingVoiceLayers = true;

            try
            {
                foreach (var layer in VoiceLayers.All.Where(layer => layer.Hero == hero.Name))
                {
                    var defaults = VoiceLayers.GetDefaultLines(voiceLayerRules, layer, criteria, voiceLines).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var picked = pickedVoiceLayerLines.GetValueOrDefault(layer.SoundEvent);
                    var offered = new List<string>();

                    var header = CreateLabel($"Voice lines: {layer.DisplayName}", bold: true, left: 3);
                    toolTip.SetToolTip(header, $"The game says nothing here, so {layer.SoundEvent} speaks one of the ticked lines at random.\nUntick every line to leave it silent.");
                    AddRow(null, header);

                    foreach (var situation in layer.Situations)
                    {
                        var lines = voiceLayerRules.GetLines(situation.Requirement, criteria);

                        if (lines.Count == 0)
                        {
                            lines = [.. voiceLayerRules.GetLines(situation.Requirement, null).Select(line => voiceLines.GetValueOrDefault(line) ?? line)];
                        }

                        lines = [.. lines.Where(line => !offered.Contains(line, StringComparer.OrdinalIgnoreCase))];

                        if (lines.Count == 0)
                        {
                            continue;
                        }

                        AddRow(null, CreateLabel($"{situation.DisplayName} ({lines.Count})", bold: false, left: this.AdjustForDPI(12)));

                        foreach (var line in lines)
                        {
                            offered.Add(line);

                            var playButton = new Button
                            {
                                Text = "▶",
                                Width = this.AdjustForDPI(28),
                                Height = this.AdjustForDPI(24),
                                FlatStyle = FlatStyle.Flat,
                                Margin = new Padding(this.AdjustForDPI(20), 1, 3, 1),
                                Tag = line,
                            };

                            playButton.Click += VoiceLinePlayButton_Click;
                            toolTip.SetToolTip(playButton, "Listen to the line");

                            var checkBox = new CheckBox
                            {
                                AutoSize = true,
                                Text = GetVoiceLineText(hero, line),
                                Checked = picked?.Contains(line) ?? defaults.Contains(line),
                                Tag = layer,
                                Margin = new Padding(3, 4, 3, 1),
                            };

                            checkBox.CheckedChanged += VoiceLayerCheckBox_CheckedChanged;
                            toolTip.SetToolTip(checkBox, line);
                            AddRow(playButton, checkBox);
                            voiceLayerRows.Add((layer, line, checkBox));
                        }
                    }

                    voiceLayerChoices[layer.SoundEvent] = offered;
                }
            }
            finally
            {
                updatingVoiceLayers = false;
            }

            Themer.ThemeControl(voiceLayersTable);
            voiceLayersTable.ResumeLayout(true);
        }

        // e.g. "earth_arcana_ability_echo_02_02" for "earthshaker_earth_arcana_ability_echo_02_02"
        private static string GetVoiceLineText(HeroDefinition hero, string line)
            => line.StartsWith(hero.ShortName + "_", StringComparison.OrdinalIgnoreCase) ? line[(hero.ShortName.Length + 1)..] : line;

        private void VoiceLayerCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (updatingVoiceLayers || sender is not CheckBox { Tag: VoiceLayer layer })
            {
                return;
            }

            pickedVoiceLayerLines[layer.SoundEvent] = voiceLayerRows
                .Where(row => row.Layer == layer && row.CheckBox.Checked)
                .Select(static row => row.Line)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The lines picked for each layer, as the export takes them, only the ones offered for the voice spoken with.
        /// </summary>
        private Dictionary<string, IReadOnlyList<string>> GetVoiceLayerLines() => pickedVoiceLayerLines
            .Where(pair => voiceLayerChoices.ContainsKey(pair.Key))
            .ToDictionary(
                static pair => pair.Key,
                pair => (IReadOnlyList<string>)[.. voiceLayerChoices[pair.Key].Where(pair.Value.Contains)],
                StringComparer.OrdinalIgnoreCase);

        private void SaveVoiceLayerLines(SavedLoadout saved)
        {
            foreach (var (soundEvent, lines) in pickedVoiceLayerLines)
            {
                saved.VoiceLayerLines[soundEvent] = [.. lines];
            }
        }

        private void RestoreVoiceLayerLines(SavedLoadout saved)
        {
            foreach (var (soundEvent, lines) in saved.VoiceLayerLines ?? [])
            {
                pickedVoiceLayerLines[soundEvent] = new HashSet<string>(lines, StringComparer.OrdinalIgnoreCase);
            }

            voiceLayerRowsBuilt = false;
        }

        private async void VoiceLinePlayButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string line } || SelectedHero?.VoiceFile is not { } voiceFile)
            {
                return;
            }

            voiceLineSounds ??= VoiceLayers.ReadEventSounds(package, voiceFile);

            if (voiceLineSounds.GetValueOrDefault(line)?.FirstOrDefault() is not { } sound
                || package.FindEntry(CharacterLoadout.NormalizePath(sound) + GameFileLoader.CompiledFileSuffix) is not { } entry)
            {
                await AppMessageDialogs.ShowMessageAsync($"\"{line}\" plays no sound the package has.", "Voice line").ConfigureAwait(true);
                return;
            }

            try
            {
                using var resource = new Resource { FileName = entry.GetFullPath() };
                resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                if (AudioPlayer.CreateWaveStream(resource) is not { } audio)
                {
                    return;
                }

#pragma warning disable CA2000 // Disposes itself once closed
                var preview = new Form
                {
                    Text = line,
                    Size = new Size(this.AdjustForDPI(520), this.AdjustForDPI(170)),
                    StartPosition = FormStartPosition.CenterParent,
                    ShowInTaskbar = false,
                    FormBorderStyle = FormBorderStyle.SizableToolWindow,
                };
#pragma warning restore CA2000

                // The panel disposes the stream, and a window opened with Show disposes itself once closed
#pragma warning disable CA2000 // Disposed with the preview it is added to
                preview.Controls.Add(new AudioPlaybackPanel(audio.Stream, autoPlay: true, (0, 0)));
#pragma warning restore CA2000
                Themer.ThemeControl(preview);
                preview.Show(this);
            }
            catch (Exception exception)
            {
                Log.Warn(nameof(CharacterSelectForm), $"Failed to play \"{sound}\": {exception.Message}");
            }
        }
    }
}
