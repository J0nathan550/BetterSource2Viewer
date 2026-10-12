using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// What to export for a character besides the selected items' own models. The switches are saved between runs of the
    /// app, see <see cref="CharacterExportPreferences"/>, the choices that come from the loadout are not.
    /// </summary>
    sealed class CharacterExportOptions
    {
        /// <summary>
        /// Whether the dialog picks the options from the loadout, see <see cref="Recommended"/>, rather than as ticked by
        /// hand. The other options then hold what was last ticked by hand.
        /// </summary>
        public bool OnlyWhatChanges { get; set; } = true;

        /// <summary>The hero's model, or the one an equipped item swaps it for.</summary>
        public bool HeroModel { get; set; } = true;

        /// <summary>Models the selected items wear or swap in.</summary>
        public bool ItemModels { get; set; } = true;

        /// <summary>
        /// The materials every exported model uses, with their textures, so the addon compiles its own copies. Otherwise
        /// the models use the game's, which it applies by itself, see <see cref="ArcanaMaterials"/> to export only some.
        /// Recolors always export them, they are what gets recolored.
        /// </summary>
        public bool Materials { get; set; }

        /// <summary>
        /// Without <see cref="Materials"/>, still exports the materials of the models that switch their meshes by the
        /// arcana level, for the arcanas that render semi-transparent in game when the addon uses the game's compiled
        /// ones. Off by default, the game applies its own materials to most.
        /// </summary>
        public bool ArcanaMaterials { get; set; }

        /// <summary>
        /// Exports copies of every material the exported models use, with their textures, with alpha testing turned on,
        /// into <see cref="MaterialCopiesFolder"/>, and points the models at them. For the arcanas that render
        /// see-through in game with the game's own materials, e.g. Razor's. Off by default, it is not needed for most.
        /// </summary>
        public bool AlphaTestMaterials { get; set; }

        /// <summary>Where <see cref="AlphaTestMaterials"/> puts the copies, under their own paths.</summary>
        public const string MaterialCopiesFolder = "materials/j0nathan550";

        /// <summary>
        /// Adds the meshes of the models items wear besides their own, e.g. an arcana's frost overlay, to the hero's model
        /// when the default assets are replaced. They are exported as models of their own either way.
        /// </summary>
        public bool MergeAdditionalWearables { get; set; } = true;

        /// <summary>Particles the selected items create or swap in.</summary>
        public bool ItemParticles { get; set; } = true;

        /// <summary>
        /// Whether each of the particles the equipped items create is exported, see <see cref="CharacterLoadout.CreatedEffects"/>,
        /// by package source path. The ones left out are exported when the game shows them with the equipped items.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, bool> ItemEffects { get; set; } = new Dictionary<string, bool>();

        /// <summary>
        /// Versions of the hero's particles picked by hand, which win over the ones the worn items swap in, e.g. a totem's
        /// effects without the totem. A source that is the target itself keeps the game's own particle.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<ParticleReplacement> ParticleSwaps { get; set; } = [];

        /// <summary>
        /// The voice lines picked for each <see cref="VoiceLayer"/>, by its sound event. A layer missing here speaks its
        /// default lines, one with no lines is left out.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyDictionary<string, IReadOnlyList<string>> VoiceLayerLines { get; set; } = new Dictionary<string, IReadOnlyList<string>>();

        /// <summary>Every particle in the hero's particle folder, which covers its abilities.</summary>
        public bool HeroParticles { get; set; }

        /// <summary>
        /// The sound event files of the sound events the selected items swap in, as they are. <see cref="Sounds"/> already
        /// writes the swapped in events over the hero's own, so these are only of use to edit them by hand.
        /// </summary>
        public bool ItemSounds { get; set; }

        /// <summary>The hero's game sound events file, as it is, see <see cref="ItemSounds"/>.</summary>
        public bool HeroSounds { get; set; }

        /// <summary>The hero's voice line events file, as it is, see <see cref="ItemSounds"/>.</summary>
        public bool HeroVoice { get; set; }

        /// <summary>
        /// The sounds the exported sound events play. Without them, only the sound event files are written, which keep
        /// pointing at the sounds in the game.
        /// </summary>
        public bool IncludeAudio { get; set; }

        /// <summary>
        /// Writes <see cref="IconReplacements"/> into the game folder: the compiled images the game already has, renamed
        /// over the hero's own icons.
        /// </summary>
        public bool Icons { get; set; } = true;

        /// <summary>The icons to write when <see cref="Icons"/> is set.</summary>
        [JsonIgnore]
        public IReadOnlyList<IconReplacement> IconReplacements { get; set; } = [];

        /// <summary>
        /// Writes <see cref="SpriteReplacements"/> into a copy of <see cref="CharacterIcons.SpriteSheetFile"/> in the
        /// game folder, which is where the minimap takes the hero's icon from. The copy stands in for the whole file, so
        /// other mods' changes to it are lost unless they are in the copy too, which is why it is left out by default.
        /// A copy an earlier export wrote is changed rather than started over.
        /// </summary>
        public bool SpriteSheet { get; set; }

        /// <summary>The sprites to write when <see cref="SpriteSheet"/> is set, e.g. the minimap icon.</summary>
        [JsonIgnore]
        public IReadOnlyList<SpriteReplacement> SpriteReplacements { get; set; } = [];

        /// <summary>
        /// Writes <see cref="DefaultItemSwaps"/> into a copy of items_game.txt in the game folder, so the hero's default
        /// items are defined as the equipped items and the game shows those without any model being replaced. Like
        /// <see cref="SpriteSheet"/>, the copy stands in for the whole file, so it is left out by default, and a copy an
        /// earlier export wrote is changed rather than started over.
        /// </summary>
        public bool DefaultItemsInItemsGame { get; set; }

        /// <summary>The default items to write when <see cref="DefaultItemsInItemsGame"/> is set.</summary>
        [JsonIgnore]
        public IReadOnlyList<DefaultItemSwap> DefaultItemSwaps { get; set; } = [];

        /// <summary>
        /// With <see cref="ReplaceDefaults"/>, writes the portrait settings of each model written over another, e.g. the
        /// lights an arcana's HUD portrait is lit with, over those of the model it replaces, in a copy of
        /// <see cref="CharacterAssetsExporter.PortraitsFile"/> in the game folder. The game looks them up by the model's
        /// path, so it would light the replacement like the original. Like <see cref="SpriteSheet"/>, the copy stands in
        /// for the whole file, so it is left out by default, and a copy an earlier export wrote is changed rather than
        /// started over.
        /// </summary>
        public bool Portraits { get; set; }

        /// <summary>
        /// Writes <see cref="SoundReplacements"/> and the <see cref="Voice"/> lines over the hero's own sounds, in the
        /// sound event files they are defined in.
        /// </summary>
        public bool Sounds { get; set; } = true;

        /// <summary>The sound events to write over others when <see cref="Sounds"/> is set.</summary>
        [JsonIgnore]
        public IReadOnlyList<SoundReplacement> SoundReplacements { get; set; } = [];

        /// <summary>
        /// The voice whose lines are written over the hero's own when <see cref="Sounds"/> is set, see
        /// <see cref="HeroResponseRules.MapVoice"/>, or null to keep the hero's voice.
        /// </summary>
        [JsonIgnore]
        public VoiceChoice? Voice { get; set; }

        /// <summary>
        /// The models the hero stands on in the loadout screen, and the particles items only play there. With
        /// <see cref="ReplaceDefaults"/>, the item's pedestal is written over the hero's own.
        /// </summary>
        public bool Pedestal { get; set; }

        /// <summary>
        /// Writes the equipped look over the hero's default assets, so it shows without the items being equipped: the
        /// arcana or persona model as the hero's model, chosen items over the default items' models, the persona's items
        /// over the hero's own default items, see <see cref="CharacterLoadout.PersonaModels"/>, particles the items
        /// swap in over the ones they replace, the particles items create added to their models, and the skin and
        /// animations items pick for the hero made its defaults.
        /// </summary>
        public bool ReplaceDefaults { get; set; } = true;

        /// <summary>
        /// Also replaces particles every hero uses, like the blink dagger or stun effects, which then change for all heroes.
        /// Works without <see cref="ItemParticles"/>, to export only these.
        /// </summary>
        public bool ReplaceSharedParticles { get; set; }

        /// <summary>
        /// With <see cref="ReplaceDefaults"/>, moves each model over the default one it replaces as it was exported,
        /// like renaming drow_arcana_weapon to drow_weapon by hand. Body group choices the items pick are kept enabled
        /// and the others are disabled rather than removed, see <see cref="ModelDocEditor.DisableBodyGroupChoices"/>.
        /// The picked style's skin is made the default material group, extra wearables are not merged, and the
        /// particles items create and the activity modifiers are still added. Items without a default model of their
        /// own take the place of the model they swap to fit them, e.g. a helmet renamed over the hero's head in place of
        /// the face refit that goes under it; without this they are added to that model with it. Those that swap none
        /// are added to the hero's model either way, e.g. Earthshaker's arcana head.
        /// </summary>
        public bool RenameModels { get; set; } = true;

        /// <summary>
        /// With <see cref="ReplaceDefaults"/>, adds the persona pieces and items the game keeps apart from the hero so they
        /// play animations of their own, e.g. the wind-up key on Morphling's automaton or Legion Commander's Hell's Legion
        /// wings, to the hero's model with those animations layered on the hero's, see
        /// <see cref="ModelDocEditor.AddLayeredAnimations"/>. Otherwise they are written over the default models like
        /// other items, and those parts stand still in game. Items that follow the hero's sequences, like arcana pieces,
        /// are always written over their default models.
        /// </summary>
        public bool AnimateOwnParts { get; set; } = true;

        /// <summary>
        /// With <see cref="ReplaceDefaults"/>, leaves out the models the loadout does not change, e.g. the hero's own model
        /// when only an item is swapped. They would be copies of what the game already has.
        /// </summary>
        public bool SkipUnchangedModels { get; set; } = true;

        /// <summary>
        /// Particles taken out of the models the export writes, as package source paths, see
        /// <see cref="ModelDocEditor.GetOwnParticles"/>. Models that would otherwise be left as they are get written
        /// for it too.
        /// </summary>
        [JsonIgnore]
        public IReadOnlySet<string> RemovedModelParticles { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Need to apply recolor options to a model
        /// </summary>
        public Dictionary<string, ItemRecolorOption> RecolorOptions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Color> ParticleRecolorOptions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The options that make an addon of only what the loadout changes about the hero: its look written over the
        /// default assets, the effects the game shows with the items, the sounds and icons picked, and nothing the game
        /// already has as it is, materials included.
        /// </summary>
        public static CharacterExportOptions Recommended => new()
        {
            OnlyWhatChanges = true,
            HeroModel = true,
            ItemModels = true,
            Materials = false,
            ArcanaMaterials = false,
            AlphaTestMaterials = false,
            MergeAdditionalWearables = true,
            ItemParticles = true,
            HeroParticles = false,
            ItemSounds = false,
            HeroSounds = false,
            HeroVoice = false,
            IncludeAudio = false,
            Icons = true,
            SpriteSheet = false,
            DefaultItemsInItemsGame = false,
            Portraits = false,
            Sounds = true,
            Pedestal = false,
            ReplaceDefaults = true,
            ReplaceSharedParticles = false,
            RenameModels = true,
            AnimateOwnParts = true,
            SkipUnchangedModels = true,
        };

        /// <summary>
        /// What the options add that a hero mod most likely does not need, or that changes more than the hero, to warn
        /// about before exporting.
        /// </summary>
        public List<string> GetWarnings()
        {
            var warnings = new List<string>();

            if (!ReplaceDefaults)
            {
                warnings.Add("Replace default assets is off: the models are exported as they are, and nothing in game uses them until they are put in place by hand.");
            }
            else if (!SkipUnchangedModels && (HeroModel || ItemModels))
            {
                warnings.Add("Only models the loadout changes is off: models the game already has as they are, like the hero's own model, are exported too.");
            }

            if (Materials && (HeroModel || ItemModels))
            {
                warnings.Add("Materials and textures: copies of every material the exported models use, which the game already has and applies by itself. Recolors get theirs either way.");
            }

            if (HeroParticles)
            {
                warnings.Add("All hero particles: every particle of the hero's abilities, as the game has them.");
            }

            if (HeroSounds)
            {
                warnings.Add("Hero sound events: the hero's whole game_sounds file, as the game has it. Replace sounds already writes the sounds the loadout changes.");
            }

            if (HeroVoice)
            {
                warnings.Add("Voice line events: the hero's whole voice file, as the game has it. Replace sounds already writes the voice the loadout picks.");
            }

            if (ItemSounds && (Sounds || ReplaceDefaults))
            {
                warnings.Add("Item sound events: the files the items' sounds are defined in, as the game has them. Replace sounds already writes them over the hero's own.");
            }

            if (IncludeAudio)
            {
                warnings.Add("Include the sounds they play: copies of sounds the game already has, usually most of the export's size.");
            }

            if (Pedestal)
            {
                warnings.Add("Pedestal: only shown in the loadout screen, which custom games do not have.");
            }

            if (ReplaceDefaults && ReplaceSharedParticles)
            {
                warnings.Add("Also shared particles: effects every hero uses, like the blink dagger's, change for all heroes.");
            }

            return warnings;
        }

        /// <summary>
        /// The question to ask before exporting with <see cref="GetWarnings"/>.
        /// </summary>
        public static string GetWarningsMessage(IEnumerable<string> warnings)
            => "These options add what the mod most likely does not need, or change more than the hero:\n\n" +
                string.Join("\n", warnings.Select(static warning => $"- {warning}")) +
                "\n\nThe Recommended options export only what the loadout changes. Export with these anyway?";

        /// <summary>
        /// What the Recommended options export, with what was asked for besides.
        /// </summary>
        public static string GetRecommendedHint(bool materials, bool alphaTestMaterials, bool sharedParticles)
        {
            List<string> extras = [];

            if (materials)
            {
                extras.Add("the materials to modify");
            }

            if (alphaTestMaterials)
            {
                extras.Add("the materials with alpha test");
            }

            if (sharedParticles)
            {
                extras.Add("the shared particles");
            }

            return extras switch
            {
                [] => "Exports only what the loadout changes.",
                [var extra] => $"Exports only what the loadout changes, and {extra}.",
                [.. var first, var last] => $"Exports only what the loadout changes, {string.Join(", ", first)}, and {last}.",
            };
        }
    }

    public sealed record ItemRecolorOption(Color Color, bool Enabled = true);

    /// <summary>
    /// A model written over another one, see <see cref="CharacterExportOptions.ReplaceDefaults"/>.
    /// </summary>
    /// <param name="Source">The model whose decompiled source is written.</param>
    /// <param name="Target">The model it is written as.</param>
    /// <param name="Skin">The material group to make the default one.</param>
    /// <param name="Particles">Particles the model should create itself, since the items that create them are not equipped.</param>
    sealed record ModelReplacement(string Source, string Target, int Skin, List<string> Particles)
    {
        /// <summary>The body group choices to pick, since nothing switches them without the items equipped.</summary>
        public Dictionary<string, int> BodyGroups { get; init; } = [];

        /// <summary>Models whose meshes are added to this one, since they have no model of their own to be written over.</summary>
        public List<MergedModel> Merged { get; init; } = [];

        /// <summary>The activity modifiers to apply, since nothing sets them without the items equipped.</summary>
        public List<ActivityModifier> ActivityModifiers { get; init; } = [];

        /// <summary>Whether the source model is moved rather than copied, see <see cref="CharacterExportOptions.RenameModels"/>.</summary>
        public bool Rename { get; init; }

        /// <summary>
        /// Whether the hero the model is written over attacks from range, which picks between versions of an attack
        /// made for either, null for models other than the hero's.
        /// </summary>
        public bool? RangedAttack { get; init; }
    }

    /// <summary>
    /// A model whose meshes are added to another one, see <see cref="ModelReplacement.Merged"/>.
    /// </summary>
    sealed record MergedModel(string Model, int Skin, Dictionary<string, int> BodyGroups)
    {
        /// <summary>
        /// The model's bones the hero does not have, which its own animations move, see
        /// <see cref="ModelDocEditor.AddLayeredAnimations"/>. Empty when only the hero's skeleton moves it.
        /// </summary>
        public IReadOnlyList<string> AnimatedBones { get; init; } = [];

        /// <summary>
        /// The activity modifiers of the hero's sequences that play the model's scripted gesture on <see cref="AnimatedBones"/>,
        /// e.g. Legion Commander's wings raised for as long as Press the Attack lasts. The item has the game play that
        /// gesture for the ability's buff, which an addon hero without the item never gets, so the sequences the buff
        /// picks play it instead.
        /// </summary>
        public IReadOnlyList<string> GestureModifiers { get; init; } = [];

        /// <summary>
        /// Whether the model shows only the body group choices picked for it, like the game shows the item at the arcana
        /// level, with that choice's own materials, rather than the arcana body group's first choice given the style's
        /// materials, see <see cref="ModelDocEditor.SelectBodyGroupChoices"/>. For items added to a model for want of
        /// one of their own to be written over, e.g. Earthshaker's arcana head, which the hero's model then shows as is.
        /// </summary>
        public bool PickedChoicesOnly { get; init; }
    }

    /// <summary>
    /// A particle written over another one, see <see cref="CharacterExportOptions.ReplaceDefaults"/>.
    /// </summary>
    sealed record ParticleReplacement(string Source, string Target);

    /// <summary>
    /// A game particle written with its references to particles that get written over pointed at copies of their game
    /// versions, see <see cref="CharacterExportPlan.ParticleRedirects"/>.
    /// </summary>
    /// <param name="Particle">The particle whose game version is written, as a source path.</param>
    /// <param name="Output">The source path it is written as.</param>
    /// <param name="Redirects">The copies to reference instead, by the particle written over they are copies of.</param>
    sealed record ParticleRedirect(string Particle, string Output, Dictionary<string, string> Redirects);

    /// <summary>
    /// Every file a character export writes, grouped by how it gets decompiled. Paths are package paths of compiled files
    /// (ending in "_c"), except <see cref="RawFiles"/> and the replacements, which use source paths.
    /// </summary>
    sealed class CharacterExportPlan
    {
        /// <summary>Models, decompiled with the custom model extractor, which also writes their meshes, animations and physics.</summary>
        public List<string> Models { get; } = [];

        /// <summary>Materials the models use, decompiled with the custom material exporter, which also writes their textures.</summary>
        public List<string> Materials { get; } = [];

        /// <summary>
        /// The copies of materials the models are pointed at instead, by the material's package source path, see
        /// <see cref="CharacterExportOptions.AlphaTestMaterials"/>.
        /// </summary>
        public Dictionary<string, string> MaterialCopies { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The dynamic params of material copies that are left out, so the copy keeps the value it is compiled with, by
        /// the material's package source path.
        /// </summary>
        public Dictionary<string, List<string>> FrozenMaterialParams { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Everything else that is compiled, decompiled with the built-in decompiler.</summary>
        public List<string> Resources { get; } = [];

        /// <summary>Files that are not compiled, copied as they are.</summary>
        public List<string> RawFiles { get; } = [];

        /// <summary>Models written over the default ones once everything is exported.</summary>
        public List<ModelReplacement> ModelReplacements { get; } = [];

        /// <summary>
        /// The materials to swap for the picked arcana style's versions of them, by the source model they are made for,
        /// see <see cref="ModelDocEditor.SelectBodyGroupChoices"/>.
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> StyleMaterialRemaps { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Particles written over the default ones once everything is exported.</summary>
        public List<ParticleReplacement> ParticleReplacements { get; } = [];

        /// <summary>Particles taken out of the models written over others, see <see cref="CharacterExportOptions.RemovedModelParticles"/>.</summary>
        public IReadOnlySet<string> RemovedModelParticles { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The particles the swapped in particles play that reference particles written over, rewritten to reference copies
        /// of the game's versions, and those copies. The game only swaps the particle it creates, so the parts keep
        /// playing the originals, e.g. an arcana effect whose parts use the empty effect it replaces as a placeholder
        /// child, which would otherwise contain itself.
        /// </summary>
        public List<ParticleRedirect> ParticleRedirects { get; } = [];

        /// <summary>
        /// The control points the equipped items set on exported particles, by particle source path, which are written
        /// into the particles so they show as with the items equipped, e.g. the color of an arcana style's effects.
        /// The particles written over others are copies, so they get them too.
        /// </summary>
        public Dictionary<string, Dictionary<int, Vector3>> ParticleControlPoints { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Particle swaps left out because every hero uses the particle they replace.</summary>
        public List<ParticleReplacement> SkippedSharedParticles { get; } = [];

        /// <summary>Equipped models that are neither worn by the hero nor written over another model, e.g. a summon's.</summary>
        public List<string> UnplacedModels { get; } = [];

        /// <summary>
        /// Default models of the hero's own slots written as empty models, since nothing is worn in their place: the
        /// persona has no item for them, see <see cref="CharacterLoadout.PersonaModels"/>, or the item was added to the
        /// hero's model, see <see cref="CharacterExportOptions.AnimateOwnParts"/>.
        /// </summary>
        public List<string> HiddenModels { get; } = [];

        /// <summary>Compiled icons copied into the game folder, as package paths.</summary>
        public List<IconReplacement> IconReplacements { get; } = [];

        /// <summary>Sprites written over others in a copy of the sprite sheet definitions in the game folder.</summary>
        public List<SpriteReplacement> SpriteReplacements { get; } = [];

        /// <summary>Default items written over in a copy of items_game.txt in the game folder.</summary>
        public List<DefaultItemSwap> DefaultItemSwaps { get; } = [];

        /// <summary>
        /// Models whose portrait settings are written over those of the model they replace, see
        /// <see cref="CharacterExportOptions.Portraits"/>.
        /// </summary>
        public List<(string Source, string Target)> PortraitReplacements { get; } = [];

        /// <summary>Sound event files rewritten with other events' definitions once everything is exported.</summary>
        public List<SoundEventEdit> SoundEventEdits { get; } = [];

        /// <summary>Things worth knowing about how the loadout was exported.</summary>
        public List<string> Notes { get; } = [];

        /// <summary>References that could not be found in the game files.</summary>
        public List<string> Missing { get; } = [];
    }

    /// <summary>
    /// Works out which files make up a hero with a set of items: the models, particles and sound events the items name
    /// in items_game.txt. Nothing they reference is exported on top, the addon loads it from the game like the game does.
    /// </summary>
    sealed class CharacterDependencyCollector
    {
        private static readonly HashSet<string> AssetExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".vmdl", ".vpcf", ".vsnap", ".vsndevts", ".vsnd",
        };

        private readonly Package package;
        private readonly GameFileLoader fileLoader;
        private readonly IProgress<string>? progress;

        private readonly Queue<(string Path, bool IncludeSounds)> queue = new();
        private readonly HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> styleMaterialRemaps = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (string File, KVObject Data)>? soundEvents;

        public CharacterDependencyCollector(Package package, GameFileLoader fileLoader, IProgress<string>? progress)
        {
            this.package = package;
            this.fileLoader = fileLoader;
            this.progress = progress;
        }

        public CharacterExportPlan Collect(CharacterLoadout loadout, CharacterExportOptions options, CancellationToken cancellationToken)
        {
            var plan = new CharacterExportPlan();

            AddHeroRoots(loadout, options);

            var equippedAssets = GetEquippedAssets(loadout);

            foreach (var item in loadout.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddItemRoots(loadout, item, options, equippedAssets, plan);
            }

            // Like the worn items' swaps, they are only exported as they are when the defaults are not written over
            if (options.ItemParticles && !options.ReplaceDefaults)
            {
                foreach (var picked in options.ParticleSwaps.Where(static picked => !CharacterLoadout.IsSamePath(picked.Source, picked.Target)))
                {
                    Enqueue(picked.Source);
                }
            }

            if (options.ReplaceDefaults)
            {
                AddReplacements(loadout, options, equippedAssets, plan);
                AddParticleRemovals(loadout, options, plan);
                AddParticleRedirects(plan, cancellationToken);
            }
            else
            {
                AddWornModels(loadout, options);
            }

            if (options.Pedestal)
            {
                AddPedestals(loadout, options, plan);
            }

            var keptModels = options.ReplaceDefaults && options.SkipUnchangedModels ? GetChangedModels(loadout, options, plan) : null;
            var skippedModels = 0;

            if (options.Icons)
            {
                foreach (var icon in options.IconReplacements)
                {
                    if (Exists(icon.Source))
                    {
                        plan.IconReplacements.Add(icon);
                    }
                    else
                    {
                        plan.Missing.Add(icon.Source);
                    }
                }
            }

            if (options.SpriteSheet)
            {
                plan.SpriteReplacements.AddRange(options.SpriteReplacements);
            }

            if (options.DefaultItemsInItemsGame)
            {
                plan.DefaultItemSwaps.AddRange(options.DefaultItemSwaps);

                foreach (var item in loadout.Items.Where(item => !item.Item.IsDefault
                    && !options.DefaultItemSwaps.Any(swap => swap.Item == item.Item.DefIndex)))
                {
                    plan.Notes.Add($"{item.Item.Name} is not in items_game.txt's default items, its slot \"{item.Item.Slot}\" has no default item");
                }

                if (loadout.Items.Any(static item => item.Unusual != null))
                {
                    plan.Notes.Add("Unusual effects are not in items_game.txt's default items, they only come with the exported particles");
                }
            }

            if (options.Sounds)
            {
                AddSoundReplacements(loadout, options, plan);
            }

            while (queue.TryDequeue(out var next))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (keptModels != null && next.Path.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) && !keptModels.Contains(next.Path))
                {
                    skippedModels++;
                    continue;
                }

                Visit(next.Path, next.IncludeSounds, plan);
            }

            if (skippedModels > 0)
            {
                plan.Notes.Add($"{skippedModels} model{(skippedModels == 1 ? " the loadout does not change was" : "s the loadout does not change were")} left out, the game already has {(skippedModels == 1 ? "it" : "them")}");
            }

            AddParticleFixes(plan, cancellationToken);

            foreach (var (particle, controlPoints) in loadout.ParticleControlPoints)
            {
                if (plan.Resources.Contains(particle + GameFileLoader.CompiledFileSuffix, StringComparer.OrdinalIgnoreCase))
                {
                    plan.ParticleControlPoints[particle] = controlPoints;
                }
            }

            if (options.Materials || options.RecolorOptions.Count > 0)
            {
                AddMaterials(plan, plan.Models, cancellationToken);
            }
            else if (options.ArcanaMaterials && plan.Models.Where(IsArcanaModel).ToList() is { Count: > 0 } arcanaModels)
            {
                AddMaterials(plan, arcanaModels, cancellationToken);
                plan.Notes.Add($"the materials of {arcanaModels.Count} arcana model{(arcanaModels.Count == 1 ? "" : "s")} were exported, the other models use the game's own");
            }

            if (options.AlphaTestMaterials)
            {
                AddMaterials(plan, plan.Models, cancellationToken);

                foreach (var material in plan.Materials)
                {
                    var source = NormalizePath(material[..^GameFileLoader.CompiledFileSuffix.Length]);
                    plan.MaterialCopies[source] = GetMaterialCopyPath(source);
                }

                plan.Notes.Add(plan.MaterialCopies.Count switch
                {
                    0 => "the exported models use no materials to copy",
                    var count => $"{count} material{(count == 1 ? " was" : "s were")} copied into {CharacterExportOptions.MaterialCopiesFolder} with alpha testing and the models point at them",
                });
            }

            AddFrozenMaterialParams(plan, cancellationToken);

            if (options.Portraits && options.ReplaceDefaults)
            {
                plan.PortraitReplacements.AddRange(plan.ModelReplacements
                    .Where(static replacement => !CharacterLoadout.IsSamePath(replacement.Source, replacement.Target))
                    .Select(static replacement => (NormalizePath(replacement.Source), NormalizePath(replacement.Target)))
                    .Distinct());
            }

            foreach (var (model, remaps) in styleMaterialRemaps)
            {
                plan.StyleMaterialRemaps[model] = remaps;
            }

            return plan;
        }

        /// <summary>
        /// The models that end up changed in the addon, see <see cref="CharacterExportOptions.SkipUnchangedModels"/>: the
        /// ones written over others or added to them, and the ones exported as they are for want of a model to be written
        /// over, which would otherwise be lost.
        /// </summary>
        private static HashSet<string> GetChangedModels(CharacterLoadout loadout, CharacterExportOptions options, CharacterExportPlan plan)
        {
            var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var replacement in plan.ModelReplacements)
            {
                models.Add(replacement.Source);
                models.UnionWith(replacement.Merged.Select(static merged => merged.Model));
            }

            models.UnionWith(plan.UnplacedModels);

            if (options.ItemModels)
            {
                models.UnionWith(loadout.AdditionalWearables
                    .Where(wearable => !loadout.IsAppliedByGame(wearable.Item))
                    .Select(static wearable => NormalizePath(wearable.Model)));
            }

            if (options.Pedestal)
            {
                models.UnionWith(loadout.Pedestals.Select(static pedestal => NormalizePath(pedestal.Model)));
            }

            return models;
        }

        /// <summary>
        /// Writes the models the hero wears that create a particle taken out, see
        /// <see cref="CharacterExportOptions.RemovedModelParticles"/>, over themselves when nothing else is written over
        /// them, so they are written without it.
        /// </summary>
        private void AddParticleRemovals(CharacterLoadout loadout, CharacterExportOptions options, CharacterExportPlan plan)
        {
            plan.RemovedModelParticles = options.RemovedModelParticles;

            if (options.RemovedModelParticles.Count == 0)
            {
                return;
            }

            var worn = new List<string>();

            if (options.HeroModel && (loadout.HeroModel ?? loadout.Hero.Model) is { } heroModel)
            {
                worn.Add(heroModel);
                worn.AddRange(loadout.VariantModelSwaps.Select(static swap => swap.Model));
            }

            if (options.ItemModels)
            {
                foreach (var item in loadout.Items)
                {
                    // Models that replace another one are written already, so only the ones the game shows as they are
                    if (loadout.IsWornByHero(item.Item.Slot) && loadout.GetItemModel(item) is { } itemModel
                        && CharacterLoadout.IsSamePath(itemModel, loadout.GetDefaultModel(item.Item.Slot)))
                    {
                        worn.Add(itemModel);
                    }
                }
            }

            foreach (var model in worn.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (plan.ModelReplacements.Any(replacement => CharacterLoadout.IsSamePath(replacement.Source, model)
                    || replacement.Merged.Any(merged => CharacterLoadout.IsSamePath(merged.Model, model))))
                {
                    continue;
                }

                List<string> particles;

                try
                {
                    particles = ModelDocEditor.GetOwnParticles(fileLoader, model);
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read the particles of \"{model}\": {e.Message}");
                    continue;
                }

                if (!particles.Any(options.RemovedModelParticles.Contains))
                {
                    continue;
                }

                // Nothing about the model changes but the particles, or it would have been written over already
                plan.ModelReplacements.Add(new ModelReplacement(model, model, 0, []));
                plan.Notes.Add($"{model} was written as it is to take particles out of it");

                Enqueue(model);
            }
        }

        /// <summary>
        /// Whether a model switches its meshes by the arcana level, see <see cref="CharacterLoadout.ArcanaBodyGroup"/>,
        /// whose materials need copies of their own, see <see cref="CharacterExportOptions.ArcanaMaterials"/>.
        /// </summary>
        private bool IsArcanaModel(string compiledPath)
        {
            try
            {
                using var resource = fileLoader.LoadFile(compiledPath);

                return resource?.DataBlock is Model model
                    && model.MeshGroups.BodyGroups.Any(static group => group.Name.Equals(CharacterLoadout.ArcanaBodyGroup, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read \"{compiledPath}\": {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds the materials the models use, including those of their reference meshes and material groups.
        /// </summary>
        /// <param name="include">Picks which of them to add, all when null. Asked about the ones added already too.</param>
        private void AddMaterials(CharacterExportPlan plan, List<string> models, CancellationToken cancellationToken, Func<string, bool>? include = null)
        {
            var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddReferences(Resource? resource)
            {
                foreach (var reference in resource?.ExternalReferences?.ResourceRefInfoList ?? [])
                {
                    if (reference.Name?.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        materials.Add(NormalizePath(reference.Name));
                    }
                }
            }

            foreach (var modelPath in models)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var resource = fileLoader.LoadFile(modelPath);
                    AddReferences(resource);

                    if (resource?.DataBlock is not Model model)
                    {
                        continue;
                    }

                    foreach (var (_, groupMaterials) in model.GetMaterialGroups())
                    {
                        foreach (var material in groupMaterials)
                        {
                            if (!string.IsNullOrEmpty(material))
                            {
                                materials.Add(NormalizePath(material));
                            }
                        }
                    }

                    foreach (var reference in model.GetReferenceMeshNamesAndLoD())
                    {
                        using var mesh = fileLoader.LoadFileCompiled(reference.MeshName);
                        AddReferences(mesh);
                    }
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read the materials of \"{modelPath}\": {e.Message}");
                }
            }

            foreach (var material in materials.Order(StringComparer.OrdinalIgnoreCase))
            {
                var compiledPath = material + GameFileLoader.CompiledFileSuffix;

                if (!Exists(compiledPath))
                {
                    if (!plan.Missing.Contains(material, StringComparer.OrdinalIgnoreCase))
                    {
                        plan.Missing.Add(material);
                    }
                }
                else if ((include == null || include(compiledPath)) && !plan.Materials.Contains(compiledPath, StringComparer.OrdinalIgnoreCase))
                {
                    plan.Materials.Add(compiledPath);
                }
            }
        }

        /// <summary>
        /// Where a copy of a material goes, see <see cref="CharacterExportPlan.MaterialCopies"/>.
        /// </summary>
        private static string GetMaterialCopyPath(string source)
        {
            var path = source.StartsWith("materials/", StringComparison.OrdinalIgnoreCase) ? source["materials/".Length..] : source;
            return $"{CharacterExportOptions.MaterialCopiesFolder}/{path}";
        }

        /// <summary>
        /// Copies the materials that read a material attribute the models animate, e.g. the fade of Spectre's arcana
        /// blades, see <see cref="CharacterExportPlan.FrozenMaterialParams"/>. The decompiled animations do not set those
        /// attributes, so the copies keep the value the material is compiled with, otherwise the blades stay faded out.
        /// </summary>
        private void AddFrozenMaterialParams(CharacterExportPlan plan, CancellationToken cancellationToken)
        {
            foreach (var modelPath in plan.Models.ToList())
            {
                List<string> attributes;

                try
                {
                    using var resource = fileLoader.LoadFile(modelPath);

                    attributes = resource?.DataBlock is Model model
                        ? [.. (model.Data.GetArray("m_AnimatedMaterialAttributes") ?? []).Select(static attribute => attribute.GetStringProperty("m_AttributeName")).Where(static name => !string.IsNullOrEmpty(name))]
                        : [];
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read the animated material attributes of \"{modelPath}\": {e.Message}");
                    continue;
                }

                if (attributes.Count == 0)
                {
                    continue;
                }

                var reads = new Regex($@"\b(?:{string.Join('|', attributes.Select(Regex.Escape))})\b", RegexOptions.CultureInvariant);

                AddMaterials(plan, [modelPath], cancellationToken, compiledPath =>
                {
                    using var resource = fileLoader.LoadFile(compiledPath);

                    if (resource?.DataBlock is not Material material)
                    {
                        return false;
                    }

                    var frozen = material.DynamicExpressions.Where(expression => reads.IsMatch(expression.Value)).Select(static expression => expression.Key).ToList();

                    if (frozen.Count == 0)
                    {
                        return false;
                    }

                    var source = NormalizePath(compiledPath[..^GameFileLoader.CompiledFileSuffix.Length]);
                    plan.MaterialCopies.TryAdd(source, GetMaterialCopyPath(source));
                    plan.FrozenMaterialParams[source] = frozen;
                    plan.Notes.Add($"{source} was copied with {string.Join(", ", frozen)} kept as compiled, the animations that set it are not decompiled");
                    return true;
                });
            }
        }

        private void AddHeroRoots(CharacterLoadout loadout, CharacterExportOptions options)
        {
            var hero = loadout.Hero;

            if (options.HeroSounds && hero.GameSoundsFile != null)
            {
                Enqueue(hero.GameSoundsFile, includeSounds: options.IncludeAudio);
            }

            if (options.HeroVoice && hero.VoiceFile != null)
            {
                Enqueue(hero.VoiceFile, includeSounds: options.IncludeAudio);
            }

            if (options.HeroParticles && hero.ParticleFolder != null)
            {
                var folder = NormalizePath(hero.ParticleFolder).TrimEnd('/');

                foreach (var entry in GetPackageEntries("vpcf_c"))
                {
                    var directory = entry.DirectoryName.Replace('\\', '/');

                    if (directory.Equals(folder, StringComparison.OrdinalIgnoreCase)
                        || directory.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        Enqueue(entry.GetFullPath());
                    }
                }
            }
        }

        /// <summary>
        /// Models and particles the hero ends up with, which decides whether swaps that only apply on top of another item
        /// are needed.
        /// </summary>
        private static HashSet<string> GetEquippedAssets(CharacterLoadout loadout)
        {
            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (loadout.HeroModel != null)
            {
                assets.Add(NormalizePath(loadout.HeroModel));
            }

            foreach (var item in loadout.Items)
            {
                if (loadout.GetItemModel(item) is { } model)
                {
                    assets.Add(NormalizePath(model));
                }

                foreach (var modifier in item.Modifiers)
                {
                    if (!IsConditionalSwap(modifier.Type) && IsAssetPath(modifier.Modifier))
                    {
                        assets.Add(NormalizePath(modifier.Modifier));
                    }
                }
            }

            return assets;
        }

        /// <summary>
        /// Swaps that replace another item's asset with a version made to go with this item, e.g. a refit of a head that
        /// clips with an arcana. They only matter when that other item is equipped too.
        /// </summary>
        private static bool IsConditionalSwap(string type) => type is "model" or "particle_combined";

        /// <summary>
        /// Queues what an item swaps in or creates, other than models the hero wears, which depend on whether the default
        /// assets get replaced. Only the kinds of assets a hero addon can use are looked at: the rest of what items_game.txt
        /// names, like loading screens, couriers or map effects, has nothing to do with the hero.
        /// </summary>
        private void AddItemRoots(CharacterLoadout loadout, EquippedItem item, CharacterExportOptions options, HashSet<string> equippedAssets, CharacterExportPlan plan)
        {
            foreach (var modifier in item.Modifiers)
            {
                // Only shown in the loadout screen, which a custom game does not have
                if (modifier.LoadoutOnly && !options.Pedestal)
                {
                    continue;
                }

                switch (modifier.Type)
                {
                    case "sound" when options.ItemSounds && modifier.Modifier != null:
                        AddSoundEvent(loadout.Hero, modifier.Modifier, options.IncludeAudio, plan);
                        break;

                    // Swapped in particles are exported as the replacements of what they swap when defaults get replaced
                    case "particle" or "particle_combined" when options.ItemParticles && !options.ReplaceDefaults:
                        if (IsAssetPath(modifier.Modifier)
                            && (!IsConditionalSwap(modifier.Type) || (modifier.Asset != null && equippedAssets.Contains(NormalizePath(modifier.Asset)))))
                        {
                            Enqueue(modifier.Modifier);
                        }

                        break;

                    case "particle_create" when options.ItemParticles && !modifier.LoadoutOnly:
                        var effect = new CreatedEffect(item, modifier);

                        if (IsParticlePath(modifier.Modifier) && IsEffectEnabled(loadout, options, effect))
                        {
                            Enqueue(effect.Particle);
                        }

                        break;

                    case "particle_create" or "particle_snapshot" when options.ItemParticles && IsAssetPath(modifier.Modifier):
                        Enqueue(modifier.Modifier);
                        break;

                    // Other units, like summons, which have no default model of the hero's to be written over
                    case "entity_model" when options.ItemModels && IsAssetPath(modifier.Modifier) && !loadout.IsHeroEntity(modifier.Asset):
                        Enqueue(modifier.Modifier);
                        break;

                    case "hero_model_change" when options.ItemModels && !options.ReplaceDefaults && IsAssetPath(modifier.Modifier):
                        Enqueue(modifier.Modifier);
                        break;
                }
            }
        }

        /// <summary>
        /// Queues the models the hero wears, as they are, when they are not written over the default ones.
        /// </summary>
        private void AddWornModels(CharacterLoadout loadout, CharacterExportOptions options)
        {
            if (options.HeroModel)
            {
                if (loadout.Hero.Model != null)
                {
                    Enqueue(loadout.Hero.Model);
                }

                if (loadout.HeroModel != null)
                {
                    Enqueue(loadout.HeroModel);
                }

                foreach (var (_, variantModel, _) in loadout.VariantModelSwaps)
                {
                    Enqueue(variantModel);
                }
            }

            if (!options.ItemModels)
            {
                return;
            }

            foreach (var item in loadout.Items)
            {
                if (loadout.GetItemModel(item) is { } model)
                {
                    Enqueue(model);
                }
            }

            foreach (var (_, wearable) in loadout.AdditionalWearables)
            {
                Enqueue(wearable);
            }

            foreach (var unitModel in loadout.UnitDefaultModels)
            {
                Enqueue(unitModel);
            }
        }

        /// <summary>
        /// Works out what gets written over the hero's default assets, see <see cref="CharacterExportOptions.ReplaceDefaults"/>,
        /// and queues what that needs. What default items do is left alone, the game still applies it.
        /// </summary>
        private void AddReplacements(CharacterLoadout loadout, CharacterExportOptions options, HashSet<string> equippedAssets, CharacterExportPlan plan)
        {
            var hero = loadout.Hero;

            // Particles of items without a model of their own play on the hero, as do those of models added to the hero's
            var heroParticles = new List<VariantParticle>();
            var merged = new List<MergedModel>();

            // Items with no model of the hero's to be written over, with the models their own swaps write over
            var slotless = new List<(MergedModel Model, List<VariantParticle> Particles, List<string> Companions, List<string> Variants)>();

            // What the hero's other models get added, by variant, e.g. the night versions of items that have no slot model
            var variantMerged = new Dictionary<int, List<MergedModel>>();
            var replacedParticles = new Dictionary<string, ParticleReplacement>(StringComparer.OrdinalIgnoreCase);
            var usedParticles = GetUsedParticles(loadout, equippedAssets);

            // Picked by hand, they win over what the worn items swap the same particles for
            var pickedParticles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var picked in options.ItemParticles ? options.ParticleSwaps : [])
            {
                if (!pickedParticles.Add(picked.Target) || CharacterLoadout.IsSamePath(picked.Source, picked.Target))
                {
                    continue;
                }

                replacedParticles.Add(picked.Target, picked);
                plan.ParticleReplacements.Add(picked);
                Enqueue(picked.Source);
            }
            // The hero's other variants are written over like units, their items have no model of their own either
            var unitSwaps = (options.ItemModels ? loadout.UnitModelSwaps : [])
                .Concat(options.HeroModel ? loadout.VariantModelSwaps : [])
                .ToList();
            var personaModels = options.ItemModels ? loadout.PersonaModels : null;
            var rename = options.RenameModels;

            (int Skin, Dictionary<string, int> BodyGroups) GetLook(string model, int skin)
                => GetModelLook(loadout, model, skin, styleRemaps: !rename);

            if (options.ItemModels)
            {
                foreach (var unitModel in loadout.UnitDefaultModels)
                {
                    Enqueue(unitModel);
                }

                foreach (var (item, abilityModel, defaultModel) in loadout.AbilityModelSwaps.Where(swap => !loadout.IsAppliedByGame(swap.Item)))
                {
                    var (skin, bodyGroups) = GetLook(abilityModel, item.Skin);

                    Enqueue(abilityModel);

                    plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(abilityModel), NormalizePath(defaultModel), skin, [])
                    {
                        BodyGroups = bodyGroups,
                        Rename = rename,
                    });
                }
            }

            foreach (var item in loadout.Items)
            {
                var isDefault = loadout.IsAppliedByGame(item);
                List<VariantParticle> createdEffects = options.ItemParticles && !isDefault
                    ? [.. loadout.CreatedEffects
                        .Where(effect => effect.Item == item && IsEffectEnabled(loadout, options, effect))
                        .Select(static effect => new VariantParticle(effect.Particle, effect.Modifier.RequiredModelVariant))]
                    : [];
                var createdParticles = GetVariantParticles(createdEffects, 0);

                var model = options.ItemModels ? loadout.GetItemModel(item) : null;
                var itemUnitSwaps = unitSwaps.Where(swap => swap.Item == item).ToList();

                if (itemUnitSwaps.Count > 0)
                {
                    // Dresses a unit the hero creates, its own model is only what the loadout screen shows
                    foreach (var (_, unitModel, defaultModel) in itemUnitSwaps)
                    {
                        var (skin, bodyGroups) = GetLook(unitModel, item.Skin);

                        Enqueue(unitModel);

                        // Default items name the unit's own model too
                        if (!CharacterLoadout.IsSamePath(unitModel, defaultModel) || skin != 0 || createdParticles.Count > 0 || bodyGroups.Count > 0)
                        {
                            plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(unitModel), NormalizePath(defaultModel), skin, createdParticles)
                            {
                                BodyGroups = bodyGroups,
                                Rename = rename,
                            });
                        }
                    }
                }
                else if (model == null)
                {
                    if (loadout.IsWornByHero(item.Item.Slot))
                    {
                        heroParticles.AddRange(createdEffects);

                        // The item takes the slot without wearing anything in it, e.g. an arcana head that comes with
                        // the hero model it swaps in, so the slot's default model would show inside it
                        if (options.ItemModels && !isDefault && personaModels == null && loadout.GetDefaultModel(item.Item.Slot) is { } emptiedModel)
                        {
                            plan.HiddenModels.Add(NormalizePath(emptiedModel));
                            plan.Notes.Add($"{item.Item.Name} wears no model in its slot, {NormalizePath(emptiedModel)} was hidden");
                        }
                    }
                }
                else if (personaModels != null && !CharacterLoadout.IsPersonaSlot(item.Item.Slot) && loadout.IsWornByHero(item.Item.Slot))
                {
                    // The persona's own items take the place of the hero's
                    if (!isDefault)
                    {
                        plan.Notes.Add($"{NormalizePath(model)} is not worn by the persona, it was left out");
                    }
                }
                else
                {
                    Enqueue(model);

                    var defaultModel = personaModels != null && CharacterLoadout.IsPersonaSlot(item.Item.Slot)
                        ? personaModels.Targets.GetValueOrDefault(item)
                        : loadout.GetDefaultModel(item.Item.Slot);

                    if (options.AnimateOwnParts && options.HeroModel && hero.Model != null && GetAnimatedBones(loadout, item, model) is { Count: > 0 } animatedBones)
                    {
                        // Written over the slot's default model it would be combined into the hero and stand still
                        var (skin, bodyGroups) = GetModelLook(loadout, model, item.Skin, styleRemaps: false);
                        merged.Add(new MergedModel(NormalizePath(model), skin, bodyGroups)
                        {
                            AnimatedBones = animatedBones,
                            GestureModifiers = GetGestureModifiers(hero, item),
                            PickedChoicesOnly = true,
                        });
                        heroParticles.AddRange(createdEffects);

                        if (defaultModel != null)
                        {
                            plan.HiddenModels.Add(NormalizePath(defaultModel));
                        }

                        plan.Notes.Add($"{NormalizePath(model)} animates parts of its own, it was added to the hero's model with its animations played on them");
                    }
                    else if (options.AnimateOwnParts && options.HeroModel && hero.Model != null && defaultModel != null && item.Item.SkipModelCombine
                        && loadout.IsDefaultCombined(item.Item.Slot) && loadout.IsWornByHero(item.Item.Slot) && HasBoneCloth(model))
                    {
                        // Written over a default the game combines into the hero, its cloth would hang stretched off bones
                        // nothing moves any more, e.g. the Windranger arcana's hair
                        var (skin, bodyGroups) = GetModelLook(loadout, model, item.Skin, styleRemaps: false);
                        merged.Add(new MergedModel(NormalizePath(model), skin, bodyGroups) { PickedChoicesOnly = true });
                        heroParticles.AddRange(createdEffects);
                        plan.HiddenModels.Add(NormalizePath(defaultModel));
                        plan.Notes.Add($"{NormalizePath(model)} has cloth the game keeps apart from the hero, it was added to the hero's model");
                    }
                    else if (defaultModel != null)
                    {
                        var (skin, bodyGroups) = GetLook(model, item.Skin);

                        // Pieces the game keeps apart from the hero play the versions of their own sequences for the
                        // hero's activity modifiers, which nothing sets on them in the addon either
                        var itemActivityModifiers = loadout.IsWornByHero(item.Item.Slot) ? loadout.ActivityModifiers : [];

                        // Also covers default items that another item swaps for a refit, or whose parts an arcana hides
                        if (!CharacterLoadout.IsSamePath(model, defaultModel) || skin != 0 || createdParticles.Count > 0 || bodyGroups.Count > 0)
                        {
                            plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(model), NormalizePath(defaultModel), skin, createdParticles)
                            {
                                BodyGroups = bodyGroups,
                                ActivityModifiers = itemActivityModifiers,
                                Rename = rename,
                            });
                        }

                        // The hero's other models wear the item's versions for them, e.g. at night Night Stalker's items
                        // swap for their night models, which would otherwise stay the default items'
                        if (personaModels == null || !CharacterLoadout.IsPersonaSlot(item.Item.Slot))
                        {
                            foreach (var (variant, variantModel, defaultVariantModel) in loadout.GetVariantModelSwaps(item))
                            {
                                var (variantSkin, variantBodyGroups) = GetLook(variantModel, item.Skin);

                                Enqueue(variantModel);
                                plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(variantModel), NormalizePath(defaultVariantModel), variantSkin,
                                    GetVariantParticles(createdEffects, variant))
                                {
                                    BodyGroups = variantBodyGroups,
                                    ActivityModifiers = itemActivityModifiers,
                                    Rename = rename,
                                });
                            }
                        }
                    }
                    else if (!isDefault && loadout.IsWornByHero(item.Item.Slot))
                    {
                        // Nothing of the hero's to write it over, e.g. Earthshaker's arcana head, since his head is part of
                        // his model. Left as is it would not show at all, so it goes with a model it swaps to fit it, else
                        // the hero's, renamed or not.
                        var (skin, bodyGroups) = GetModelLook(loadout, model, item.Skin, styleRemaps: false);
                        var companions = item.Modifiers
                            .Where(static modifier => modifier.Type == "model" && IsAssetPath(modifier.Asset))
                            .Select(static modifier => NormalizePath(modifier.Asset!))
                            .ToList();

                        slotless.Add((new MergedModel(NormalizePath(model), skin, bodyGroups) { PickedChoicesOnly = true }, createdEffects, companions,
                            item.Item.VariantModelPlayers));
                    }
                    else if (!isDefault)
                    {
                        plan.UnplacedModels.Add(NormalizePath(model));
                    }
                }

                if (options.ItemModels)
                {
                    AddTransformationReplacements(loadout, item, rename, plan);
                }

                // Shared particles have an option of their own, so they can be exported without the hero's
                if (!(options.ItemParticles || options.ReplaceSharedParticles) || isDefault)
                {
                    continue;
                }

                foreach (var modifier in item.Modifiers)
                {
                    if (modifier.Type is not ("particle" or "particle_combined")
                        || !IsParticlePath(modifier.Asset)
                        || !IsParticlePath(modifier.Modifier)
                        || CharacterLoadout.IsSamePath(modifier.Asset, modifier.Modifier))
                    {
                        continue;
                    }

                    if (modifier.Type == "particle_combined" && !equippedAssets.Contains(NormalizePath(modifier.Asset)))
                    {
                        continue;
                    }

                    var replacement = new ParticleReplacement(NormalizePath(modifier.Modifier), NormalizePath(modifier.Asset));

                    if (pickedParticles.Contains(replacement.Target))
                    {
                        continue;
                    }

                    // Items also refit the particles of other items, which only matters with those equipped
                    if (IsEconParticle(replacement.Target) && !usedParticles.Contains(replacement.Target))
                    {
                        continue;
                    }

                    if (IsHeroParticle(hero, replacement.Target))
                    {
                        if (!options.ItemParticles)
                        {
                            continue;
                        }
                    }
                    // An empty placeholder like status_effect_null stands in for unrelated effects everywhere
                    else if (!options.ReplaceSharedParticles || Path.GetFileNameWithoutExtension(replacement.Target).EndsWith("null", StringComparison.OrdinalIgnoreCase))
                    {
                        plan.SkippedSharedParticles.Add(replacement);
                        continue;
                    }

                    // A style can swap the same particle twice, the file can only hold one of them
                    if (replacedParticles.TryGetValue(replacement.Target, out var existing))
                    {
                        if (!CharacterLoadout.IsSamePath(existing.Source, replacement.Source))
                        {
                            plan.Notes.Add($"{replacement.Target} is swapped for both {existing.Source} and {replacement.Source}, the first one is used");
                        }

                        continue;
                    }

                    replacedParticles.Add(replacement.Target, replacement);
                    plan.ParticleReplacements.Add(replacement);
                    Enqueue(replacement.Source);
                }
            }

            foreach (var (model, particles, companions, variants) in slotless)
            {
                var companionIndex = plan.ModelReplacements.FindIndex(replacement => companions.Contains(replacement.Target, StringComparer.OrdinalIgnoreCase));

                if (companionIndex >= 0 && rename)
                {
                    // The swapped in model only fills in under the item, e.g. a face refit hidden inside a helmet
                    var companion = plan.ModelReplacements[companionIndex];

                    plan.ModelReplacements[companionIndex] = new ModelReplacement(model.Model, companion.Target, model.Skin,
                        [.. companion.Particles.Union(GetVariantParticles(particles, 0), StringComparer.OrdinalIgnoreCase)])
                    {
                        BodyGroups = model.BodyGroups,
                        Merged = companion.Merged,
                        ActivityModifiers = companion.ActivityModifiers,
                        Rename = true,
                    };

                    plan.Notes.Add($"{model.Model} has no default model of its own, it was renamed over {companion.Target} in place of {companion.Source}, which it swaps to fit under it");
                }
                else if (companionIndex >= 0)
                {
                    var companion = plan.ModelReplacements[companionIndex];

                    companion.Merged.Add(model);
                    companion.Particles.AddRange(GetVariantParticles(particles, 0).Except(companion.Particles, StringComparer.OrdinalIgnoreCase));
                    plan.Notes.Add($"{model.Model} has no default model of its own, it was added to {companion.Target}, which it swaps to fit it");
                }
                else
                {
                    merged.Add(model);
                    heroParticles.AddRange(particles);
                    plan.Notes.Add($"{model.Model} has no default model of its own, it was added to the hero's model");

                    for (var variant = 1; variant <= variants.Count; variant++)
                    {
                        var (variantSkin, variantBodyGroups) = GetModelLook(loadout, variants[variant - 1], model.Skin, styleRemaps: false);

                        Enqueue(variants[variant - 1]);

                        if (!variantMerged.TryGetValue(variant, out var list))
                        {
                            variantMerged[variant] = list = [];
                        }

                        list.Add(new MergedModel(NormalizePath(variants[variant - 1]), variantSkin, variantBodyGroups) { PickedChoicesOnly = true });
                    }
                }
            }

            if (options.ItemModels)
            {
                foreach (var (item, wearable) in loadout.AdditionalWearables.Where(wearable => !loadout.IsAppliedByGame(wearable.Item)))
                {
                    if (rename)
                    {
                        plan.Notes.Add($"{NormalizePath(wearable)} is an extra model items wear and has no default model to be renamed over, it was exported as is");
                    }
                    else if (options.MergeAdditionalWearables)
                    {
                        var (skin, bodyGroups) = GetModelLook(loadout, wearable, item.Skin);
                        merged.Add(new MergedModel(NormalizePath(wearable), skin, bodyGroups));
                    }

                    Enqueue(wearable);
                }
            }

            if (personaModels != null)
            {
                plan.HiddenModels.AddRange(personaModels.Hidden.Select(NormalizePath));
            }

            if (!options.HeroModel || hero.Model == null)
            {
                foreach (var model in merged)
                {
                    plan.UnplacedModels.Add(model.Model);
                    plan.Notes.Add($"{model.Model} has no slot of its own and can only be added to the hero's model, which is not exported");
                }

                return;
            }

            var heroModel = loadout.HeroModel ?? hero.Model;
            var (heroSkin, heroBodyGroups) = GetLook(heroModel, loadout.HeroSkin);
            var activityModifiers = loadout.ActivityModifiers;

            Enqueue(heroModel);

            var dayHeroParticles = GetVariantParticles(heroParticles, 0);

            if (!CharacterLoadout.IsSamePath(heroModel, hero.Model) || heroSkin != 0 || dayHeroParticles.Count > 0 || merged.Count > 0 || heroBodyGroups.Count > 0
                || activityModifiers.Count > 0)
            {
                plan.ModelReplacements.Add(new ModelReplacement(
                    NormalizePath(heroModel),
                    NormalizePath(hero.Model),
                    heroSkin,
                    dayHeroParticles)
                {
                    BodyGroups = heroBodyGroups,
                    Merged = merged,
                    ActivityModifiers = activityModifiers,
                    Rename = rename,
                    RangedAttack = hero.RangedAttack,
                });
            }

            // The hero's other models wear the night versions too, e.g. Night Stalker's, which he turns into at night
            foreach (var (variant, variantModels) in variantMerged)
            {
                if (variant >= hero.VariantModels.Count)
                {
                    continue;
                }

                var target = NormalizePath(hero.VariantModels[variant]);
                var existing = plan.ModelReplacements.FindIndex(replacement => CharacterLoadout.IsSamePath(replacement.Target, target));
                var variantParticles = GetVariantParticles(heroParticles, variant);

                if (existing >= 0)
                {
                    var replacement = plan.ModelReplacements[existing];
                    replacement.Merged.AddRange(variantModels);
                    replacement.Particles.AddRange(variantParticles.Except(replacement.Particles, StringComparer.OrdinalIgnoreCase));
                    continue;
                }

                Enqueue(target);
                plan.ModelReplacements.Add(new ModelReplacement(target, target, 0, variantParticles)
                {
                    Merged = variantModels,
                    ActivityModifiers = activityModifiers,
                    RangedAttack = hero.RangedAttack,
                });
            }
        }

        /// <summary>
        /// The abilities an item has the game play a gesture for, from its "apply_&lt;hero&gt;_&lt;ability&gt;_gesture"
        /// named values, e.g. "press_the_attack" for Legion Commander's Hell's Legion back. The ability's buff puts the
        /// activity modifier of the same name on the hero's sequences, see <see cref="MergedModel.GestureModifiers"/>.
        /// </summary>
        private static List<string> GetGestureModifiers(HeroDefinition hero, EquippedItem item)
        {
            var prefix = $"apply_{hero.ShortName}_";
            const string Suffix = "_gesture";

            return [.. item.Modifiers
                .Where(modifier => modifier.Type == "named_value" && modifier.Value != 0
                    && modifier.Asset is { } name
                    && name.Length > prefix.Length + Suffix.Length
                    && name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                .Select(modifier => modifier.Asset![prefix.Length..^Suffix.Length])
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>
        /// The bones an item's model moves with animations of its own that the hero's model does not have, e.g. the
        /// wind-up key on Morphling's automaton or the wings of Legion Commander's Hell's Legion back. The game keeps such
        /// items apart from the hero so they play their own sequences, but an addon hero's wearables are all combined
        /// into it, where nothing would move those bones. Only for persona pieces and items that play their own cycle,
        /// see <see cref="EconItem.PlaysOwnCycle"/>. Empty for items the game combines too, or whose model only has the
        /// hero's bones, and for other items of the hero's own slots: arcana pieces are kept apart as well, e.g.
        /// Earthshaker's and Drow Ranger's, but follow the hero's sequences and have to be written over their default
        /// models like other items.
        /// </summary>
        /// <summary>
        /// Whether a model has cloth on chains of its bones, which <see cref="ModelDocEditor.MergeModel"/> can carry over.
        /// Cloth on a proxy mesh's vertices is set up outside the soft body and is not.
        /// </summary>
        private bool HasBoneCloth(string model)
        {
            try
            {
                using var resource = fileLoader.LoadFileCompiled(NormalizePath(model));

                return resource?.DataBlock is Model compiledModel && compiledModel.GetEmbeddedPhys()?.FeModel is { } cloth
                    && !cloth.CtrlName.Any(static name => name.StartsWith("$cloth_m", StringComparison.Ordinal));
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read the cloth of \"{model}\": {e.Message}");
                return false;
            }
        }

        private List<string> GetAnimatedBones(CharacterLoadout loadout, EquippedItem item, string model)
        {
            if (!item.Item.SkipModelCombine
                || !(CharacterLoadout.IsPersonaSlot(item.Item.Slot) || item.Item.PlaysOwnCycle)
                || !loadout.IsWornByHero(item.Item.Slot)
                || (loadout.HeroModel ?? loadout.Hero.Model) is not { } heroModelPath)
            {
                return [];
            }

            try
            {
                using var resource = fileLoader.LoadFileCompiled(NormalizePath(model));
                using var heroResource = fileLoader.LoadFileCompiled(NormalizePath(heroModelPath));

                if (resource?.DataBlock is not Model itemModel || heroResource?.DataBlock is not Model heroModel)
                {
                    return [];
                }

                var animated = itemModel.GetAllAnimations(fileLoader)
                    .Any(static animation => animation is SequenceAnimation { IsLooping: true, Hidden: false }
                        && !ModelDocEditor.IsLoadoutAnimation(animation.Name));

                if (!animated)
                {
                    return [];
                }

                var heroBones = heroModel.Skeleton.Bones.Select(static bone => bone.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                return [.. itemModel.Skeleton.Bones.Select(static bone => bone.Name).Where(bone => !heroBones.Contains(bone))];
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read the bones of \"{model}\": {e.Message}");
                return [];
            }
        }

        /// <summary>
        /// Works out <see cref="CharacterExportPlan.ParticleRedirects"/>: walks what the swapped in particles play in the
        /// game, and keeps whatever references a particle that gets written over on a copy of its game version.
        /// </summary>
        private void AddParticleRedirects(CharacterExportPlan plan, CancellationToken cancellationToken)
        {
            var replaced = plan.ParticleReplacements.Select(static replacement => replacement.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (replaced.Count == 0)
            {
                return;
            }

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(plan.ParticleReplacements.Select(static replacement => replacement.Source));

            while (queue.TryDequeue(out var particle))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!visited.Add(particle))
                {
                    continue;
                }

                var references = GetParticleReferences(particle);
                var redirects = references
                    .Where(replaced.Contains)
                    .ToDictionary(static target => target, GetOriginalCopyPath, StringComparer.OrdinalIgnoreCase);

                // Only reached through a reference, so the copy of its game version is needed
                var isReplaced = replaced.Contains(particle);

                if (redirects.Count > 0 || isReplaced)
                {
                    plan.ParticleRedirects.Add(new ParticleRedirect(particle, isReplaced ? GetOriginalCopyPath(particle) : particle, redirects));
                }

                foreach (var reference in references)
                {
                    queue.Enqueue(reference);
                }
            }
        }

        /// <summary>
        /// Adds to <see cref="CharacterExportPlan.ParticleRedirects"/> the fixed copies of the broken game particles the
        /// exported particles play, see <see cref="ParticleFixes"/>, and of the game particles in between, which point at
        /// the copies. The exported particles are written with their references pointed at the copies too.
        /// </summary>
        private void AddParticleFixes(CharacterExportPlan plan, CancellationToken cancellationToken)
        {
            var exported = plan.Resources
                .Select(static path => NormalizePath(path[..^GameFileLoader.CompiledFileSuffix.Length]))
                .Where(IsParticlePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var references = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var affected = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            // Whether a particle is broken or plays one that is, a particle that plays itself is not while it is walked
            bool IsAffected(string particle)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (affected.TryGetValue(particle, out var known))
                {
                    return known;
                }

                affected[particle] = false;
                var isAffected = ParticleFixes.IsBroken(particle);

                // Every reference is walked, the redirects need to know about each one
                foreach (var reference in references[particle] = GetParticleReferences(particle))
                {
                    isAffected |= IsAffected(reference);
                }

                affected[particle] = isAffected;
                return isAffected;
            }

            string GetOutput(string particle) => exported.Contains(particle) ? particle : ParticleFixes.GetCopyPath(particle);

            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(exported.Where(IsAffected));

            while (pending.TryDequeue(out var particle))
            {
                if (!written.Add(particle))
                {
                    continue;
                }

                var redirects = references[particle]
                    .Where(reference => affected[reference])
                    .ToDictionary(static reference => reference, GetOutput, StringComparer.OrdinalIgnoreCase);
                var output = GetOutput(particle);

                // The particle can already be written with the copies of particles written over pointed at
                var existing = plan.ParticleRedirects.FindIndex(redirect => CharacterLoadout.IsSamePath(redirect.Particle, particle)
                    && CharacterLoadout.IsSamePath(redirect.Output, output));

                if (existing >= 0)
                {
                    foreach (var (target, copy) in plan.ParticleRedirects[existing].Redirects)
                    {
                        redirects.TryAdd(target, copy);
                    }

                    plan.ParticleRedirects.RemoveAt(existing);
                }

                plan.ParticleRedirects.Add(new ParticleRedirect(particle, output, redirects));

                if (ParticleFixes.Describe(particle) is { } description)
                {
                    plan.Notes.Add($"{particle} is broken in the game, a fixed copy was written to {output} ({description})");
                }

                foreach (var reference in redirects.Keys)
                {
                    pending.Enqueue(reference);
                }
            }
        }

        private List<string> GetParticleReferences(string particle)
        {
            var references = new List<string>();

            try
            {
                using var resource = fileLoader.LoadFileCompiled(particle);

                foreach (var reference in resource?.ExternalReferences?.ResourceRefInfoList ?? [])
                {
                    if (IsParticlePath(reference.Name) && !references.Contains(NormalizePath(reference.Name), StringComparer.OrdinalIgnoreCase))
                    {
                        references.Add(NormalizePath(reference.Name));
                    }
                }
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read the references of \"{particle}\": {e.Message}");
            }

            return references;
        }

        /// <summary>
        /// Where the game version of a particle that gets written over is kept, for the particles that play it.
        /// </summary>
        private static string GetOriginalCopyPath(string particle) => $"{particle[..^".vpcf".Length]}_original.vpcf";

        /// <summary>
        /// Queues the pedestals the equipped items bring, and when the default assets are replaced writes the first one
        /// over the one the hero stands on without them, with the skin the item's style shows it with.
        /// </summary>
        private void AddPedestals(CharacterLoadout loadout, CharacterExportOptions options, CharacterExportPlan plan)
        {
            var pedestals = loadout.Pedestals.ToList();

            foreach (var (model, _) in pedestals)
            {
                Enqueue(model);
            }

            if (!options.ReplaceDefaults || pedestals.Count == 0)
            {
                return;
            }

            var hero = loadout.Hero;
            var (pedestal, pedestalSkin) = pedestals[0];

            if (hero.Pedestal == null)
            {
                plan.Notes.Add($"{NormalizePath(pedestal)} has no pedestal of the hero's to be written over, it was exported as is");
                return;
            }

            var target = NormalizePath(hero.Pedestal);
            var (skin, bodyGroups) = GetModelLook(loadout, pedestal, pedestalSkin, styleRemaps: !options.RenameModels);

            plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(pedestal), target, skin, [])
            {
                BodyGroups = bodyGroups,
                Rename = options.RenameModels,
            });

            if (hero.SharesPedestal)
            {
                plan.Notes.Add($"{target} is the pedestal every hero without one of its own stands on, they all stand on {NormalizePath(pedestal)} now");
            }

            foreach (var (other, _) in pedestals.Skip(1))
            {
                plan.Notes.Add($"{target} can only be one pedestal, {NormalizePath(other)} was exported as is");
            }
        }

        /// <summary>
        /// Models of forms the hero transforms into, which items can swap like the hero's own.
        /// </summary>
        private void AddTransformationReplacements(CharacterLoadout loadout, EquippedItem item, bool rename, CharacterExportPlan plan)
        {
            foreach (var modifier in item.Modifiers)
            {
                if (modifier.Type != "hero_model_change" || !IsAssetPath(modifier.Asset) || !IsAssetPath(modifier.Modifier))
                {
                    continue;
                }

                var (skin, bodyGroups) = GetModelLook(loadout, modifier.Modifier, item.Skin, styleRemaps: !rename);
                var activityModifiers = loadout.ActivityModifiers;

                if (CharacterLoadout.IsSamePath(modifier.Asset, modifier.Modifier) && skin == 0 && bodyGroups.Count == 0 && activityModifiers.Count == 0)
                {
                    continue;
                }

                plan.ModelReplacements.Add(new ModelReplacement(NormalizePath(modifier.Modifier), NormalizePath(modifier.Asset), skin, [])
                {
                    BodyGroups = bodyGroups,
                    ActivityModifiers = activityModifiers,
                    Rename = rename,
                });

                Enqueue(modifier.Modifier);
            }
        }

        /// <summary>
        /// How the loadout shows a model: the skin, unless the model has no such material group, since a style's skin
        /// also applies to models that come without skins, and the body group choices it sets that change what the
        /// model shows, i.e. of body groups the model has and to other choices than the one it shows by default.
        /// </summary>
        /// <param name="styleRemaps">Whether to work out the <see cref="CharacterExportPlan.StyleMaterialRemaps"/> of the model too.</param>
        private (int Skin, Dictionary<string, int> BodyGroups) GetModelLook(CharacterLoadout loadout, string model, int skin, bool styleRemaps = true)
        {
            var choices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var resource = fileLoader.LoadFileCompiled(NormalizePath(model));

                if (resource?.DataBlock is not Model modelData)
                {
                    return (skin, choices);
                }

                if (skin >= modelData.GetMaterialGroups().Count())
                {
                    skin = 0;
                }

                var bodyGroups = modelData.MeshGroups.BodyGroups;

                foreach (var (name, choice) in loadout.GetBodyGroupChoices(model))
                {
                    if (choice > 0 && bodyGroups.Any(group => group.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && group.Choices.Count > 1))
                    {
                        choices[name] = choice;
                    }
                }

                if (styleRemaps && choices.TryGetValue(CharacterLoadout.ArcanaBodyGroup, out var arcanaChoice)
                    && bodyGroups.FirstOrDefault(static group => group.Name.Equals(CharacterLoadout.ArcanaBodyGroup, StringComparison.OrdinalIgnoreCase)) is { } arcanaGroup)
                {
                    var picked = arcanaGroup.Choices[Math.Min(arcanaChoice, arcanaGroup.Choices.Count - 1)];
                    var remaps = GetStyleMaterialRemaps(modelData, arcanaGroup.Choices[0].FullName, picked.FullName);

                    if (remaps.Count > 0)
                    {
                        styleMaterialRemaps[NormalizePath(model)] = remaps;
                    }
                }
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read \"{model}\": {e.Message}");
            }

            return (skin, choices);
        }

        /// <summary>
        /// Swaps the materials of an arcana body group's first choice and picked style for the style's versions of them,
        /// e.g. drow_arcana_cape for drow_arcana_cape_style1. The first choice is the one shown while nothing switches
        /// the body group, and it is a full mesh of the item made to be shown with the style's materials.
        /// </summary>
        private Dictionary<string, string> GetStyleMaterialRemaps(Model model, string defaultMeshGroup, string pickedMeshGroup)
        {
            var remaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (defaultMeshGroup == pickedMeshGroup)
            {
                return remaps;
            }

            var styleMaterials = GetMeshGroupMaterials(model, pickedMeshGroup);

            // The style's own meshes can use a plain material too, e.g. a level of detail made before its style version
            foreach (var material in GetMeshGroupMaterials(model, defaultMeshGroup).Union(styleMaterials))
            {
                var name = Path.GetFileNameWithoutExtension(material);
                var versions = styleMaterials
                    .Where(style => Path.GetFileNameWithoutExtension(style).StartsWith(name + "_", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Materials the style has no version of stay as they are
                if (versions.Count == 1)
                {
                    remaps[material] = versions[0];
                }
            }

            return remaps;
        }

        private HashSet<string> GetMeshGroupMaterials(Model model, string meshGroup)
        {
            var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] groups = [meshGroup];

            void AddMaterials(Mesh mesh)
            {
                foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects") ?? [])
                {
                    foreach (var drawCall in sceneObject.GetArray("m_drawCalls") ?? [])
                    {
                        if (drawCall.GetStringProperty("m_material") is { Length: > 0 } material)
                        {
                            materials.Add(NormalizePath(material));
                        }
                    }
                }
            }

            foreach (var embedded in model.GetEmbeddedMeshes())
            {
                if (model.MeshGroups.IsMeshInAnyGroup(embedded.MeshIndex, groups))
                {
                    AddMaterials(embedded.Mesh);
                }
            }

            foreach (var reference in model.GetReferenceMeshNamesAndLoD())
            {
                if (!model.MeshGroups.IsMeshInAnyGroup(reference.MeshIndex, groups))
                {
                    continue;
                }

                using var resource = fileLoader.LoadFileCompiled(reference.MeshName);

                if (resource?.DataBlock is Mesh mesh)
                {
                    AddMaterials(mesh);
                }
            }

            return materials;
        }

        /// <summary>
        /// Particles the loadout plays itself: the ones its items create or swap in, and the ones the models it wears
        /// play, e.g. from their animations.
        /// </summary>
        private HashSet<string> GetUsedParticles(CharacterLoadout loadout, HashSet<string> equippedAssets)
        {
            var particles = new HashSet<string>(equippedAssets.Where(IsParticlePath), StringComparer.OrdinalIgnoreCase);

            foreach (var model in equippedAssets.Where(static asset => asset.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
                .Concat(loadout.AdditionalWearables.Select(static wearable => wearable.Model)))
            {
                try
                {
                    using var resource = fileLoader.LoadFileCompiled(NormalizePath(model));

                    foreach (var reference in resource?.ExternalReferences?.ResourceRefInfoList ?? [])
                    {
                        if (IsParticlePath(reference.Name))
                        {
                            particles.Add(NormalizePath(reference.Name));
                        }
                    }
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read the references of \"{model}\": {e.Message}");
                }
            }

            return particles;
        }

        /// <summary>
        /// Whether an effect is exported: as ticked, else when the game shows it with the equipped items, leaving out the
        /// ones staged for the loadout screen, see <see cref="ModelDocEditor.IsStagedForLoadout"/>.
        /// </summary>
        private bool IsEffectEnabled(CharacterLoadout loadout, CharacterExportOptions options, CreatedEffect effect)
        {
            if (options.ItemEffects.TryGetValue(effect.Particle, out var enabled))
            {
                return enabled;
            }

            try
            {
                return loadout.IsShown(effect) && !ModelDocEditor.IsStagedForLoadout(fileLoader, effect.Particle);
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read \"{effect.Particle}\": {e.Message}");
                return loadout.IsShown(effect);
            }
        }

        /// <summary>
        /// A particle an item creates, with the hero model it is made for, see <see cref="AssetModifier.RequiredModelVariant"/>.
        /// </summary>
        private readonly record struct VariantParticle(string Particle, int? Variant);

        /// <summary>
        /// The particles that show on one of the hero's models, by its index in <see cref="HeroDefinition.VariantModels"/>:
        /// the ones made for it and the ones made for every model, e.g. at night Night Stalker's items play their night
        /// effects instead of the day ones.
        /// </summary>
        private static List<string> GetVariantParticles(IEnumerable<VariantParticle> particles, int variant) => [.. particles
            .Where(particle => particle.Variant is null || particle.Variant == variant)
            .Select(static particle => particle.Particle)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        private static bool IsEconParticle(string path) => path.StartsWith("particles/econ/", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether only this hero uses the particle, as opposed to effects every hero plays, like items' or stuns.
        /// </summary>
        public static bool IsHeroParticle(HeroDefinition hero, string path)
            => IsEconParticle(path)
                || (hero.ParticleFolder != null && path.StartsWith(NormalizePath(hero.ParticleFolder).TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                || path.Contains(hero.ShortName, StringComparison.OrdinalIgnoreCase)
                || NamesHeroFolder(hero, path);

        /// <summary>
        /// Whether the particle's name has the hero's name as its particle folder spells it, e.g. Drow Ranger's
        /// "status_effect_drow_frost_arrow" for "hero_drow", which only the hero's abilities play.
        /// </summary>
        private static bool NamesHeroFolder(HeroDefinition hero, string path)
        {
            const string FolderPrefix = "hero_";

            var folder = hero.ParticleFolder == null ? null : Path.GetFileName(NormalizePath(hero.ParticleFolder).TrimEnd('/'));

            if (folder == null || folder.Length <= FolderPrefix.Length || !folder.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return $"_{Path.GetFileNameWithoutExtension(path)}_".Contains($"_{folder[FolderPrefix.Length..]}_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsParticlePath([NotNullWhen(true)] string? value)
            => IsAssetPath(value) && value.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Adds the file a single sound event is defined in, and when <paramref name="includeAudio"/> is set the sounds it
        /// plays. The file is exported for its text, but not the other events' sounds, since it usually holds a lot of them.
        /// </summary>
        private void AddSoundEvent(HeroDefinition hero, string eventName, bool includeAudio, CharacterExportPlan plan)
        {
            soundEvents ??= BuildSoundEventIndex(hero);

            if (!soundEvents.TryGetValue(eventName, out var soundEvent))
            {
                plan.Missing.Add($"sound event {eventName}");
                return;
            }

            Enqueue(soundEvent.File);

            if (!includeAudio)
            {
                return;
            }

            var sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectEventSounds(soundEvent.Data, sounds, depth: 0);

            foreach (var sound in sounds)
            {
                Enqueue(sound);
            }
        }

        /// <summary>
        /// Works out how the sound event files get rewritten for <see cref="CharacterExportOptions.SoundReplacements"/>
        /// and the <see cref="CharacterExportOptions.Voice"/>, and queues those files, and the sounds the copied
        /// definitions play when <see cref="CharacterExportOptions.IncludeAudio"/> is set.
        /// </summary>
        private void AddSoundReplacements(CharacterLoadout loadout, CharacterExportOptions options, CharacterExportPlan plan)
        {
            if (options.SoundReplacements.Count == 0 && options.Voice == null)
            {
                return;
            }

            var hero = loadout.Hero;

            var index = soundEvents ??= BuildSoundEventIndex(hero);
            var edits = new Dictionary<string, SoundEventEdit>(StringComparer.OrdinalIgnoreCase);
            var sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            SoundEventEdit GetEdit(string file)
            {
                if (!edits.TryGetValue(file, out var edit))
                {
                    edit = new SoundEventEdit(file, ReadEventNames(file));
                    edits.Add(edit.File, edit);
                }

                return edit;
            }

            SoundEventEdit? Replace(string target, string source)
            {
                if (!index.TryGetValue(target, out var targetEvent))
                {
                    plan.Missing.Add($"sound event {target}");
                    return null;
                }

                if (!index.TryGetValue(source, out var sourceEvent))
                {
                    plan.Missing.Add($"sound event {source}");
                    return null;
                }

                var edit = GetEdit(targetEvent.File);
                edit.Events[target] = sourceEvent.Data;
                CollectEventSounds(sourceEvent.Data, sounds, depth: 0);
                AddPlayedEvents(edit, sourceEvent, sounds, depth: 0);

                return edit;
            }

            foreach (var (source, target) in options.SoundReplacements)
            {
                Replace(target, source)?.Details.Add($"{target} <- {source}");
            }

            var rules = HeroResponseRules.Load(package, hero);
            IReadOnlyDictionary<string, string> voiceLines = new Dictionary<string, string>();

            if (options.Voice is { Criteria: { } criteria } voice)
            {
                var mapping = rules?.MapVoice(criteria, hero.ChatWheel, voice.ChatWheelSwaps, GetModelSpeechCriteria(loadout));

                if (mapping == null || mapping.Lines.Count == 0)
                {
                    plan.Notes.Add($"the hero's response rules have no lines for the {criteria} voice, it keeps its own");
                }
                else
                {
                    var chatWheelSounds = hero.ChatWheel.Where(static line => !line.Persona).Select(static line => line.Sound).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    SoundEventEdit? voiceEdit = null;
                    var replaced = 0;
                    var chatWheelReplaced = 0;

                    foreach (var (line, voiceLine) in mapping.Lines)
                    {
                        if (Replace(line, voiceLine) is { } edit)
                        {
                            voiceEdit = edit;
                            replaced++;

                            if (chatWheelSounds.Contains(line))
                            {
                                chatWheelReplaced++;
                            }
                        }
                    }

                    voiceEdit?.Details.Add($"{replaced} of the hero's {mapping.HeroLines} voice lines <- the {criteria} voice, " +
                        $"{chatWheelReplaced} of the {chatWheelSounds.Count} chat wheel lines as the voice swaps them");
                    voiceLines = mapping.Lines;
                }
            }

            foreach (var layer in VoiceLayers.All)
            {
                if (layer.Hero != hero.Name || rules == null || !index.TryGetValue(layer.SoundEvent, out var target))
                {
                    continue;
                }

                // A layer picked with no lines is left out
                var lineNames = options.VoiceLayerLines.TryGetValue(layer.SoundEvent, out var picked)
                    ? picked
                    : VoiceLayers.GetDefaultLines(rules, layer, options.Voice?.Criteria, voiceLines);

                var lines = lineNames
                    .Select(line => index.TryGetValue(line, out var lineEvent) ? lineEvent.Data : null)
                    .OfType<KVObject>()
                    .ToList();

                if (lines.Count == 0)
                {
                    continue;
                }

                var edit = GetEdit(target.File);
                var voiceEventName = VoiceLayers.GetVoiceEventName(layer);

                if (VoiceLayers.CreateVoiceEvent(lines) is not { } voiceEvent
                    || VoiceLayers.AddLayer(edit.Events.GetValueOrDefault(layer.SoundEvent) ?? target.Data, voiceEventName) is not { } layered)
                {
                    continue;
                }

                edit.Events[layer.SoundEvent] = layered;
                edit.Events[voiceEventName] = voiceEvent;
                CollectEventSounds(voiceEvent, sounds, depth: 0);
                edit.Details.Add($"{layer.SoundEvent} also speaks one of {lines.Count} voice lines, where the game's rules have the hero say nothing");
            }

            foreach (var edit in edits.Values)
            {
                plan.SoundEventEdits.Add(edit);
                Enqueue(edit.File, includeSounds: options.IncludeAudio);
            }

            if (options.IncludeAudio)
            {
                foreach (var sound in sounds)
                {
                    Enqueue(sound);
                }
            }
        }

        /// <summary>
        /// What the hero model the loadout swaps in asks to be spoken that the hero's own model does not, e.g. the lines
        /// of an arcana's loadout, see <see cref="CharacterSounds.GetSpeechCriteria"/>.
        /// </summary>
        private HashSet<string>? GetModelSpeechCriteria(CharacterLoadout loadout)
        {
            if (loadout.HeroModel == null || loadout.Hero.Model == null || CharacterLoadout.IsSamePath(loadout.HeroModel, loadout.Hero.Model))
            {
                return null;
            }

            try
            {
                var criteria = CharacterSounds.GetSpeechCriteria(fileLoader, loadout.HeroModel);
                criteria.ExceptWith(CharacterSounds.GetSpeechCriteria(fileLoader, loadout.Hero.Model));

                return criteria;
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read what \"{loadout.HeroModel}\" speaks: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Adds the events a copied definition plays, e.g. its "soundevent_layer2", when they are defined next to it but
        /// not in the file it is copied into, which would otherwise not have them.
        /// </summary>
        private void AddPlayedEvents(SoundEventEdit edit, (string File, KVObject Data) soundEvent, HashSet<string> sounds, int depth)
        {
            if (depth > 8)
            {
                return;
            }

            foreach (var name in GetStrings(soundEvent.Data))
            {
                if (edit.FileEvents.Contains(name)
                    || edit.Events.ContainsKey(name)
                    || !soundEvents!.TryGetValue(name, out var played)
                    || !played.File.Equals(soundEvent.File, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                edit.Events[name] = played.Data;
                CollectEventSounds(played.Data, sounds, depth: 0);
                AddPlayedEvents(edit, played, sounds, depth + 1);
            }
        }

        private HashSet<string> ReadEventNames(string file)
        {
            try
            {
                using var resource = fileLoader.LoadFile(file + GameFileLoader.CompiledFileSuffix);

                if (resource?.DataBlock != null)
                {
                    return resource.DataBlock.AsKeyValueCollection().Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read the sound events of \"{file}\": {e.Message}");
            }

            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private void CollectEventSounds(KVObject soundEvent, HashSet<string> sounds, int depth)
        {
            CollectSoundPaths(soundEvent, sounds);

            // An event may inherit its sounds from a base event
            if (depth < 8
                && soundEvent.GetStringProperty("base") is { Length: > 0 } baseName
                && soundEvents!.TryGetValue(baseName, out var baseEvent))
            {
                CollectEventSounds(baseEvent.Data, sounds, depth + 1);
            }
        }

        private static void CollectSoundPaths(KVObject value, HashSet<string> sounds)
            => sounds.UnionWith(GetStrings(value).Where(static text => text.EndsWith(".vsnd", StringComparison.OrdinalIgnoreCase)));

        private static IEnumerable<string> GetStrings(KVObject value)
        {
            switch (value.ValueType)
            {
                case KVValueType.String when value.ToString() is { } text:
                    yield return text;
                    break;

                case KVValueType.Collection:
                case KVValueType.Array:
                    foreach (var child in value.Values)
                    {
                        foreach (var text in GetStrings(child))
                        {
                            yield return text;
                        }
                    }

                    break;
            }
        }

        private Dictionary<string, (string File, KVObject Data)> BuildSoundEventIndex(HeroDefinition hero)
        {
            progress?.Report("Indexing sound events...");

            var index = new Dictionary<string, (string File, KVObject Data)>(StringComparer.OrdinalIgnoreCase);
            var heroVoiceFile = hero.VoiceFile != null ? NormalizePath(hero.VoiceFile) + GameFileLoader.CompiledFileSuffix : null;

            // Hero files go first so their definitions win over same named events elsewhere
            var entries = GetPackageEntries(CharacterSounds.SoundEventsTypeName)
                .Where(entry =>
                {
                    var path = entry.GetFullPath();

                    if (path.StartsWith(CharacterSounds.VoiceScriptsFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        return path.Equals(heroVoiceFile, StringComparison.OrdinalIgnoreCase);
                    }

                    return !CharacterSounds.IgnoredFolders.Any(folder => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
                })
                .OrderByDescending(entry => entry.GetFullPath().Contains(hero.ShortName, StringComparison.OrdinalIgnoreCase));

            foreach (var entry in entries)
            {
                var path = entry.GetFullPath();

                try
                {
                    using var resource = new Resource { FileName = path };
                    resource.Read(GameFileLoader.GetPackageEntryStream(package, entry));

                    if (resource.DataBlock == null)
                    {
                        continue;
                    }

                    var sourcePath = path[..^GameFileLoader.CompiledFileSuffix.Length];

                    foreach (var (eventName, eventData) in resource.DataBlock.AsKeyValueCollection())
                    {
                        if (eventData.ValueType == KVValueType.Collection)
                        {
                            index.TryAdd(eventName, (sourcePath, eventData));
                        }
                    }
                }
                catch (Exception e)
                {
                    progress?.Report($"  ! failed to read sound events from \"{path}\": {e.Message}");
                }
            }

            return index;
        }

        private void Visit(string path, bool includeSounds, CharacterExportPlan plan)
        {
            var sourcePath = path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
                ? path[..^GameFileLoader.CompiledFileSuffix.Length]
                : path;
            var compiledPath = sourcePath + GameFileLoader.CompiledFileSuffix;

            if (!Exists(compiledPath))
            {
                if (Exists(sourcePath))
                {
                    plan.RawFiles.Add(sourcePath);
                }
                else
                {
                    plan.Missing.Add(sourcePath);
                }

                return;
            }

            if (sourcePath.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
            {
                plan.Models.Add(compiledPath);
            }
            else
            {
                plan.Resources.Add(compiledPath);
            }

            if (includeSounds && sourcePath.EndsWith(".vsndevts", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sound in ReadSounds(compiledPath))
                {
                    Enqueue(sound);
                }
            }
        }

        /// <summary>
        /// The sounds a sound event file plays, which it does not always list as references.
        /// </summary>
        private HashSet<string> ReadSounds(string compiledPath)
        {
            var sounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var resource = fileLoader.LoadFile(compiledPath);

                if (resource == null)
                {
                    return sounds;
                }

                if (resource.ExternalReferences is { } externalReferences)
                {
                    foreach (var reference in externalReferences.ResourceRefInfoList)
                    {
                        if (reference.Name?.EndsWith(".vsnd", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            sounds.Add(reference.Name);
                        }
                    }
                }

                if (resource.DataBlock != null)
                {
                    CollectSoundPaths(resource.DataBlock.AsKeyValueCollection(), sounds);
                }
            }
            catch (Exception e)
            {
                progress?.Report($"  ! failed to read the sounds of \"{compiledPath}\": {e.Message}");
            }

            return sounds;
        }

        private void Enqueue(string path, bool includeSounds = false)
        {
            path = NormalizePath(path);

            if (path.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[..^GameFileLoader.CompiledFileSuffix.Length];
            }

            if (visited.Add(path))
            {
                queue.Enqueue((path, includeSounds));
            }
        }

        private bool Exists(string path)
        {
            var (pathOnDisk, _, packageEntry) = fileLoader.FindFile(path, logNotFound: false);
            return pathOnDisk != null || packageEntry != null;
        }

        private List<PackageEntry> GetPackageEntries(string typeName)
            => package.Entries != null && package.Entries.TryGetValue(typeName, out var entries) ? entries : [];

        private static bool IsAssetPath([NotNullWhen(true)] string? value)
            => value != null && value.Contains('/', StringComparison.Ordinal) && AssetExtensions.Contains(Path.GetExtension(value));

        private static string NormalizePath(string path) => CharacterLoadout.NormalizePath(path);
    }
}
