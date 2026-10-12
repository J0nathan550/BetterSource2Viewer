using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// A sound the game plays where the hero's response rules have it say nothing, which gets the lines of a rule for a
    /// similar situation layered on, so the hero speaks there too.
    /// </summary>
    /// <param name="Hero">The hero's entity name.</param>
    /// <param name="SoundEvent">The sound event that gets the lines.</param>
    /// <param name="Requirement">The criteria of the rules whose lines the hero speaks, e.g. "IsGoodEchoSlam".</param>
    sealed record VoiceLayer(string Hero, string SoundEvent, string Requirement);

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
            new("npc_dota_hero_earthshaker", "Hero_EarthShaker.EchoSlam.Single", "IsGoodEchoSlam"),
        ];

        private const int FirstLayer = 2;
        private const int LastLayer = 9;

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
