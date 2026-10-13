using System.Drawing;
using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    sealed partial class ItemsGameCatalog
    {
        /// <summary>
        /// The colors prismatic gems give items, from items_game.txt's "colors", by their name there, e.g.
        /// "unusual_terrorblade_abysm". See <see cref="CharacterGemColor"/>.
        /// </summary>
        public IReadOnlyList<(string Key, GemColorChoice Choice)> GemColors { get; private init; } = [];

        private static List<(string Key, GemColorChoice Choice)> ReadGemColors(KVObject itemsGame)
        {
            var colors = new List<(string Key, GemColorChoice Choice)>();

            if (itemsGame.GetSubCollection("colors") is not { ValueType: KVValueType.Collection } colorsData)
            {
                return colors;
            }

            foreach (var (key, color) in colorsData)
            {
                // The others are the colors of the item tooltips' text
                if (!key.StartsWith("unusual_", StringComparison.OrdinalIgnoreCase) || color.ValueType != KVValueType.Collection
                    || GetValue(color, "hex_color") is not { Length: 7 } hex || hex[0] != '#'
                    || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                {
                    continue;
                }

                var name = CharacterGemColor.GetDisplayName(GetValue(color, "color_name") is { Length: > 0 } colorName ? colorName : key);
                colors.Add((key, new GemColorChoice(name, Color.FromArgb(255, Color.FromArgb(rgb)))));
            }

            return colors;
        }
    }
}
