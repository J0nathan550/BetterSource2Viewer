using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;
using WinForms = System.Windows.Forms;

namespace GUI.Forms
{
    /// <summary>
    /// Asks which Deadlock hero to export, what of it, and into which content folder. Same API as the WinForms one so
    /// the exporter is shared.
    /// </summary>
    sealed class DeadlockHeroSelectForm : IDisposable
    {
        private readonly IReadOnlyList<DeadlockHero> heroes;
        private readonly Window window;
        private readonly TextBox searchTextBox;
        private readonly ListBox heroListBox;
        private readonly TextBlock detailsText;
        private readonly CheckBox modelCheckBox;
        private readonly CheckBox abilityModelsCheckBox;
        private readonly CheckBox materialsCheckBox;
        private readonly CheckBox particlesCheckBox;
        private readonly CheckBox soundsCheckBox;
        private readonly CheckBox voiceCheckBox;
        private readonly CheckBox audioCheckBox;
        private readonly TextBox folderTextBox;
        private WinForms.DialogResult result = WinForms.DialogResult.Cancel;

        public DeadlockHero? SelectedHero => heroListBox.SelectedItem as DeadlockHero;

        public DeadlockHeroExportOptions Options => new()
        {
            Model = modelCheckBox.IsChecked == true,
            AbilityModels = abilityModelsCheckBox.IsChecked == true,
            Materials = materialsCheckBox.IsChecked == true,
            Particles = particlesCheckBox.IsChecked == true,
            Sounds = soundsCheckBox.IsChecked == true,
            Voice = voiceCheckBox.IsChecked == true,
            IncludeAudio = audioCheckBox.IsChecked == true,
        };

        /// <summary>The content folder to export into, set once the dialog is confirmed.</summary>
        public string? ContentFolder { get; private set; }

        public DeadlockHeroSelectForm(IReadOnlyList<DeadlockHero> heroes)
        {
            this.heroes = heroes;

            searchTextBox = new TextBox { PlaceholderText = "Search heroes" };
            searchTextBox.TextChanged += (_, _) => FilterHeroes();

            heroListBox = new ListBox();
            heroListBox.SelectionChanged += (_, _) => UpdateDetails();
            heroListBox.DoubleTapped += async (_, _) => await SubmitAsync().ConfigureAwait(true);

            detailsText = new TextBlock { Opacity = 0.75, Margin = new(0, 6, 0, 0) };

            modelCheckBox = new CheckBox { Content = "Model, with its meshes, animations and physics", IsChecked = true };
            abilityModelsCheckBox = new CheckBox { Content = "Models of its abilities, such as summons and transformations", IsChecked = true };
            materialsCheckBox = new CheckBox { Content = "Materials and textures of the models", IsChecked = true };
            particlesCheckBox = new CheckBox { Content = "Particles of the hero, its weapon and abilities", IsChecked = true };
            soundsCheckBox = new CheckBox { Content = "Sound events of its weapon and abilities", IsChecked = true };
            voiceCheckBox = new CheckBox { Content = "Voice line sound events" };
            audioCheckBox = new CheckBox { Content = "Audio files the exported sound events play" };

            folderTextBox = new TextBox { Text = Settings.Config.DeadlockHeroExportContentDir };

            var browseButton = new Button { Content = "Browse...", Width = 88, Margin = new(6, 0, 0, 0) };
            browseButton.Click += async (_, _) =>
            {
                if (await AppFileDialogs.PickFolderAsync("Choose the content folder to export the hero into", AppFileDialogs.RememberIn.SaveDirectory).ConfigureAwait(true) is { } folder)
                {
                    folderTextBox.Text = folder;
                }
            };

            var folderRow = new DockPanel();
            DockPanel.SetDock(browseButton, Dock.Right);
            folderRow.Children.Add(browseButton);
            folderRow.Children.Add(folderTextBox);

            var exportButton = new Button { Content = "Export", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };

            var bottom = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    detailsText,
                    modelCheckBox,
                    abilityModelsCheckBox,
                    materialsCheckBox,
                    particlesCheckBox,
                    soundsCheckBox,
                    voiceCheckBox,
                    audioCheckBox,
                    new TextBlock { Text = "Content folder:", Margin = new(0, 6, 0, 0) },
                    folderRow,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new(0, 8, 0, 0),
                        Children = { exportButton, cancelButton },
                    },
                },
            };

            var root = new DockPanel { Margin = new(12) };
            DockPanel.SetDock(searchTextBox, Dock.Top);
            DockPanel.SetDock(bottom, Dock.Bottom);
            searchTextBox.Margin = new(0, 0, 0, 6);
            root.Children.Add(searchTextBox);
            root.Children.Add(bottom);
            root.Children.Add(heroListBox);

            window = new Window
            {
                Title = "Export Deadlock hero",
                Width = 560,
                Height = 600,
                MinWidth = 440,
                MinHeight = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Content = root,
            };

            exportButton.Click += async (_, _) => await SubmitAsync().ConfigureAwait(true);
            cancelButton.Click += (_, _) => window.Close();

            FilterHeroes();
        }

        private void FilterHeroes()
        {
            var selected = SelectedHero;
            var search = searchTextBox.Text?.Trim() ?? string.Empty;
            var shown = heroes.Where(hero => search.Length == 0
                || hero.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || hero.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

            heroListBox.ItemsSource = shown;
            heroListBox.SelectedItem = selected != null && shown.Contains(selected) ? selected : shown.FirstOrDefault();

            UpdateDetails();
        }

        private void UpdateDetails()
            => detailsText.Text = SelectedHero is { } hero
                ? $"{hero.Model}\n{hero.AbilityModels.Count} ability models, {hero.Particles.Count} particles, {hero.SoundEventFiles.Count + hero.VoiceEventFiles.Count} sound event files"
                : string.Empty;

        private async Task SubmitAsync()
        {
            if (SelectedHero == null)
            {
                return;
            }

            var folder = folderTextBox.Text?.Trim() ?? string.Empty;

            if (!Path.IsPathFullyQualified(folder))
            {
                await AppMessageDialogs.ShowMessageAsync("Choose the content folder to export the hero into.", "No content folder", MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            ContentFolder = Path.GetFullPath(folder);
            Settings.Config.DeadlockHeroExportContentDir = ContentFolder;
            Settings.Save();

            result = WinForms.DialogResult.OK;
            window.Close();
        }

        public async Task<WinForms.DialogResult> ShowDialogAsync()
        {
            if (AppMessageDialogs.GetOwner() is not { } owner)
            {
                return WinForms.DialogResult.Cancel;
            }

            await window.ShowDialog(owner).ConfigureAwait(true);
            return result;
        }

        public void Dispose()
        {
        }
    }
}
