using System.IO;
using GUI.Utils;

namespace GUI.Types.Exporter.CharacterAssets
{
    static partial class CharacterAssetsExporter
    {
        /// <summary>
        /// Gives the colors of the exported particles that take no gem color its hue, see
        /// <see cref="CharacterExportOptions.TintOtherEffects"/>. Runs before the replacements, which copy the particles
        /// as they are written here.
        /// </summary>
        private static int ApplyGemColorTint(CharacterExportPlan plan, string outputRoot, IProgress<string> progress)
        {
            if (plan.GemColor is not { } color || plan.TintedParticles.Count == 0)
            {
                return 0;
            }

            progress.Report($"Tinting {plan.TintedParticles.Count} particles with the gem color's hue...");

            var failed = 0;

            foreach (var particle in plan.TintedParticles)
            {
                try
                {
                    var path = GetExportedPath(outputRoot, particle, "vpcf");
                    File.WriteAllText(path, CharacterGemColor.TintParticle(File.ReadAllText(path), color));
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {particle}: {e.Message}");
                    Log.Error(nameof(CharacterAssetsExporter), $"Failed to tint '{particle}': {e}");
                }
            }

            return failed;
        }
    }
}
