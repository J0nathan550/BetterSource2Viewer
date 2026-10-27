using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter.CharacterAssets;
using GUI.Utils;

namespace GUI.Forms
{
    /// <summary>
    /// Asks which Deadlock hero to export, what of it, and into which content folder.
    /// </summary>
    sealed class DeadlockHeroSelectForm : ThemedForm
    {
        private readonly IReadOnlyList<DeadlockHero> heroes;
        private readonly TextBox searchTextBox;
        private readonly ListBox heroListBox;
        private readonly Label detailsLabel;
        private readonly CheckBox modelCheckBox;
        private readonly CheckBox abilityModelsCheckBox;
        private readonly CheckBox materialsCheckBox;
        private readonly CheckBox particlesCheckBox;
        private readonly CheckBox soundsCheckBox;
        private readonly CheckBox voiceCheckBox;
        private readonly CheckBox audioCheckBox;
        private readonly TextBox folderTextBox;

        public DeadlockHero? SelectedHero => heroListBox.SelectedItem as DeadlockHero;

        public DeadlockHeroExportOptions Options => new()
        {
            Model = modelCheckBox.Checked,
            AbilityModels = abilityModelsCheckBox.Checked,
            Materials = materialsCheckBox.Checked,
            Particles = particlesCheckBox.Checked,
            Sounds = soundsCheckBox.Checked,
            Voice = voiceCheckBox.Checked,
            IncludeAudio = audioCheckBox.Checked,
        };

        /// <summary>The content folder to export into, set once the dialog is confirmed.</summary>
        public string? ContentFolder { get; private set; }

        public DeadlockHeroSelectForm(IReadOnlyList<DeadlockHero> heroes)
        {
            this.heroes = heroes;

            Text = "Export Deadlock hero";
            Icon = Program.MainForm.Icon;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 520);
            MinimumSize = new Size(480, 420);

            searchTextBox = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Search heroes" };
            searchTextBox.TextChanged += (_, _) => FilterHeroes();

            heroListBox = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            heroListBox.SelectedIndexChanged += (_, _) => UpdateDetails();
            heroListBox.DoubleClick += (_, _) => Submit();

            detailsLabel = new Label { Dock = DockStyle.Fill, AutoSize = true };

            modelCheckBox = CreateCheckBox("Model, with its meshes, animations and physics", true);
            abilityModelsCheckBox = CreateCheckBox("Models of its abilities, such as summons and transformations", true);
            materialsCheckBox = CreateCheckBox("Materials and textures of the models", true);
            particlesCheckBox = CreateCheckBox("Particles of the hero, its weapon and abilities", true);
            soundsCheckBox = CreateCheckBox("Sound events of its weapon and abilities", true);
            voiceCheckBox = CreateCheckBox("Voice line sound events", false);
            audioCheckBox = CreateCheckBox("Audio files the exported sound events play", false);

            folderTextBox = new TextBox { Dock = DockStyle.Fill, Text = Settings.Config.DeadlockHeroExportContentDir };

            var browseButton = new ThemedButton { Text = "Browse...", AutoSize = true, Anchor = AnchorStyles.Right };
            browseButton.Click += (_, _) =>
            {
                if (AppFileDialogs.PickFolder("Choose the content folder to export the hero into", AppFileDialogs.RememberIn.SaveDirectory) is { } folder)
                {
                    folderTextBox.Text = folder;
                }
            };

            var exportButton = new ThemedButton { Text = "Export", AutoSize = true, MinimumSize = new Size(90, 0) };
            exportButton.Click += (_, _) => Submit();

            var cancelButton = new ThemedButton { Text = "Cancel", AutoSize = true, MinimumSize = new Size(90, 0), DialogResult = DialogResult.Cancel };

            AcceptButton = exportButton;
            CancelButton = cancelButton;

            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
            options.Controls.AddRange([modelCheckBox, abilityModelsCheckBox, materialsCheckBox, particlesCheckBox, soundsCheckBox, voiceCheckBox, audioCheckBox]);

            var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            folderRow.Controls.Add(folderTextBox, 0, 0);
            folderRow.Controls.Add(browseButton, 1, 0);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            buttons.Controls.AddRange([cancelButton, exportButton]);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(searchTextBox, 0, 0);
            layout.Controls.Add(heroListBox, 0, 1);
            layout.Controls.Add(detailsLabel, 0, 2);
            layout.Controls.Add(options, 0, 3);
            layout.Controls.Add(new Label { Text = "Content folder:", AutoSize = true, Margin = new Padding(3, 8, 3, 0) }, 0, 4);
            layout.Controls.Add(folderRow, 0, 5);
            layout.Controls.Add(buttons, 0, 6);

            Controls.Add(layout);

            FilterHeroes();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                searchTextBox.Dispose();
                heroListBox.Dispose();
                detailsLabel.Dispose();
                modelCheckBox.Dispose();
                abilityModelsCheckBox.Dispose();
                materialsCheckBox.Dispose();
                particlesCheckBox.Dispose();
                soundsCheckBox.Dispose();
                voiceCheckBox.Dispose();
                audioCheckBox.Dispose();
                folderTextBox.Dispose();
            }

            base.Dispose(disposing);
        }

        private static CheckBox CreateCheckBox(string text, bool isChecked) => new() { Text = text, Checked = isChecked, AutoSize = true };

        private void FilterHeroes()
        {
            var selected = SelectedHero;
            var search = searchTextBox.Text.Trim();

            heroListBox.BeginUpdate();
            heroListBox.Items.Clear();
            heroListBox.Items.AddRange([.. heroes.Where(hero => search.Length == 0
                || hero.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                || hero.Name.Contains(search, StringComparison.OrdinalIgnoreCase))]);
            heroListBox.EndUpdate();

            heroListBox.SelectedItem = selected != null && heroListBox.Items.Contains(selected) ? selected : null;

            if (heroListBox.SelectedIndex < 0 && heroListBox.Items.Count > 0)
            {
                heroListBox.SelectedIndex = 0;
            }

            UpdateDetails();
        }

        private void UpdateDetails()
            => detailsLabel.Text = SelectedHero is { } hero
                ? $"{hero.Model}\n{hero.AbilityModels.Count} ability models, {hero.Particles.Count} particles, {hero.SoundEventFiles.Count + hero.VoiceEventFiles.Count} sound event files"
                : string.Empty;

        private async void Submit()
        {
            if (SelectedHero == null)
            {
                return;
            }

            var folder = folderTextBox.Text.Trim();

            if (!Path.IsPathFullyQualified(folder))
            {
                await AppMessageDialogs.ShowMessageAsync("Choose the content folder to export the hero into.", "No content folder", MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            ContentFolder = Path.GetFullPath(folder);
            Settings.Config.DeadlockHeroExportContentDir = ContentFolder;
            Settings.Save();

            DialogResult = DialogResult.OK;
        }
    }
}
