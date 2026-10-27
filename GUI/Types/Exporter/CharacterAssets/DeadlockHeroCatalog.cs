using System.IO;
using System.Linq;
using System.Threading;
using GUI.Utils;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// A Deadlock hero as scripts/heroes.vdata describes it, with the assets of its base look and kit.
    /// </summary>
    /// <param name="Name">The hero's key in heroes.vdata, e.g. "hero_inferno".</param>
    /// <param name="Model">The hero's model, a package source path.</param>
    /// <param name="AbilityModels">
    /// The other models the hero's abilities use, such as summons and transformations, leaving out the ones most heroes
    /// share.
    /// </param>
    /// <param name="Particles">
    /// The particles the hero and its abilities create, leaving out the ones most heroes share, such as the melee and
    /// bullet impact effects.
    /// </param>
    /// <param name="SoundEventFiles">The hero's own sound event files, its abilities' and weapon's sounds.</param>
    /// <param name="VoiceEventFiles">The hero's voice line sound event files.</param>
    /// <param name="Released">Whether the hero is playable, rather than disabled or in development.</param>
    sealed record DeadlockHero(string Name, string DisplayName, string Model, IReadOnlyList<string> AbilityModels, IReadOnlyList<string> Particles,
        IReadOnlyList<string> SoundEventFiles, IReadOnlyList<string> VoiceEventFiles, bool Released)
    {
        public override string ToString() => Released ? DisplayName : $"{DisplayName} (in development)";
    }

    /// <summary>
    /// The heroes of Deadlock's package, read from its hero and ability scripts.
    /// </summary>
    static class DeadlockHeroCatalog
    {
        public const string HeroesPath = "scripts/heroes.vdata_c";
        public const string AbilitiesPath = "scripts/abilities.vdata_c";

        // Next to the package on disk rather than in it
        private const string HeroNamesPath = "resource/localization/citadel_gc_hero_names/citadel_gc_hero_names_english.txt";

        /// <summary>
        /// Whether the package has the hero and ability scripts an export is read from.
        /// </summary>
        public static bool IsAvailable(Package package)
            => package.FindEntry(HeroesPath) != null && package.FindEntry(AbilitiesPath) != null;

        /// <summary>
        /// Reads the heroes that have a model in the package, the released ones first, each group by name.
        /// </summary>
        public static List<DeadlockHero> Load(Package package, string vpkPath, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            progress?.Report($"Reading {HeroesPath}...");
            var heroes = ReadScript(package, HeroesPath);

            progress?.Report($"Reading {AbilitiesPath}...");
            var abilities = ReadScript(package, AbilitiesPath);

            var names = LoadHeroNames(vpkPath);
            var entries = new List<(string Name, KVObject Data, HashSet<string> Assets)>();

            foreach (var (name, data) in heroes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!name.StartsWith("hero_", StringComparison.Ordinal) || name == "hero_base" || data.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var assets = new HashSet<string>(GetAssets(data), StringComparer.OrdinalIgnoreCase);

                if (data.GetSubCollection("m_mapBoundAbilities") is { } boundAbilities)
                {
                    foreach (var (_, ability) in boundAbilities)
                    {
                        if (ability.ToString() is { Length: > 0 } abilityName && abilities.GetSubCollection(abilityName) is { } abilityData)
                        {
                            assets.UnionWith(GetAssets(abilityData));
                        }
                    }
                }

                entries.Add((name, data, assets));
            }

            // Every hero has the movement and melee abilities, and most the default bullet effects, which are not theirs
            var users = entries.SelectMany(static entry => entry.Assets).CountBy(static asset => asset, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(StringComparer.OrdinalIgnoreCase);
            var common = entries.Count / 2;

            var result = new List<DeadlockHero>(entries.Count);

            foreach (var (name, data, assets) in entries)
            {
                var model = data.GetStringProperty("m_strModelName");

                if (string.IsNullOrEmpty(model) || package.FindEntry(model + GameFileLoader.CompiledFileSuffix) == null)
                {
                    continue;
                }

                var own = assets.Where(asset => users[asset] <= common).Order(StringComparer.OrdinalIgnoreCase).ToList();

                result.Add(new DeadlockHero(
                    name,
                    names.GetValueOrDefault($"{name}:n") ?? names.GetValueOrDefault(name) ?? name["hero_".Length..],
                    model,
                    [.. own.Where(asset => asset.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) && !asset.Equals(model, StringComparison.OrdinalIgnoreCase))],
                    [.. own.Where(static asset => asset.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase))],
                    GetPaths(data, "m_hGameSoundEventScript"),
                    GetPaths(data, "m_hGeneratedVOEventScript"),
                    !data.GetBooleanProperty("m_bDisabled") && !data.GetBooleanProperty("m_bInDevelopment")));
            }

            return [.. result
                .OrderByDescending(static hero => hero.Released)
                .ThenBy(static hero => hero.DisplayName, StringComparer.OrdinalIgnoreCase)];
        }

        private static List<string> GetPaths(KVObject data, string key)
            => data.GetStringProperty(key) is { Length: > 0 } path ? [path] : [];

        /// <summary>
        /// The models and particles a script entry references, anywhere in it.
        /// </summary>
        private static IEnumerable<string> GetAssets(KVObject value)
        {
            switch (value.ValueType)
            {
                case KVValueType.String when value.ToString() is { } text
                    && (text.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase)):
                    yield return text;
                    break;

                case KVValueType.Collection:
                case KVValueType.Array:
                    foreach (var child in value.Values)
                    {
                        foreach (var asset in GetAssets(child))
                        {
                            yield return asset;
                        }
                    }

                    break;
            }
        }

        private static KVObject ReadScript(Package package, string path)
        {
            var entry = package.FindEntry(path) ?? throw new FileNotFoundException($"\"{path}\" was not found in the package");

            using var resource = new Resource { FileName = path };
            resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

            return resource.DataBlock?.AsKeyValueCollection() ?? throw new InvalidDataException($"\"{path}\" has no data");
        }

        private static Dictionary<string, string> LoadHeroNames(string vpkPath)
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(Path.GetDirectoryName(vpkPath) ?? string.Empty, HeroNamesPath);

            if (!File.Exists(path))
            {
                return names;
            }

            try
            {
                using var stream = File.OpenRead(path);
                KVObject root = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream, new KVSerializerOptions { HasEscapeSequences = true });

                if (root.GetSubCollection("Tokens") is not { ValueType: KVValueType.Collection } tokens)
                {
                    return names;
                }

                foreach (var (key, value) in tokens)
                {
                    if (value.ValueType == KVValueType.String && value.ToString() is { Length: > 0 } text)
                    {
                        names.TryAdd(key, text);
                    }
                }
            }
            catch (Exception e)
            {
                // Heroes fall back to their keys, which is still usable
                Log.Warn(nameof(DeadlockHeroCatalog), $"Failed to read \"{path}\": {e.Message}");
            }

            return names;
        }
    }
}
