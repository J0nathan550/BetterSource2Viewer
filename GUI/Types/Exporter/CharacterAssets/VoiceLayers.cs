using System.Globalization;
using System.Linq;
using GUI.Utils;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// A situation of the hero's response rules whose lines a <see cref="VoiceLayer"/> can speak.
    /// </summary>
    /// <param name="Requirement">The criteria of the rules, e.g. "IsMegaEchoSlam".</param>
    /// <param name="DisplayName">What it is, e.g. "Mega slam".</param>
    sealed record VoiceSituation(string Requirement, string DisplayName);

    /// <summary>
    /// A sound the game plays where the hero's response rules have it say nothing, which gets lines of rules for similar
    /// situations layered on, so the hero speaks there too.
    /// </summary>
    /// <param name="Hero">The hero's entity name.</param>
    /// <param name="SoundEvent">The sound event that gets the lines.</param>
    /// <param name="DisplayName">When the game plays it.</param>
    /// <param name="Situations">The situations whose lines can be picked, the one spoken by default first.</param>
    sealed record VoiceLayer(string Hero, string SoundEvent, string DisplayName, IReadOnlyList<VoiceSituation> Situations)
    {
        public VoiceSituation Default => Situations[0];
    }

    /// <summary>
    /// The sounds that get voice lines layered on, see <see cref="VoiceLayer"/>. The voice lines are picked by the
    /// server, which an addon cannot change, but the sounds play on the client like any other.
    /// </summary>
    static class VoiceLayers
    {
        public static readonly IReadOnlyList<VoiceLayer> All =
        [
            // Echo Slam's lines go by echoslam_count: none for <=0, good for >1,<=2, mega for >=3. A count of 1, e.g. two
            // heroes caught, matches no rule, and that is when the single slam sound plays
            new("npc_dota_hero_earthshaker", "Hero_EarthShaker.EchoSlam.Single", "Echo Slam the game says nothing for, e.g. two heroes caught",
            [
                new("IsGoodEchoSlam", "Good slam"),
                new("IsMegaEchoSlam", "Mega slam"),
                new("IsFailEchoSlam", "Missed slam"),
            ]),
        ];

        private const int FirstLayer = 2;
        private const int LastLayer = 9;

        /// <summary>
        /// The lines spoken when none were picked: the voice's lines of the default situation, or the hero's own ones as
        /// the voice writes over them when it has none of its own.
        /// </summary>
        /// <param name="criteria">The voice's criteria, null for the hero's own voice.</param>
        /// <param name="voiceLines">The voice line each of the hero's own lines becomes.</param>
        public static List<string> GetDefaultLines(HeroResponseRules rules, VoiceLayer layer, string? criteria, IReadOnlyDictionary<string, string> voiceLines)
        {
            var lines = rules.GetLines(layer.Default.Requirement, criteria);

            return lines.Count > 0
                ? lines
                : [.. rules.GetLines(layer.Default.Requirement, null).Select(line => voiceLines.GetValueOrDefault(line) ?? line)];
        }

        /// <summary>
        /// The sounds each event of a sound event file plays, by event name, empty when the file cannot be read.
        /// </summary>
        /// <param name="file">The file as a source path, e.g. "soundevents/voscripts/game_sounds_vo_earthshaker.vsndevts".</param>
        public static Dictionary<string, List<string>> ReadEventSounds(Package package, string file)
        {
            var sounds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var path = CharacterLoadout.NormalizePath(file) + GameFileLoader.CompiledFileSuffix;

            try
            {
                if (package.FindEntry(path) is not { } entry)
                {
                    return sounds;
                }

                using var resource = new Resource { FileName = path };
                resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                foreach (var (name, definition) in resource.DataBlock?.AsKeyValueCollection().Children ?? [])
                {
                    sounds[name] = [.. GetValues(definition, "vsnd_files")];
                }
            }
            catch (Exception e)
            {
                Log.Warn(nameof(VoiceLayers), $"Failed to read \"{path}\": {e.Message}");
            }

            return sounds;
        }

        /// <summary>The name of the event that speaks the lines, which the sound plays as a layer.</summary>
        public static string GetVoiceEventName(VoiceLayer layer) => $"{layer.SoundEvent}.Voice";

        /// <summary>
        /// An event that speaks one of the lines at random: the first line's definition with every line's sounds.
        /// </summary>
        /// <param name="lines">The definitions of the lines' sound events.</param>
        public static KVObject? CreateVoiceEvent(IReadOnlyList<KVObject> lines)
        {
            var files = lines.SelectMany(static line => GetValues(line, "vsnd_files")).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (files.Count == 0)
            {
                return null;
            }

            var duration = lines
                .SelectMany(static line => GetValues(line, "vsnd_duration"))
                .Select(static value => double.TryParse(value, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0)
                .DefaultIfEmpty(0)
                .Max();

            var voiceEvent = KVObject.ListCollection();

            foreach (var (name, value) in lines[0].Children)
            {
                if (name.Equals("vsnd_files", StringComparison.OrdinalIgnoreCase))
                {
                    var array = KVObject.Array();

                    foreach (var file in files)
                    {
                        array.Add(file);
                    }

                    voiceEvent.Add(name, array);
                }
                else if (name.Equals("vsnd_duration", StringComparison.OrdinalIgnoreCase))
                {
                    voiceEvent.Add(name, (float)duration);
                }
                else
                {
                    voiceEvent.Add(name, KVObjectDeepClone.Clone(value));
                }
            }

            return voiceEvent;
        }

        /// <summary>
        /// A copy of a sound event's definition that also plays another event, in the first layer it does not use.
        /// </summary>
        public static KVObject? AddLayer(KVObject definition, string eventName)
        {
            var layer = Enumerable.Range(FirstLayer, LastLayer - FirstLayer + 1)
                .Select(static index => string.Create(CultureInfo.InvariantCulture, $"soundevent_layer{index}"))
                .FirstOrDefault(key => !definition.Children.Any(child => child.Key.Equals(key, StringComparison.OrdinalIgnoreCase)));

            if (layer == null)
            {
                return null;
            }

            var copy = KVObjectDeepClone.Clone(definition);
            copy.Add(layer, eventName);
            copy.Add($"{layer}_on", 1.0f);

            return copy;
        }

        private static IEnumerable<string> GetValues(KVObject definition, string key)
        {
            foreach (var (name, value) in definition.Children)
            {
                if (!name.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (value.IsArray)
                {
                    foreach (var element in value.Values)
                    {
                        if (element.ToString(CultureInfo.InvariantCulture) is { Length: > 0 } text)
                        {
                            yield return text;
                        }
                    }
                }
                else if (value.ToString(CultureInfo.InvariantCulture) is { Length: > 0 } text)
                {
                    yield return text;
                }
            }
        }
    }
}
