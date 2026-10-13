using System.Linq;
using System.Threading;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    sealed partial class CharacterDependencyCollector
    {
        private readonly Dictionary<string, bool> gemColorParticles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> tintableParticles = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Exports the hero's game particles that take the gem color as they are, so they get it written in, see
        /// <see cref="CharacterExportOptions.GemColor"/>: the ones of its abilities, and the parts of the exported particles
        /// and of those its models play. The game gives every particle the hero creates the arcana's color, so it is not
        /// enough to color the ones the items swap in. Effects every hero plays are left alone. With
        /// <see cref="CharacterExportOptions.TintOtherEffects"/>, the ones that take no gem color but have colors to tint are
        /// exported too.
        /// </summary>
        private void AddGemColorParticles(CharacterLoadout loadout, CharacterExportOptions options, HashSet<string> equippedAssets,
            CharacterExportPlan plan, CancellationToken cancellationToken)
        {
            if (options.GemColor == null || !options.ReplaceDefaults)
            {
                return;
            }

            var hero = loadout.Hero;
            var exported = GetExportedParticles(plan).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Their swapped in versions are written over them, which get the color themselves
            var replaced = plan.ParticleReplacements.Select(static replacement => replacement.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var walked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(exported.Concat(GetHeroFolderParticles(hero)).Concat(GetUsedParticles(loadout, equippedAssets)));
            var added = 0;

            while (pending.TryDequeue(out var particle))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!walked.Add(particle) || !IsHeroParticle(hero, particle))
                {
                    continue;
                }

                if (!exported.Contains(particle) && !replaced.Contains(particle)
                    && (ReadsGemColor(particle) || (options.TintOtherEffects && IsTintable(particle))))
                {
                    visited.Add(particle);
                    Visit(particle, includeSounds: false, plan);
                    exported.Add(particle);
                    added++;
                }

                foreach (var reference in GetParticleReferences(particle))
                {
                    pending.Enqueue(reference);
                }
            }

            if (added > 0)
            {
                plan.Notes.Add($"{added} game particle{(added == 1 ? "" : "s")} of {hero.DisplayName} {(added == 1 ? "was" : "were")} written to show the gem color");
            }
        }

        /// <summary>
        /// Gives every exported particle that takes the gem color the picked one, see <see cref="CharacterExportOptions.GemColor"/>,
        /// including the copies of game particles the export writes. Control points the items set win. With
        /// <see cref="CharacterExportOptions.TintOtherEffects"/>, the hero's exported particles get their colors tinted too.
        /// </summary>
        private void SetGemColorControlPoints(CharacterLoadout loadout, CharacterExportOptions options, CharacterExportPlan plan, CancellationToken cancellationToken)
        {
            if (options.GemColor is not { } color)
            {
                return;
            }

            var gemControlPoints = CharacterGemColor.GetControlPoints(color);
            var outputs = GetExportedParticles(plan).Select(static particle => (Particle: particle, Output: particle))
                .Concat(plan.ParticleRedirects
                    .Where(static redirect => !CharacterLoadout.IsSamePath(redirect.Particle, redirect.Output))
                    .Select(static redirect => (redirect.Particle, redirect.Output)));
            var colored = 0;

            foreach (var (particle, output) in outputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (options.TintOtherEffects && IsHeroParticle(loadout.Hero, particle) && IsTintable(particle))
                {
                    plan.TintedParticles.Add(output);
                }

                if (!ReadsGemColor(particle))
                {
                    continue;
                }

                if (!plan.ParticleControlPoints.TryGetValue(output, out var controlPoints))
                {
                    controlPoints = [];
                    plan.ParticleControlPoints[output] = controlPoints;
                }

                foreach (var (number, value) in gemControlPoints)
                {
                    controlPoints.TryAdd(number, value);
                }

                colored++;
            }

            if (colored > 0)
            {
                plan.Notes.Add($"{colored} particle{(colored == 1 ? " was" : "s were")} given the gem color R:{color.R} G:{color.G} B:{color.B}");
            }

            if (plan.TintedParticles.Count > 0)
            {
                plan.Notes.Add($"{plan.TintedParticles.Count} particle{(plan.TintedParticles.Count == 1 ? " was" : "s were")} tinted with the gem color's hue");
            }
        }

        /// <summary>
        /// Exports the materials that read the gem color, of the exported models and of the models the hero wears, so the
        /// color gets written into them, see <see cref="CharacterExportOptions.GemColor"/>. The ones exported for this alone
        /// use the game's textures.
        /// </summary>
        private void AddGemColorMaterials(CharacterLoadout loadout, CharacterExportOptions options, HashSet<string> equippedAssets,
            CharacterExportPlan plan, CancellationToken cancellationToken)
        {
            if (options.GemColor is not { } color)
            {
                return;
            }

            plan.GemColor = color;

            var models = plan.Models
                .Select(static model => NormalizePath(model[..^GameFileLoader.CompiledFileSuffix.Length]))
                .Concat(equippedAssets.Where(static asset => asset.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase)))
                .Concat(loadout.AdditionalWearables.Select(static wearable => wearable.Model))
                .Concat(loadout.VariantModelSwaps.Select(static swap => swap.Model))
                .Append(loadout.HeroModel ?? loadout.Hero.Model)
                .OfType<string>()
                .Select(static model => NormalizePath(model) + GameFileLoader.CompiledFileSuffix)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var before = plan.Materials.ToHashSet(StringComparer.OrdinalIgnoreCase);

            AddMaterials(plan, models, cancellationToken, compiledPath =>
            {
                try
                {
                    using var resource = fileLoader.LoadFile(compiledPath);

                    if (resource?.DataBlock is not Material material || !CharacterGemColor.ReadsColor(material))
                    {
                        return false;
                    }
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read \"{compiledPath}\": {e.Message}");
                    return false;
                }

                plan.GemColorMaterials.Add(NormalizePath(compiledPath[..^GameFileLoader.CompiledFileSuffix.Length]));
                return true;
            });

            plan.GameTextureMaterials.UnionWith(plan.Materials.Where(material => !before.Contains(material)));

            if (plan.GemColorMaterials.Count > 0)
            {
                plan.Notes.Add($"{plan.GemColorMaterials.Count} material{(plan.GemColorMaterials.Count == 1 ? " was" : "s were")} given the gem color in place of $GemColor");
            }
        }

        private static IEnumerable<string> GetExportedParticles(CharacterExportPlan plan) => plan.Resources
            .Select(static path => NormalizePath(path[..^GameFileLoader.CompiledFileSuffix.Length]))
            .Where(IsParticlePath);

        /// <summary>
        /// Every particle in the hero's particle folder, which covers its abilities, as package source paths.
        /// </summary>
        private IEnumerable<string> GetHeroFolderParticles(HeroDefinition hero)
        {
            if (hero.ParticleFolder == null)
            {
                yield break;
            }

            var folder = NormalizePath(hero.ParticleFolder).TrimEnd('/');

            foreach (var entry in GetPackageEntries("vpcf_c"))
            {
                var directory = entry.DirectoryName.Replace('\\', '/');

                if (directory.Equals(folder, StringComparison.OrdinalIgnoreCase)
                    || directory.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    yield return NormalizePath(entry.GetFullPath()[..^GameFileLoader.CompiledFileSuffix.Length]);
                }
            }
        }

        /// <summary>
        /// The particle the equipped items show in place of one, e.g. the arcana's eye glow for Rictus Ripper's empty head
        /// ambient, or the particle itself.
        /// </summary>
        private static string GetShownParticle(CharacterLoadout loadout, string particle) => loadout.Items
            .SelectMany(static item => item.Modifiers)
            .Where(modifier => modifier is { Type: "particle", LoadoutOnly: false, Asset: { } asset } && CharacterLoadout.IsSamePath(asset, particle)
                && IsParticlePath(modifier.Modifier))
            .Select(static modifier => NormalizePath(modifier.Modifier!))
            .FirstOrDefault() ?? particle;

        /// <summary>
        /// Whether a model has every attachment a particle follows in the configuration it plays under on a model, see
        /// <see cref="ModelDocEditor.ResolveParticle"/>, so it shows on that model where it is meant to.
        /// </summary>
        private bool FitsModel(string particle, string model)
        {
            try
            {
                using var modelResource = fileLoader.LoadFileCompiled(NormalizePath(model));
                using var particleResource = fileLoader.LoadFileCompiled(particle);

                if (modelResource?.DataBlock is not Model modelData || particleResource?.DataBlock is not ParticleSystem particleSystem)
                {
                    return false;
                }

                var config = ModelDocEditor.ResolveParticle(fileLoader, particle).Config;
                var attachments = (particleSystem.GetUpgradedData().GetArray("m_controlPointConfigurations") ?? [])
                    .Where(configuration => configuration.GetStringProperty("m_name", string.Empty).Equals(config, StringComparison.OrdinalIgnoreCase))
                    .SelectMany(static configuration => configuration.GetArray("m_drivers") ?? [])
                    .Select(static driver => driver.GetStringProperty("m_attachmentName", string.Empty))
                    .Where(static attachment => attachment.Length > 0)
                    .ToList();

                return attachments.Count > 0 && attachments.All(modelData.Attachments.ContainsKey);
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read the attachments of \"{particle}\" or \"{model}\": {e.Message}");
                return false;
            }
        }

        private bool IsTintable(string particle)
        {
            if (!tintableParticles.TryGetValue(particle, out var tintable))
            {
                try
                {
                    tintable = CharacterGemColor.HasTintableColor(fileLoader, particle);
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read \"{particle}\": {e.Message}");
                }

                tintableParticles[particle] = tintable;
            }

            return tintable;
        }

        private bool ReadsGemColor(string particle)
        {
            if (gemColorParticles.TryGetValue(particle, out var reads))
            {
                return reads;
            }

            try
            {
                reads = CharacterGemColor.ReadsColor(fileLoader, particle);
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read \"{particle}\": {e.Message}");
                reads = false;
            }

            gemColorParticles[particle] = reads;
            return reads;
        }
    }
}
