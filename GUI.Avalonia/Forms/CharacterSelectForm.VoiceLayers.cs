using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Forms
{
    sealed partial class CharacterSelectForm
    {
        /// <summary>The voice lines picked by hand for each <see cref="VoiceLayer"/>, by its sound event.</summary>
        private readonly Dictionary<string, HashSet<string>> pickedVoiceLayerLines = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The lines each layer offers for the voice the rows were built for, by its sound event.</summary>
        private readonly Dictionary<string, List<string>> voiceLayerChoices = new(StringComparer.OrdinalIgnoreCase);

        private readonly List<(VoiceLayer Layer, string Line, CheckBox CheckBox)> voiceLayerRows = [];

        private StackPanel? voiceLayersTable;
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

            voiceLayersTable = new StackPanel();
            AddCell(soundsTable, voiceLayersTable, soundsTable.RowDefinitions.Count, 0, columnSpan: 2);
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

            var voice = voiceComboBox?.SelectedItem as VoiceChoice;
            var criteria = voice?.Criteria;

            if (voiceLayerRowsBuilt && string.Equals(criteria, voiceLayerCriteria, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            voiceLayerCriteria = criteria;
            voiceLayerRowsBuilt = true;

            voiceLayersTable.Children.Clear();
            voiceLayerRows.Clear();
            voiceLayerChoices.Clear();

            static TextBlock CreateLabel(string text, bool bold, double left) => new()
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
                Margin = new(left, bold ? 8 : 4, 3, 3),
            };

            var voiceLines = criteria != null
                ? voiceLayerRules.MapVoice(criteria, hero.ChatWheel, voice?.ChatWheelSwaps ?? new Dictionary<string, string>()).Lines
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
                    ToolTip.SetTip(header, $"The game says nothing here, so {layer.SoundEvent} speaks one of the ticked lines at random.\nUntick every line to leave it silent.");
                    voiceLayersTable.Children.Add(header);

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

                        voiceLayersTable.Children.Add(CreateLabel($"{situation.DisplayName} ({lines.Count})", bold: false, left: 12));

                        foreach (var line in lines)
                        {
                            offered.Add(line);

                            var playButton = new Button
                            {
                                Content = "▶",
                                Width = 28,
                                Margin = new(20, 1, 3, 1),
                                Tag = line,
                                HorizontalContentAlignment = HorizontalAlignment.Center,
                            };

                            playButton.Click += VoiceLinePlayButton_Click;
                            ToolTip.SetTip(playButton, "Listen to the line");

                            var checkBox = new CheckBox
                            {
                                Content = PlainText(GetVoiceLineText(hero, line)),
                                IsChecked = picked?.Contains(line) ?? defaults.Contains(line),
                                Tag = layer,
                                Margin = new(3, 1, 3, 1),
                            };

                            checkBox.IsCheckedChanged += VoiceLayerCheckBox_CheckedChanged;
                            ToolTip.SetTip(checkBox, line);

                            voiceLayersTable.Children.Add(new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Children = { playButton, checkBox },
                            });

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
        }

        // e.g. "earth_arcana_ability_echo_02_02" for "earthshaker_earth_arcana_ability_echo_02_02"
        private static string GetVoiceLineText(HeroDefinition hero, string line)
            => line.StartsWith(hero.ShortName + "_", StringComparison.OrdinalIgnoreCase) ? line[(hero.ShortName.Length + 1)..] : line;

        private void VoiceLayerCheckBox_CheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (updatingVoiceLayers || sender is not CheckBox { Tag: VoiceLayer layer })
            {
                return;
            }

            pickedVoiceLayerLines[layer.SoundEvent] = voiceLayerRows
                .Where(row => row.Layer == layer && row.CheckBox.IsChecked == true)
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

        private async void VoiceLinePlayButton_Click(object? sender, RoutedEventArgs e)
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
                resource.Read(package.GetMemoryMappedStreamIfPossible(entry));

                if (AudioPlayer.CreateWaveStream(resource) is not { } audio)
                {
                    return;
                }

                var panel = new AudioPlaybackPanel(audio.Stream, autoPlay: true, (0, 0));
                var preview = new Window
                {
                    Title = line,
                    Width = 520,
                    Height = 170,
                    Content = panel,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                };

                preview.Closed += (_, _) => panel.Dispose();
                preview.Show(window);
            }
            catch (Exception exception)
            {
                Log.Warn(nameof(CharacterSelectForm), $"Failed to play \"{sound}\": {exception.Message}");
            }
        }
    }
}
