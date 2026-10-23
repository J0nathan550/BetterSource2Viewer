using System.Globalization;
using System.Linq;
using ValveResourceFormat.IO;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// An activity modifier the hero's items set, which makes the hero play the sequences made for it, e.g. a totem's
    /// attack. It can be applied or left out whichever items are worn.
    /// </summary>
    sealed class AnimationChoice
    {
        /// <summary>The modifier's name, e.g. "ti9_totem".</summary>
        public required string Name { get; init; }

        /// <summary>What the items set, one for each activity the modifier is limited to.</summary>
        public required IReadOnlyList<ActivityModifier> Modifiers { get; init; }

        /// <summary>The items that set it.</summary>
        public required IReadOnlyList<EconItem> Items { get; init; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Animations of a model played one after another as an animation of its own, see <see cref="ChainedAnimation"/>.
    /// </summary>
    /// <param name="Name">The chain's name.</param>
    /// <param name="Parts">The animations' names, in the order they play.</param>
    sealed record AnimationChain(string Name, IReadOnlyList<string> Parts);

    /// <summary>
    /// Works out which activity modifiers the hero's models are exported with, so animations of items can be mixed with
    /// other items' looks.
    /// </summary>
    static class CharacterAnimations
    {
        // The server only picks between the original's few versions of an activity, so models with more versions get
        // some of theirs played in another state's place by hand
        private static readonly Dictionary<string, Dictionary<string, string>> PickedSequences = new(StringComparer.OrdinalIgnoreCase)
        {
            // Axe has two attacks, which play the punch and the left hook, so the kick plays while he is injured
            ["models/items/axe/ti9_jungle_axe/axe_bare.vmdl"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["attack_main_injured"] = "ti9_axe_attack_kick_injured",
            },
            // Drow Ranger has one run, which plays the arcana's long run and then its slide
            ["models/items/drow/drow_arcana/drow_arcana.vmdl"] = new(StringComparer.OrdinalIgnoreCase)
            {
                ["run_anim"] = "drow_arc_run_long_slide",
            },
        };

        private static readonly Dictionary<string, AnimationChain[]> Chains = new(StringComparer.OrdinalIgnoreCase)
        {
            ["models/items/drow/drow_arcana/drow_arcana.vmdl"] = [new("drow_arc_run_long_slide", ["drow_arc_run_long", "drow_arc_arcana_run_slide"])],
        };

        /// <summary>
        /// The sequence of a model each sequence of the one it is written over plays instead of the closest one, by the
        /// original's name, see <see cref="ModelDocEditor.MatchSequenceOrder"/>.
        /// </summary>
        /// <param name="model">The model, as a package source path.</param>
        public static IReadOnlyDictionary<string, string>? GetPickedSequences(string model) => PickedSequences.GetValueOrDefault(model);

        /// <summary>
        /// The animations a model gets that play some of its own one after another, see <see cref="ChainedAnimation"/>.
        /// </summary>
        /// <param name="model">The model, as a package source path.</param>
        public static IReadOnlyList<AnimationChain> GetChains(string model) => Chains.GetValueOrDefault(model) ?? [];

        /// <summary>
        /// Every activity modifier any of the hero's items sets, by name.
        /// </summary>
        public static List<AnimationChoice> GetChoices(ItemsGameCatalog catalog, HeroDefinition hero) => [.. catalog.GetItems(hero)
            .SelectMany(static item => item.AssetModifiers
                .Where(CharacterLoadout.IsActivityModifier)
                .Select(modifier => (Item: item, Modifier: CharacterLoadout.ToActivityModifier(modifier))))
            .GroupBy(static pair => pair.Modifier.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => new AnimationChoice
            {
                Name = group.First().Modifier.Name,
                Modifiers = [.. group.Select(static pair => pair.Modifier).Distinct()],
                Items = [.. group.Select(static pair => pair.Item).Distinct()],
            })
            .OrderBy(static choice => choice.Name, StringComparer.OrdinalIgnoreCase)];

        /// <summary>
        /// The activities a model has sequences made for each activity modifier in, by modifier name. A modifier that is
        /// missing changes nothing about the model.
        /// </summary>
        public static Dictionary<string, List<string>> GetModifiedActivities(IFileLoader fileLoader, string model)
        {
            var activities = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var sequence in CharacterAssetsExporter.GetSequences(fileLoader, model))
            {
                if (sequence.Activity is not { Length: > 0 } activity)
                {
                    continue;
                }

                foreach (var modifier in sequence.Modifiers)
                {
                    if (!activities.TryGetValue(modifier, out var list))
                    {
                        list = [];
                        activities.Add(modifier, list);
                    }

                    if (!list.Contains(activity, StringComparer.Ordinal))
                    {
                        list.Add(activity);
                    }
                }
            }

            return activities;
        }

        /// <summary>
        /// The activity modifiers to export with: the worn items' ones that were not unticked by hand, and the ones ticked
        /// by hand that no worn item sets.
        /// </summary>
        /// <param name="picked">Whether each modifier was ticked or unticked by hand, by name.</param>
        public static List<ActivityModifier> Resolve(IEnumerable<AnimationChoice> choices, IReadOnlyList<ActivityModifier> worn,
            IReadOnlyDictionary<string, bool> picked)
        {
            var modifiers = worn.Where(modifier => !picked.TryGetValue(modifier.Name, out var ticked) || ticked).ToList();

            foreach (var choice in choices)
            {
                if (picked.TryGetValue(choice.Name, out var ticked) && ticked
                    && !worn.Any(modifier => modifier.Name.Equals(choice.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    modifiers.AddRange(choice.Modifiers);
                }
            }

            return [.. modifiers.Distinct()];
        }

        /// <summary>
        /// An activity as it reads in a list, e.g. "Cast ability 2" for "ACT_DOTA_CAST_ABILITY_2".
        /// </summary>
        public static string GetActivityText(string activity)
        {
            var name = activity.StartsWith("ACT_DOTA_", StringComparison.Ordinal) ? activity["ACT_DOTA_".Length..]
                : activity.StartsWith("ACT_", StringComparison.Ordinal) ? activity["ACT_".Length..]
                : activity;

            var text = name.Replace('_', ' ').ToLower(CultureInfo.InvariantCulture);

            return text.Length > 0 ? char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..] : activity;
        }
    }
}
