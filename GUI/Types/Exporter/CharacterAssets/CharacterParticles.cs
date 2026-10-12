using System.IO;
using System.Linq;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// One version of a particle the hero's abilities play.
    /// </summary>
    sealed class ParticleSwapChoice
    {
        /// <summary>The particle that plays, the hero's own for the game's version.</summary>
        public required string Particle { get; init; }

        /// <summary>The items that swap it in, with the style they do it in, null when they do in every style. Empty for the game's own.</summary>
        public List<(EconItem Item, int? Style)> Items { get; } = [];

        /// <summary>What it is, the items it comes from for anything but the game's own particle.</summary>
        public string DisplayName => Items.Count == 0
            ? "Default"
            : string.Join(", ", Items.Select(static source => CharacterSounds.GetItemName(source.Item, source.Style)).Distinct(StringComparer.OrdinalIgnoreCase));

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// A particle of the hero's own that items swap for one of theirs, e.g. Enchant Totem's buff.
    /// </summary>
    sealed class ParticleSlot
    {
        /// <summary>The particle the game plays, e.g. "particles/units/heroes/hero_earthshaker/earthshaker_totem_buff.vpcf".</summary>
        public required string Target { get; init; }

        /// <summary>The particle's name without the hero's, as it reads in a list, e.g. "Totem buff".</summary>
        public required string DisplayName { get; init; }

        /// <summary>The ability whose name the particle's has, e.g. "Enchant Totem", or <see cref="CharacterParticles.OtherGroup"/>.</summary>
        public required string Group { get; init; }

        /// <summary>The versions to choose from, the game's own one first.</summary>
        public List<ParticleSwapChoice> Choices { get; } = [];

        public ParticleSwapChoice Default => Choices[0];
    }

    /// <summary>
    /// Finds the particles items swap for the hero, so the version of each can be picked whichever items are worn.
    /// </summary>
    static class CharacterParticles
    {
        public const string OtherGroup = "Other";

        /// <summary>
        /// The hero's own particles that at least one of its items swaps, by name.
        /// </summary>
        /// <param name="exists">Whether a package path exists, swaps to particles the package lacks are left out.</param>
        public static List<ParticleSlot> GetSlots(ItemsGameCatalog catalog, HeroDefinition hero, Func<string, bool> exists)
        {
            var slots = new Dictionary<string, ParticleSlot>(StringComparer.OrdinalIgnoreCase);
            var abilities = hero.Abilities
                .Select(ability => (Name: catalog.Localize($"DOTA_Tooltip_ability_{ability}") ?? ability, Words: GetAbilityWords(hero, ability)))
                .Where(static ability => ability.Words.Count > 0)
                .ToList();

            foreach (var item in catalog.GetItems(hero))
            {
                foreach (var modifier in item.AssetModifiers)
                {
                    // Swaps of other items' particles, e.g. an arcana's, only refit them to go with this item
                    if (modifier is not { Type: "particle", LoadoutOnly: false, Asset: { Length: > 0 } asset, Modifier: { Length: > 0 } particle }
                        || !IsParticle(asset)
                        || !IsParticle(particle)
                        || IsEconParticle(asset)
                        || CharacterLoadout.IsSamePath(asset, particle)
                        || !CharacterDependencyCollector.IsHeroParticle(hero, CharacterLoadout.NormalizePath(asset))
                        || !exists(CharacterLoadout.NormalizePath(particle) + "_c"))
                    {
                        continue;
                    }

                    var target = CharacterLoadout.NormalizePath(asset);

                    if (!slots.TryGetValue(target, out var slot))
                    {
                        var name = GetShortName(hero, target);
                        slot = new ParticleSlot { Target = target, DisplayName = GetDisplayName(name), Group = GetGroup(abilities, name) };
                        slot.Choices.Add(new ParticleSwapChoice { Particle = target });
                        slots.Add(target, slot);
                    }

                    var source = CharacterLoadout.NormalizePath(particle);
                    var choice = slot.Choices.Find(choice => choice.Particle.Equals(source, StringComparison.OrdinalIgnoreCase));

                    if (choice == null)
                    {
                        choice = new ParticleSwapChoice { Particle = source };
                        slot.Choices.Add(choice);
                    }

                    if (!choice.Items.Contains((item, modifier.Style)))
                    {
                        choice.Items.Add((item, modifier.Style));
                    }
                }
            }

            var groupOrder = abilities.Select(static ability => ability.Name).Append(OtherGroup).ToList();

            return [.. slots.Values
                .OrderBy(slot => groupOrder.IndexOf(slot.Group))
                .ThenBy(static slot => slot.DisplayName, StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>
        /// The version of a particle the worn items play, the game's own one when none of them swaps it. The first worn
        /// item that swaps it wins, like it does in the export.
        /// </summary>
        public static ParticleSwapChoice GetWornChoice(CharacterLoadout loadout, ParticleSlot slot)
            => GetWornParticle(loadout, slot) is { } particle
                ? slot.Choices.FirstOrDefault(choice => choice.Particle.Equals(particle, StringComparison.OrdinalIgnoreCase)) ?? slot.Default
                : slot.Default;

        /// <summary>
        /// What a picked version writes over the hero's particle. Items come with versions of their particles made to go
        /// with another item's, e.g. a totem's buff refit for an arcana, which the game plays when both are worn, so the
        /// picked item's version for what the worn items swap the particle for wins over its plain one.
        /// </summary>
        public static ParticleReplacement GetReplacement(CharacterLoadout loadout, ParticleSlot slot, ParticleSwapChoice choice)
        {
            if (choice == slot.Default || GetWornParticle(loadout, slot) is not { } worn)
            {
                return new ParticleReplacement(choice.Particle, slot.Target);
            }

            foreach (var (item, style) in choice.Items)
            {
                var combined = item.AssetModifiers.FirstOrDefault(modifier => modifier is { Type: "particle_combined", LoadoutOnly: false }
                    && CharacterLoadout.IsSamePath(modifier.Asset, worn)
                    && IsParticle(modifier.Modifier)
                    && (style == null || modifier.Style == null || modifier.Style == style));

                if (combined != null)
                {
                    return new ParticleReplacement(CharacterLoadout.NormalizePath(combined.Modifier!), slot.Target);
                }
            }

            return new ParticleReplacement(choice.Particle, slot.Target);
        }

        private static string? GetWornParticle(CharacterLoadout loadout, ParticleSlot slot) => loadout.Items
            .SelectMany(static item => item.Modifiers)
            .FirstOrDefault(modifier => modifier is { Type: "particle", LoadoutOnly: false, Modifier.Length: > 0 }
                && CharacterLoadout.IsSamePath(modifier.Asset, slot.Target))?
            .Modifier is { } particle
                ? CharacterLoadout.NormalizePath(particle)
                : null;

        // e.g. "totem_buff" for "particles/units/heroes/hero_earthshaker/earthshaker_totem_buff.vpcf", and "frost_arrow" for
        // Drow Ranger's "drow_frost_arrow", whose particles only have part of the hero's name
        private static string GetShortName(HeroDefinition hero, string particle)
        {
            var heroWords = hero.ShortName.Split('_').ToHashSet(StringComparer.OrdinalIgnoreCase);
            var words = Path.GetFileNameWithoutExtension(particle).Split('_');
            var start = 0;

            while (start < words.Length - 1 && heroWords.Contains(words[start]))
            {
                start++;
            }

            return string.Join('_', words[start..]);
        }

        // The words of an ability's name that particle names use, e.g. "totem" of "earthshaker_enchant_totem", and all of
        // them run together, since particles write some names as one word, e.g. "echoslam"
        private static List<string> GetAbilityWords(HeroDefinition hero, string ability)
        {
            var heroWords = hero.ShortName.Split('_').ToHashSet(StringComparer.OrdinalIgnoreCase);
            var words = ability.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => word.Length >= 4 && !heroWords.Contains(word))
                .Select(static word => word.Length > 4 && word.EndsWith('s') ? word[..^1] : word)
                .ToList();

            if (words.Count > 1)
            {
                words.Add(string.Concat(words));
            }

            return words;
        }

        // The ability most of whose words the particle's name has
        private static string GetGroup(List<(string Name, List<string> Words)> abilities, string particle)
        {
            var words = particle.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(static word => word.Length > 4 && word.EndsWith('s') ? word[..^1] : word)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var joined = particle.Replace("_", string.Empty, StringComparison.Ordinal);

            return abilities
                .Select(ability => (ability.Name, Matches: ability.Words.Count(word => words.Contains(word) || (word.Length > 6 && joined.Contains(word, StringComparison.OrdinalIgnoreCase)))))
                .Where(static ability => ability.Matches > 0)
                .OrderByDescending(static ability => ability.Matches)
                .Select(static ability => ability.Name)
                .FirstOrDefault() ?? OtherGroup;
        }

        // e.g. "Totem blur impact" for "totem_blur_impact"
        private static string GetDisplayName(string name)
        {
            var text = name.Replace('_', ' ');

            return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : name;
        }

        private static bool IsParticle(string? path) => path != null && path.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase);

        private static bool IsEconParticle(string path) => CharacterLoadout.NormalizePath(path).StartsWith("particles/econ/", StringComparison.OrdinalIgnoreCase);
    }
}
