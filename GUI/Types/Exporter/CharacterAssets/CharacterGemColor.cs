using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// A color from items_game.txt's "colors" that a prismatic gem can give an item, e.g. "Abysm".
    /// </summary>
    sealed record GemColorChoice(string Name, Color Color)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// The color a prismatic gem gives an item's effects and materials. The game hands it to particles as control point
    /// <see cref="ColorControlPoint"/>, with how much of it to take in <see cref="StrengthControlPoint"/>, and to materials
    /// as <c>$GemColor</c>. Some arcanas get a color of their own without a gem, e.g. Terrorblade's red: their particles are
    /// made blue and only turn red through those, so an addon hero, which never gets them from the game, has the color
    /// written into its particles and materials instead.
    /// </summary>
    static partial class CharacterGemColor
    {
        /// <summary>The control point particles take the color from, 0 to 255 for each channel.</summary>
        public const int ColorControlPoint = 15;

        /// <summary>The control point whose first value is how much of the color particles take, from 0 to 1.</summary>
        public const int StrengthControlPoint = 16;

        /// <summary>
        /// The colors items show without a gem, by def index, as their names in items_game.txt's "colors". The game picks
        /// them in code, items_game.txt only lists the colors.
        /// </summary>
        private static readonly Dictionary<string, string> ItemColors = new()
        {
            ["5957"] = "unusual_terrorblade_abysm", // Fractal Horns of Inner Abysm
            ["6879"] = "unusual_techies_swine", // Swine of the Sunken Galley
        };

        // In case items_game.txt no longer lists them
        private static readonly Dictionary<string, Color> FallbackColors = new()
        {
            ["unusual_terrorblade_abysm"] = Color.FromArgb(0xFF, 0x3C, 0x28),
            ["unusual_techies_swine"] = Color.FromArgb(0xFF, 0xAF, 0x00),
        };

        /// <summary>
        /// The color the equipped items show their effects in without a gem, e.g. Terrorblade's arcana red, or null when
        /// none has one.
        /// </summary>
        public static GemColorChoice? GetItemColor(ItemsGameCatalog catalog, IEnumerable<EquippedItem> items)
        {
            foreach (var item in items)
            {
                if (!ItemColors.TryGetValue(item.Item.DefIndex, out var key))
                {
                    continue;
                }

                if (catalog.GemColors.FirstOrDefault(choice => choice.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } listed)
                {
                    return listed.Choice;
                }

                if (FallbackColors.TryGetValue(key, out var color))
                {
                    return new GemColorChoice(GetDisplayName(key), color);
                }
            }

            return null;
        }

        /// <summary>
        /// The readable name of a color in items_game.txt's "colors", e.g. "Pristine Platinum" for "UnusualPristinePlatinum"
        /// or "unusual_pristine_platinum", which the game has no localized names for.
        /// </summary>
        public static string GetDisplayName(string name)
        {
            const string Prefix = "Unusual";

            if (name.Contains('_', StringComparison.Ordinal))
            {
                var words = name.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();

                if (words.Count > 1 && words[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    words.RemoveAt(0);
                }

                return string.Join(' ', words.Select(static word => char.ToUpperInvariant(word[0]) + word[1..]));
            }

            if (name.Length > Prefix.Length && name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                name = name[Prefix.Length..];
            }

            return WordStartRegex().Replace(name, " $1");
        }

        /// <summary>
        /// The control points that give a particle the color in full, see <see cref="CharacterExportPlan.ParticleControlPoints"/>.
        /// </summary>
        public static Dictionary<int, Vector3> GetControlPoints(Color color) => new()
        {
            [ColorControlPoint] = new Vector3(color.R, color.G, color.B),
            [StrengthControlPoint] = new Vector3(1f, 0f, 0f),
        };

        /// <summary>
        /// Whether a particle takes its color from <see cref="ColorControlPoint"/>, as opposed to one that sets that
        /// control point itself for something else, e.g. a parent handing positions to its children.
        /// </summary>
        public static bool ReadsColor(IFileLoader fileLoader, string particle)
        {
            using var resource = fileLoader.LoadFileCompiled(particle);

            if (resource?.DataBlock is not ParticleSystem particleSystem)
            {
                return false;
            }

            var data = particleSystem.Data;
            return ReadsControlPoint(data) && !SetsControlPoint(data);
        }

        /// <summary>
        /// Whether a material reads <c>$GemColor</c> in its dynamic params.
        /// </summary>
        public static bool ReadsColor(Material material)
            => material.DynamicExpressions.Values.Any(static expression => expression.Contains(MaterialAttribute, StringComparison.OrdinalIgnoreCase));

        private const string MaterialAttribute = "$GemColor";

        /// <summary>
        /// Writes the color into a decompiled material's dynamic params in place of <c>$GemColor</c>, which nothing sets
        /// without the item equipped, e.g. "return exists($GemColor) ? $GemColor : float3(.34,.73,.93);" becomes
        /// "return float3(1,.235,.157);".
        /// </summary>
        public static string BakeIntoMaterial(string vmat, Color color)
        {
            static string Format(int channel) => (channel / 255f).ToString("0.###", CultureInfo.InvariantCulture);

            var literal = $"float3({Format(color.R)},{Format(color.G)},{Format(color.B)})";

            vmat = ExistsChoiceRegex().Replace(vmat, literal);
            vmat = ExistsRegex().Replace(vmat, "1");

            return AttributeRegex().Replace(vmat, literal);
        }

        private static bool ReadsControlPoint(KVObject value)
        {
            if (value.ValueType == KVValueType.Collection)
            {
                if (IsInteger(value, "m_nCPInput", ColorControlPoint)
                    || (value.GetStringProperty("m_nType") == "PT_TYPE_CONTROL_POINT" && IsInteger(value, "m_nControlPoint", ColorControlPoint)))
                {
                    return true;
                }
            }

            return value.ValueType is KVValueType.Collection or KVValueType.Array && value.Values.Any(ReadsControlPoint);
        }

        private static bool SetsControlPoint(KVObject value)
        {
            if (value.ValueType == KVValueType.Collection
                && value.GetStringProperty("_class") is { } className
                && className.StartsWith("C_OP_Set", StringComparison.Ordinal)
                && className.Contains("ControlPoint", StringComparison.Ordinal)
                && value.Any(static property => SetControlPointKeyRegex().IsMatch(property.Key) && IsInteger(property.Value, ColorControlPoint)))
            {
                return true;
            }

            return value.ValueType is KVValueType.Collection or KVValueType.Array && value.Values.Any(SetsControlPoint);
        }

        private static bool IsInteger(KVObject value, string key, int expected)
            => value.TryGetValue(key, out var child) && IsInteger(child, expected);

        // Binary and text particles store their numbers as different types
        private static bool IsInteger(KVObject value, int expected)
            => value.ValueType is not (KVValueType.Collection or KVValueType.Array or KVValueType.String)
                && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && number == expected;

        [GeneratedRegex(@"exists\(\s*\$GemColor\s*\)\s*\?\s*\$GemColor\s*:\s*[^;""]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ExistsChoiceRegex();

        [GeneratedRegex(@"exists\(\s*\$GemColor\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ExistsRegex();

        [GeneratedRegex(@"\$GemColor\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex AttributeRegex();

        // m_nCP1 of C_OP_SetSingleControlPointPosition, m_nCP1 to m_nCP4 of C_OP_SetControlPointPositions and the like
        [GeneratedRegex(@"^m_nCP\d$", RegexOptions.CultureInvariant)]
        private static partial Regex SetControlPointKeyRegex();

        [GeneratedRegex(@"(?<=[a-z])([A-Z])", RegexOptions.CultureInvariant)]
        private static partial Regex WordStartRegex();
    }
}
