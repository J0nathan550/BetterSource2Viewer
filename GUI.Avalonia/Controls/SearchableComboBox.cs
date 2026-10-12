using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace GUI.Controls;

/// <summary>
/// A drop down list that opens its items under a search box, which narrows them down to the ones whose text contains
/// every word typed, like the WinForms SearchableComboBox. Typing while it has focus opens it with what was typed.
/// The selection only changes once an item is picked, so it behaves like any other drop down list to the code using it.
/// </summary>
sealed class SearchableComboBox : ComboBox
{
    private const double ItemHeight = 24;

    // Rows with pictures are about twice as tall, so fewer fit before the list gets taller than the screen
    private const int MaxImageRows = 10;

    private readonly Popup popup;
    private readonly TextBox searchTextBox;
    private readonly ListBox listBox;
    private readonly Border popupContent;
    private Func<object?, string> itemText = static item => item?.ToString() ?? string.Empty;
    private Func<object?, IImage?>? itemImage;

    protected override Type StyleKeyOverride => typeof(ComboBox);

    /// <summary>Most items shown at once before the list scrolls.</summary>
    public int MaxDropDownItems { get; set; } = 20;

    /// <summary>Width of the search, when it should be wider than the box.</summary>
    public double DropDownWidth { get; set; }

    /// <summary>Whether the search is open.</summary>
    public bool SearchOpen => popup.IsOpen;

    /// <summary>The text items are shown and searched by, their ToString by default.</summary>
    public Func<object?, string> ItemText
    {
        get => itemText;
        set
        {
            itemText = value;
            ItemTemplate = CreateBoxTemplate();
        }
    }

    /// <summary>
    /// Gets the picture shown next to an item, or null for none. The pictures stay the caller's, which keeps them alive
    /// while the box is shown. When set, the list's rows are tall enough for <see cref="ImageWidth"/> by
    /// <see cref="ImageHeight"/>, and the box shows the chosen item's picture too.
    /// </summary>
    public Func<object?, IImage?>? ItemImage
    {
        get => itemImage;
        set
        {
            itemImage = value;
            ItemTemplate = CreateBoxTemplate();
        }
    }

    /// <summary>Width of the box pictures are fit into in the list.</summary>
    public double ImageWidth { get; set; } = 60;

    /// <summary>Height of the box pictures are fit into in the list.</summary>
    public double ImageHeight { get; set; } = 40;

    private double RowHeight => itemImage != null ? Math.Max(ItemHeight, ImageHeight + 4) : ItemHeight;

    public SearchableComboBox()
    {
        IsTextSearchEnabled = false;
        ItemTemplate = CreateBoxTemplate();

        searchTextBox = new TextBox
        {
            PlaceholderText = "Search",
            BorderThickness = new(0),
            Margin = new(0),
        };
        searchTextBox.Classes.Add("search");
        searchTextBox.TextChanged += (_, _) => Filter();
        searchTextBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);

        listBox = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new FuncDataTemplate<object>((item, _) => CreateItemView(item, ImageWidth, ImageHeight, 6), supportsRecycling: false),
        };
        listBox.Classes.Add("searchList");
        ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
        listBox.AddHandler(PointerReleasedEvent, OnListPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);

        var searchPanel = new Border
        {
            Padding = new(6),
            Child = searchTextBox,
        };
        searchPanel.Classes.Add("searchHeader");
        DockPanel.SetDock(searchPanel, Dock.Top);

        popupContent = new Border
        {
            Child = new DockPanel { Children = { searchPanel, listBox } },
        };
        popupContent.Classes.Add("searchPopup");

        popup = new Popup
        {
            Child = popupContent,
            PlacementTarget = this,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
        };
        popup.Opened += (_, _) => searchTextBox.Focus();
        popup.Closed += (_, _) => Focus();

        // Without a parent the popup's window gets no styles or templates and shows up blank
        LogicalChildren.Add(popup);
    }

    // The box is about as tall as a line of text, so its picture is shrunk to that
    private FuncDataTemplate<object> CreateBoxTemplate() => new(
        (item, _) => CreateItemView(item, ImageWidth * 20 / ImageHeight, 20, 4),
        supportsRecycling: false);

    private Control CreateItemView(object? item, double imageWidth, double imageHeight, double spacing)
    {
        var text = new TextBlock
        {
            Text = itemText(item),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        if (itemImage == null)
        {
            return text;
        }

        var image = new Image
        {
            Source = itemImage(item),
            Width = imageWidth,
            Height = imageHeight,
            Stretch = Stretch.Uniform,
            Margin = new(0, 0, spacing, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(image, Dock.Left);

        return new DockPanel { Children = { image, text } };
    }

    /// <summary>
    /// Opens the search, starting with the given text.
    /// </summary>
    public void OpenSearch(string text = "")
    {
        if (ItemCount == 0 || SearchOpen || !IsEffectivelyEnabled)
        {
            return;
        }

        searchTextBox.Text = text;
        searchTextBox.CaretIndex = text.Length;
        Filter();

        // Sized for every item, so the list does not jump around while it narrows down
        var rows = Math.Clamp(ItemCount, 1, itemImage != null ? Math.Min(MaxDropDownItems, MaxImageRows) : MaxDropDownItems);
        listBox.Height = (rows * RowHeight) + 2;
        popupContent.Width = Math.Max(Bounds.Width, DropDownWidth);

        popup.IsOpen = true;
    }

    private bool IsFromSearch(object? source) => source is Visual visual && (visual == popupContent || popupContent.IsVisualAncestorOf(visual));

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!e.Handled && e.InitialPressMouseButton == MouseButton.Left && !IsFromSearch(e.Source))
        {
            // Handled before the box sees it, so its own drop down stays closed
            e.Handled = true;
            Focus();
            OpenSearch();
        }

        base.OnPointerReleased(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (IsFromSearch(e.Source))
        {
            return;
        }

        if (e.Key == Key.F4 || (e.Key is Key.Down or Key.Up && e.KeyModifiers.HasFlag(KeyModifiers.Alt)))
        {
            e.Handled = true;
            OpenSearch();
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (IsFromSearch(e.Source))
        {
            return;
        }

        if (!string.IsNullOrEmpty(e.Text) && !char.IsControl(e.Text[0]))
        {
            e.Handled = true;
            OpenSearch(e.Text);
            return;
        }

        base.OnTextInput(e);
    }

    /// <summary>
    /// Lists the items that contain every word of the search, selecting the chosen item while nothing is typed and
    /// the first match otherwise, which Enter picks.
    /// </summary>
    private void Filter()
    {
        var words = (searchTextBox.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = Items.Cast<object?>()
            .Where(item => words.All(word => itemText(item).Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        listBox.ItemsSource = matches;

        var selected = words.Length == 0 && SelectedItem != null ? matches.IndexOf(SelectedItem) : -1;
        listBox.SelectedIndex = selected >= 0 ? selected : matches.Count > 0 ? 0 : -1;

        if (listBox.SelectedItem is { } item)
        {
            listBox.ScrollIntoView(item);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        var page = Math.Max(1, (int)(listBox.Bounds.Height / RowHeight) - 1);

        switch (e.Key)
        {
            case Key.Enter:
            case Key.Tab:
                Pick();
                break;

            case Key.Escape:
                popup.IsOpen = false;
                break;

            case Key.Down:
                MoveSelection(1);
                break;

            case Key.Up:
                MoveSelection(-1);
                break;

            case Key.PageDown:
                MoveSelection(page);
                break;

            case Key.PageUp:
                MoveSelection(-page);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void MoveSelection(int by)
    {
        if (listBox.ItemCount == 0)
        {
            return;
        }

        listBox.SelectedIndex = Math.Clamp(listBox.SelectedIndex + by, 0, listBox.ItemCount - 1);

        if (listBox.SelectedItem is { } item)
        {
            listBox.ScrollIntoView(item);
        }
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left
            && e.Source is Visual visual
            && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
        {
            Pick();
        }
    }

    private void Pick()
    {
        var item = listBox.SelectedItem;

        popup.IsOpen = false;

        if (item != null)
        {
            SelectedItem = item;
        }
    }
}
