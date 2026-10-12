using System.IO;
using System.Text.RegularExpressions;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// Game particles that play broken and get written fixed, as copies the particles that play them point at, see
    /// <see cref="CharacterDependencyCollector"/>'s particle fixes.
    /// </summary>
    static partial class ParticleFixes
    {
        /// <summary>The folder the fixed copies of particles go into.</summary>
        public const string CopiesFolder = "particles/j0nathan550";

        private static readonly Dictionary<string, (string Description, Func<string, string> Fix)> Fixes = new(StringComparer.OrdinalIgnoreCase)
        {
            // Saved without the cull's distance, so it culls every dagger as it spawns. The style 1 version culls them
            // where they land.
            ["particles/econ/items/spectre/spectre_arcana/spectre_arcana_v2_desolate_daggers_thrust.vpcf"] =
                ("daggers culled where they land instead of as they spawn", static text => SetDistanceCull(text, "20.0", cullInside: true)),
        };

        /// <summary>Whether a particle, as a source path, gets written fixed.</summary>
        public static bool IsBroken(string particle) => Fixes.ContainsKey(particle);

        /// <summary>What the fix of a particle changes, or null when it has none.</summary>
        public static string? Describe(string particle) => Fixes.TryGetValue(particle, out var fix) ? fix.Description : null;

        /// <summary>The decompiled text of a particle with its fix applied, or as it is when it has none.</summary>
        public static string Apply(string particle, string text) => Fixes.TryGetValue(particle, out var fix) ? fix.Fix(text) : text;

        /// <summary>Where the fixed copy of a particle, or of one that plays a fixed one, goes.</summary>
        public static string GetCopyPath(string particle)
        {
            var path = particle.StartsWith("particles/", StringComparison.OrdinalIgnoreCase) ? particle["particles/".Length..] : particle;
            return $"{CopiesFolder}/{path}";
        }

        /// <summary>
        /// Gives the distance culls that were saved without a distance the one given.
        /// </summary>
        private static string SetDistanceCull(string text, string distance, bool cullInside)
        {
            var fixedText = DistanceCullRegex().Replace(text, match =>
            {
                var indent = match.Groups["indent"].Value;
                var body = match.Groups["body"].Value;

                if (body.Contains("m_flDistance", StringComparison.Ordinal))
                {
                    return match.Value;
                }

                var newLine = match.Groups["head"].Value.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var added = $"{indent}m_flDistance = {distance}{newLine}";

                if (cullInside && !body.Contains("m_bCullInside", StringComparison.Ordinal))
                {
                    added += $"{indent}m_bCullInside = true{newLine}";
                }

                return match.Groups["head"].Value + added + body;
            });

            return fixedText == text
                ? throw new InvalidDataException("It has no distance cull without a distance to fix")
                : fixedText;
        }

        // An operator block's class line and the lines after it at the same indentation
        [GeneratedRegex("""(?<head>(?<indent>[ \t]+)_class = "C_OP_DistanceCull"\r?\n)(?<body>(?:\k<indent>[^ \t\r\n}][^\n]*\n)*)""")]
        private static partial Regex DistanceCullRegex();
    }
}
