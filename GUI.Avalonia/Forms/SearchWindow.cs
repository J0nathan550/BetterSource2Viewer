using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Types.PackageViewer;
using ValveResourceFormat.IO;

namespace GUI.Forms
{
    /// <summary>
    /// The package Find dialog, like the WinForms SearchForm. Kept per main window so the last search is remembered.
    /// </summary>
    sealed class SearchWindow : Window
    {
        internal static readonly StringComparer NumericComparer =
            StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

        // Indexed by PackageSearchType
        internal static readonly string[] SearchTypeNames = ["File name", "Full path", "File contents", "File contents hex bytes"];

        private readonly TextBox findTextBox;
        private readonly ComboBox searchTypeComboBox;
        private readonly CheckBox matchCaseCheckBox;
        private readonly CheckBox wholeWordCheckBox;
        private readonly CheckBox regexCheckBox;
        private readonly ComboBox fileTypeComboBox;
        private readonly TextBlock hintText;
        private readonly Button findButton;
        private readonly ComboBox filterKeyComboBox;
        private readonly ComboBox filterValueComboBox;
        private Dictionary<string, SortedSet<string>>? filterKeys;
        private string[] fileTypes = [];
        private int filterLoadVersion;

        /// <summary>
        /// Gets the search as entered by the user.
        /// </summary>
        public SearchRequest Request => new(
            new(findTextBox.Text ?? string.Empty, (PackageSearchType)Math.Max(0, searchTypeComboBox.SelectedIndex), matchCaseCheckBox.IsChecked == true, wholeWordCheckBox.IsChecked == true, regexCheckBox.IsChecked == true),
            null,
            SelectedFileType,
            SelectedFilterKey,
            SelectedFilterKey != null && filterValueComboBox.SelectedIndex > 0 ? filterValueComboBox.SelectedItem as string : null);

        private string? SelectedFileType => fileTypeComboBox.SelectedIndex > 0 ? fileTypes[fileTypeComboBox.SelectedIndex - 1] : null;

        private string? SelectedFilterKey => filterKeys != null && filterKeyComboBox.SelectedIndex > 0 ? filterKeyComboBox.SelectedItem as string : null;

        public SearchWindow()
        {
            Title = "Find";
            Width = 432;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            findTextBox = new TextBox();
            findTextBox.Classes.Add("pathBox");

            searchTypeComboBox = new ComboBox
            {
                ItemsSource = SearchTypeNames,
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            matchCaseCheckBox = new CheckBox { Content = "Match case" };
            wholeWordCheckBox = new CheckBox { Content = "Whole word" };
            regexCheckBox = new CheckBox { Content = "Regex" };
            hintText = new TextBlock { IsVisible = false, Foreground = Avalonia.Media.Brushes.IndianRed, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

            fileTypeComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 400 };

            filterKeyComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
            filterKeyComboBox.SelectionChanged += (_, _) => OnFilterKeyChanged();

            filterValueComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };

            findButton = new Button { Content = "Find", MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
            findButton.Click += (_, _) => Close(true);

            var cancelButton = new Button { Content = "Cancel", MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
            cancelButton.Click += (_, _) => Close(false);

            Content = new StackPanel
            {
                Margin = new(12),
                Spacing = 8,
                Children =
                {
                    findTextBox,
                    searchTypeComboBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children = { matchCaseCheckBox, wholeWordCheckBox, regexCheckBox },
                    },
                    new TextBlock { Text = "File type:" },
                    fileTypeComboBox,
                    new TextBlock { Text = "Asset info filter:" },
                    filterKeyComboBox,
                    filterValueComboBox,
                    hintText,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new(0, 8, 0, 0),
                        Children = { findButton, cancelButton },
                    },
                },
            };

            findTextBox.TextChanged += (_, _) => UpdateState();
            searchTypeComboBox.SelectionChanged += (_, _) => UpdateState();
            matchCaseCheckBox.IsCheckedChanged += (_, _) => UpdateState();
            wholeWordCheckBox.IsCheckedChanged += (_, _) => UpdateState();
            regexCheckBox.IsCheckedChanged += (_, _) => UpdateState();
            fileTypeComboBox.SelectionChanged += (_, _) => UpdateState();
            filterKeyComboBox.SelectionChanged += (_, _) => UpdateState();
            UpdateState();

            Opened += (_, _) =>
            {
                findTextBox.Focus();
                findTextBox.SelectAll();
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close(false);
                }
            };
        }

        private void UpdateState()
        {
            var request = Request;
            var error = PackageSearch.GetError(request.Query);

            matchCaseCheckBox.IsEnabled = wholeWordCheckBox.IsEnabled = regexCheckBox.IsEnabled = request.Query.Type != PackageSearchType.FileContentsHex;
            hintText.Text = error;
            hintText.IsVisible = error != null;
            findButton.IsEnabled = error == null && request is not { Query.Text: "", Extension: null, FilterKey: null };
        }

        /// <summary>
        /// Sets the file types that can be filtered on, keeping the current selection if the type is still available.
        /// </summary>
        public void SetFileTypes(IEnumerable<KeyValuePair<string, int>> types)
        {
            var selectedType = SelectedFileType;
            var sorted = types.OrderBy(static type => type.Key, StringComparer.OrdinalIgnoreCase).ToArray();

            fileTypes = [.. sorted.Select(static type => type.Key)];
            fileTypeComboBox.ItemsSource = sorted
                .Select(static type => string.Create(CultureInfo.InvariantCulture, $".{type.Key} ({type.Value})"))
                .Prepend("(Any type)")
                .ToArray();
            fileTypeComboBox.SelectedIndex = selectedType != null ? Array.IndexOf(fileTypes, selectedType) + 1 : 0;
        }

        /// <summary>
        /// Sets the available SearchableUserData filter keys, showing a loading state until the task completes.
        /// </summary>
        public void SetSearchableUserDataKeys(Task<Dictionary<string, SortedSet<string>>?> keysTask)
        {
            if (keysTask.IsCompletedSuccessfully && ReferenceEquals(keysTask.Result, filterKeys) && filterKeys != null)
            {
                return;
            }

            var version = ++filterLoadVersion;

            if (keysTask.IsCompleted)
            {
                PopulateFilterKeys(keysTask.IsCompletedSuccessfully ? keysTask.Result : null);
                return;
            }

            filterKeys = null;
            filterKeyComboBox.ItemsSource = new[] { "Loading asset info\u2026" };
            filterKeyComboBox.SelectedIndex = 0;
            filterKeyComboBox.IsEnabled = false;
            filterValueComboBox.IsVisible = false;

            keysTask.ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (filterLoadVersion == version)
                {
                    PopulateFilterKeys(t.IsCompletedSuccessfully ? t.Result : null);
                }
            }), TaskScheduler.Default);
        }

        private void PopulateFilterKeys(Dictionary<string, SortedSet<string>>? keys)
        {
            filterKeys = keys is { Count: > 0 } ? keys : null;

            if (filterKeys != null)
            {
                filterKeyComboBox.ItemsSource = filterKeys.Keys.Order(NumericComparer).Prepend("(No filter)").ToArray();
                filterKeyComboBox.IsEnabled = true;
            }
            else
            {
                filterKeyComboBox.ItemsSource = new[] { "No asset info" };
                filterKeyComboBox.IsEnabled = false;
            }

            filterKeyComboBox.SelectedIndex = 0;
            filterValueComboBox.IsVisible = false;
        }

        private void OnFilterKeyChanged()
        {
            if (SelectedFilterKey is not { } key || filterKeys == null || !filterKeys.TryGetValue(key, out var values))
            {
                filterValueComboBox.IsVisible = false;
                return;
            }

            filterValueComboBox.ItemsSource = values.Prepend("(Any value)").ToArray();
            filterValueComboBox.SelectedIndex = 0;
            filterValueComboBox.IsVisible = true;
        }
    }
}
