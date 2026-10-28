using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        public static async Task ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var currentPackage = vrfGuiContext.CurrentPackage;
            if (currentPackage == null)
            {
                ErrorReport.ShowError(new InvalidOperationException("The package is not available, cannot extract file"), file.GetFullPath());
                return;
            }

            var fileName = file.GetFullPath();

            var errorContext = vrfGuiContext.GetEntryDisplayPath(fileName);

            Stream stream;

            try
            {
                stream = currentPackage.GetMemoryMappedStreamIfPossible(file);
            }
            catch (Exception e)
            {
                ErrorReport.ShowError(e, errorContext);
                return;
            }

            await ExtractFileFromStream(fileName, stream, vrfGuiContext, decompile, errorContext).ConfigureAwait(true);
        }

        /// <param name="errorContext">Which file this is, shown when extracting it fails.</param>
        public static async Task ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile, string errorContext)
        {
            try
            {
                if (decompile && !await PreExportDisclaimer(Path.GetExtension(fileName)).ConfigureAwait(true))
                {
                    return;
                }

                if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    await DecompileResource(fileName, stream, vrfGuiContext).ConfigureAwait(true);
                }
                else
                {
                    await SaveFile(fileName, stream, decompile).ConfigureAwait(true);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                ErrorReport.ShowError(e, errorContext);
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static async Task DecompileResource(string fileName, Stream stream, VrfGuiContext vrfGuiContext)
        {
            var resourceTemp = new Resource
            {
                FileName = fileName,
            };
            var resource = resourceTemp;
            string filaNameToSave;

            try
            {
                resource.Read(stream);

                var extension = FileExtract.GetExtension(resource);

                if (extension == null)
                {
                    await AppMessageDialogs.ShowMessageAsync($"Exporting \"{fileName}\" is not supported.", "Failed to export", MessageIcon.Warning).ConfigureAwait(true);
                    return;
                }

                var filter = $"{extension} file|*.{extension}";

                if (GltfModelExporter.CanExport(resource))
                {
                    const string gltfFilter = "glTF|*.gltf";
                    const string glbFilter = "glTF Binary|*.glb";

                    filter = $"{filter}|{gltfFilter}|{glbFilter}";
                }

                var fileNameForSave = Path.GetFileNameWithoutExtension(fileName);

                if (Path.GetExtension(fileName) == ".vmap_c")
                {
                    // When exporting a vmap, suggest saving with a suffix like de_dust2_d,
                    // to reduce conflicts when users end up recompiling the map with the same name as it exists in the game
                    fileNameForSave += "_d";
                }

                var (pickedFileName, _) = await AppFileDialogs.SaveFileAsync("Choose where to save the file", fileNameForSave, extension, filter).ConfigureAwait(true);

                if (pickedFileName == null)
                {
                    return;
                }

                filaNameToSave = pickedFileName;
                resourceTemp = null;
            }
            finally
            {
                resourceTemp?.Dispose();
            }

            var directory = Path.GetDirectoryName(filaNameToSave);
            // A tab of the file itself already has a context for it
            var ownsContext = vrfGuiContext.IsPackage;
            var exportData = new ExportData
            {
                VrfGuiContext = ownsContext ? new VrfGuiContext(fileName, vrfGuiContext) : vrfGuiContext,
                BypassResourceCache = !ownsContext,
            };

            try
            {
                var exporter = new PackageExporter(exportData, directory ?? string.Empty, true);

                // Cancelling closes the dialog while the extract is still winding down, so wait for it to
                // stop before the finally pulls the resource out from under it
                await exporter.ExecuteSingleFileExtractAsync(resource, fileName, filaNameToSave, $"Extracting {fileName} to \"{Path.GetFileName(filaNameToSave)}\"").ConfigureAwait(true);
            }
            finally
            {
                resource.Dispose();

                if (ownsContext)
                {
                    exportData.VrfGuiContext.Dispose();
                }
            }
        }

        private static async Task SaveFile(string fileName, Stream stream, bool decompile)
        {
            ContentFile? content = null;

            try
            {
                if (decompile && FileExtract.TryExtractNonResource(stream, fileName, out content))
                {
                    if (content.Data == null)
                    {
                        // Content has no data to extract, only potentially subfiles
                        Log.Info(nameof(ExportFile), $"File \"{fileName}\" has no extractable data");
                        return;
                    }

                    fileName = Path.ChangeExtension(fileName, Path.GetExtension(content.FileName));
                    stream = new MemoryStream(content.Data);
                }

                var (saveFileName, _) = await AppFileDialogs.SaveFileAsync("Choose where to save the file", Path.GetFileName(fileName), null, "All files (*.*)|*.*").ConfigureAwait(true);

                if (saveFileName == null)
                {
                    return;
                }

                using var streamOutput = File.Create(saveFileName);
                await stream.CopyToAsync(streamOutput).ConfigureAwait(true);

                Log.Info(nameof(ExportFile), $"Saved \"{Path.GetFileName(saveFileName)}\"");
            }
            finally
            {
                content?.Dispose();
            }
        }

        /// <summary>
        /// Extracts the files and folders selected in the package viewer, asking whether to recurse into subfolders
        /// and whether to recreate the package folder structure when those choices matter.
        /// </summary>
        public static async Task ExtractSelectedItems(List<IBetterBaseItem> items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (items.Count == 0)
            {
                return;
            }

            var includeSubfolders = true;

            if (items.Any(static item => item.PkgNode is { Folders.Count: > 0 }))
            {
                var answer = await AppMessageDialogs.AskYesNoCancelAsync(
                    """
                    The selection contains folders with subfolders.

                    Yes: export everything, including all subfolders and their files.
                    No: export only the files directly inside the selected folders.
                    """,
                    "Export subfolders").ConfigureAwait(true);

                if (answer == null)
                {
                    return;
                }

                includeSubfolders = answer.Value;
            }

            // Only files and folders below a top level folder have parent folders to recreate
            var examplePath = items
                .Select(static item => item.PackageEntry?.GetFullPath() ?? $"{item.PkgNode?.GetFullPath()}{Package.DirectorySeparatorChar}")
                .FirstOrDefault(static path => path.TrimEnd(Package.DirectorySeparatorChar).Contains(Package.DirectorySeparatorChar, StringComparison.Ordinal));

            var keepPackageStructure = false;

            if (examplePath != null)
            {
                var answer = await AppMessageDialogs.AskYesNoCancelAsync(
                    $"""
                    Export with the folder structure of the package?

                    Yes: recreate the parent folders, e.g. "{examplePath}".
                    No: place the selected files and folders directly in the chosen location.
                    """,
                    "Export folder structure").ConfigureAwait(true);

                if (answer == null)
                {
                    return;
                }

                keepPackageStructure = answer.Value;
            }

            // A single file placed as is gets the regular save dialog, so it can be renamed or exported as another format
            if (!keepPackageStructure && items is [{ PackageEntry: { } singleFile }])
            {
                await ExtractFileFromPackageEntry(singleFile, vrfGuiContext, decompile).ConfigureAwait(true);
                return;
            }

            var exportData = new ExportData
            {
                VrfGuiContext = vrfGuiContext,
            };

            var exporter = new PackageExporter(exportData, null, decompile);

            foreach (var item in items)
            {
                exporter.QueueSelection(item, includeSubfolders, keepPackageStructure);
            }

            exporter.ExecuteMultipleFileExtract();
        }

        public static async Task<bool> PreExportDisclaimer(string fileExtension)
        {
            var messageString = "";

            switch (fileExtension)
            {
                case ".vmap_c":

                    messageString =
                    """
                    Compiling a map throws away much of how it was built, so the decompiled map is only an approximation:
                    props the compiler merged come back as one prop per material, meshes come back as triangles and quads,
                    and prefabs and groups are gone.

                    It is NOT ADVISED to work on decompiled maps as your first map if you are new to mapping!
                    """;
                    break;

                default:
                    break;
            }

            if (!string.IsNullOrEmpty(messageString))
            {
                return await AppMessageDialogs.ConfirmAsync(messageString, "Decompile warning", MessageIcon.Warning).ConfigureAwait(true);
            }

            return true;
        }
    }
}
