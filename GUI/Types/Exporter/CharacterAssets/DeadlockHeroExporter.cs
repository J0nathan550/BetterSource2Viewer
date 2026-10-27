using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GUI.Forms;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using WinForms = System.Windows.Forms;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// What to export of a Deadlock hero.
    /// </summary>
    sealed class DeadlockHeroExportOptions
    {
        /// <summary>The hero's model, with its meshes, animations and physics.</summary>
        public bool Model { get; set; } = true;

        /// <summary>The models the hero's abilities use, see <see cref="DeadlockHero.AbilityModels"/>.</summary>
        public bool AbilityModels { get; set; } = true;

        /// <summary>The materials the exported models use, with their textures. Otherwise they use the game's.</summary>
        public bool Materials { get; set; } = true;

        /// <summary>The particles the hero, its weapon and its abilities create, see <see cref="DeadlockHero.Particles"/>.</summary>
        public bool Particles { get; set; } = true;

        /// <summary>The hero's sound event files, see <see cref="DeadlockHero.SoundEventFiles"/>.</summary>
        public bool Sounds { get; set; } = true;

        /// <summary>The hero's voice line sound event files, see <see cref="DeadlockHero.VoiceEventFiles"/>.</summary>
        public bool Voice { get; set; }

        /// <summary>
        /// The sounds the exported sound event files play. Without them, the files keep pointing at the game's sounds.
        /// </summary>
        public bool IncludeAudio { get; set; }
    }

    /// <summary>
    /// Exports a Deadlock hero as the game ships it, without cosmetics, into a content folder laid out like the package:
    /// its model through the custom model extractor so it recompiles as is, everything else through the built-in decompiler.
    /// </summary>
    static class DeadlockHeroExporter
    {
        /// <summary>
        /// Whether the package has the hero and ability scripts an export is read from.
        /// </summary>
        public static bool CanExport(VrfGuiContext? context)
            => context?.CurrentPackage is { } package && DeadlockHeroCatalog.IsAvailable(package);

        /// <summary>
        /// Asks for a hero from the context's package, then exports it.
        /// </summary>
        public static async Task ExportAsync(VrfGuiContext context)
        {
            var vpkPath = context.FileName;

            if (context.CurrentPackage == null || string.IsNullOrEmpty(vpkPath) || !File.Exists(vpkPath))
            {
                await AppMessageDialogs.ShowMessageAsync(
                    "Heroes can only be exported from a package opened from disk.",
                    "Cannot export hero",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            using var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);

            try
            {
                package.Read(vpkPath);
            }
            catch (Exception ex)
            {
                Log.Error(nameof(DeadlockHeroExporter), $"Failed to open VPK: {ex.Message}");
                return;
            }

            var heroes = await LoadHeroesAsync(package, vpkPath).ConfigureAwait(true);

            if (heroes == null)
            {
                return;
            }

            if (heroes.Count == 0)
            {
                await AppMessageDialogs.ShowMessageAsync(
                    $"No heroes with a model were found in {DeadlockHeroCatalog.HeroesPath}.",
                    "Cannot export hero",
                    MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            DeadlockHero hero;
            DeadlockHeroExportOptions options;
            string contentRoot;

            using (var form = new DeadlockHeroSelectForm(heroes))
            {
                if (await form.ShowDialogAsync().ConfigureAwait(true) != WinForms.DialogResult.OK || form.SelectedHero == null || form.ContentFolder == null)
                {
                    return;
                }

                hero = form.SelectedHero;
                options = form.Options;
                contentRoot = form.ContentFolder;
            }

            using var dialog = new CustomVmdlExtractProgressForm
            {
                StayOpenOnCompletion = true,
                Text = $"Exporting {hero.DisplayName}...",
            };

            dialog.AppendLine($"Exporting {hero.DisplayName} to \"{contentRoot}\"");

            dialog.OnProcess = cancellationToken =>
            {
                Export(vpkPath, package, hero, options, contentRoot, dialog, cancellationToken);
                return Task.CompletedTask;
            };

            await dialog.ShowDialogAsync().ConfigureAwait(true);
        }

        private static async Task<List<DeadlockHero>?> LoadHeroesAsync(Package package, string vpkPath)
        {
            List<DeadlockHero>? heroes = null;

            using var dialog = new GenericProgressForm { Text = "Reading heroes..." };

            dialog.OnProcess = cancellationToken =>
            {
                try
                {
                    heroes = DeadlockHeroCatalog.Load(package, vpkPath, dialog, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    heroes = null;
                }

                return Task.CompletedTask;
            };

            await dialog.ShowDialogAsync().ConfigureAwait(true);

            return heroes;
        }

        internal static void Export(string vpkPath, Package package, DeadlockHero hero, DeadlockHeroExportOptions options,
            string contentRoot, IProgress<string> progress, CancellationToken cancellationToken)
        {
            var startTimestamp = Stopwatch.GetTimestamp();

            Log.Info(nameof(DeadlockHeroExporter), $"Hero export of {hero.Name} started to \"{contentRoot}\"");

            var roots = new List<(string Path, bool IncludeSounds)>();

            if (options.Model)
            {
                roots.Add((hero.Model, false));
            }

            if (options.AbilityModels)
            {
                roots.AddRange(hero.AbilityModels.Select(static model => (model, false)));
            }

            if (options.Particles)
            {
                roots.AddRange(hero.Particles.Select(static particle => (particle, false)));
            }

            if (options.Sounds)
            {
                roots.AddRange(hero.SoundEventFiles.Select(file => (file, options.IncludeAudio)));
            }

            if (options.Voice)
            {
                roots.AddRange(hero.VoiceEventFiles.Select(file => (file, options.IncludeAudio)));
            }

            // The model extractor reports what it does on the console
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdOutWriter = new CustomVmdlExporter.LogTextWriter(isError: false, progress);
            using var stdErrWriter = new CustomVmdlExporter.LogTextWriter(isError: true, progress);
            Console.SetOut(stdOutWriter);
            Console.SetError(stdErrWriter);

            try
            {
                using var fileLoader = new GameFileLoader(package, vpkPath);

                progress.Report("Collecting dependencies...");

                var plan = new CharacterDependencyCollector(package, fileLoader, progress).CollectAssets(roots, options.Materials, cancellationToken);

                progress.Report($"Found {plan.Models.Count} models, {plan.Materials.Count} materials, {plan.Resources.Count} particles, sound events and sounds and {plan.RawFiles.Count} other files");

                foreach (var missing in plan.Missing)
                {
                    progress.Report($"  ! not found: {missing}");
                }

                var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var failed = 0;

                failed += CharacterAssetsExporter.ExportModels(plan.Models, vpkPath, contentRoot, fileLoader, progress, cancellationToken);
                failed += ExportMaterials(plan.Materials, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);
                failed += CharacterAssetsExporter.ExportResources(plan.Resources, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);
                failed += CharacterAssetsExporter.ExportRawFiles(plan.RawFiles, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);

                var completedText = $"Export completed in {GenericProgressForm.FormatTime(Stopwatch.GetElapsedTime(startTimestamp))}";

                if (failed > 0)
                {
                    completedText += $", {failed} file{(failed == 1 ? "" : "s")} failed";
                }

                if (plan.Missing.Count > 0)
                {
                    completedText += $", {plan.Missing.Count} reference{(plan.Missing.Count == 1 ? " was" : "s were")} not found";
                }

                Log.Info(nameof(DeadlockHeroExporter), completedText);
                progress.Report(completedText);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        private static int ExportMaterials(List<string> materials, string contentRoot, GameFileLoader fileLoader,
            IProgress<string> progress, HashSet<string> writtenFiles, CancellationToken cancellationToken)
        {
            if (materials.Count == 0)
            {
                return 0;
            }

            progress.Report($"Exporting {materials.Count} materials with the custom VMAT exporter...");

            var failed = 0;

            foreach (var material in materials)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var resource = fileLoader.LoadFile(material) ?? throw new FileNotFoundException("Could not be read");

                    var vmatPath = CharacterAssetsExporter.GetOutputPath(contentRoot,
                        Path.ChangeExtension(CharacterAssetsExporter.StripCompiledSuffix(material), "vmat"));

                    progress.Report($"  {material}");
                    CustomVmatExporter.ExportMaterial(resource, vmatPath, contentRoot, fileLoader, progress, writtenFiles, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failed++;
                    progress.Report($"  FAILED {material}: {e.Message}");
                    Log.Error(nameof(DeadlockHeroExporter), $"Failed to export '{material}': {e}");
                }
            }

            return failed;
        }
    }
}
