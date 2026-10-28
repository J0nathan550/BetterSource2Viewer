using System.Linq;
using System.Windows.Forms;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using AvaloniaControls = Avalonia.Controls;

namespace GUI.Controls;

/// <summary>A combo box entry; headers are shown as non-selectable group titles.</summary>
public class ThemedComboBoxItem
{
    public string Text { get; set; } = string.Empty;
    public bool IsHeader { get; set; }

    public override string ToString() => Text;
}

/// <summary>
/// A 0 to 1 slider. Like the WinForms one, <see cref="ValueChanged"/> only reports user interaction,
/// setting <see cref="Value"/> from code does not raise it.
/// </summary>
internal sealed class Slider : System.Windows.Forms.Control
{
    private readonly AvaloniaControls.Slider slider;
    private bool settingValue;

    public Slider() : this(new AvaloniaControls.Slider
    {
        Minimum = 0,
        Maximum = 1,
        SmallChange = 0.01,
        LargeChange = 0.1,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    })
    {
    }

    private Slider(AvaloniaControls.Slider slider) : base(slider)
    {
        this.slider = slider;

        slider.ValueChanged += (_, e) =>
        {
            if (!settingValue)
            {
                ValueChanged?.Invoke((float)e.NewValue);
            }
        };

        // The thumb handles pointer presses itself, so listen to handled events as well
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            Clicked = true;
            MouseDown?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        slider.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
        {
            Clicked = false;
            MouseUp?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public Action<float>? ValueChanged;

    public event MouseEventHandler? MouseDown;
    public event MouseEventHandler? MouseUp;

    public bool Clicked { get; private set; }

    public float Value
    {
        get => OnUIThread(() => (float)slider.Value);
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);

            SetOnUIThread(() =>
            {
                settingValue = true;

                try
                {
                    slider.Value = clamped;
                }
                finally
                {
                    settingValue = false;
                }
            });
        }
    }
}

internal sealed class GLViewerSliderControl : System.Windows.Forms.Control
{
    public GLViewerSliderControl() : this(new Slider())
    {
    }

    private GLViewerSliderControl(Slider slider) : base(slider.Native)
    {
        Slider = slider;
    }

    public Slider Slider { get; }
}

internal sealed class GLViewerMultiSelectionControl : System.Windows.Forms.Control
{
    public GLViewerMultiSelectionControl(string name) : this(name, new CheckedListBox())
    {
    }

    private GLViewerMultiSelectionControl(string name, CheckedListBox listBox) : base(new AvaloniaControls.StackPanel
    {
        Spacing = 2,
        Children =
        {
            new AvaloniaControls.TextBlock { Text = name },
            new AvaloniaControls.Border
            {
                BorderThickness = new(1),
                BorderBrush = Avalonia.Media.Brushes.Gray,
                CornerRadius = new(3),
                Padding = new(2),
                Child = listBox.Native,
            },
        },
    })
    {
        CheckedListBox = listBox;
    }

    public CheckedListBox CheckedListBox { get; }
}

internal sealed class GLViewerSelectionControl : System.Windows.Forms.Control
{
    /// <param name="fill">Kept for the WinForms signature, the combo box always stretches to the sidebar width.</param>
    public GLViewerSelectionControl(string name, bool horizontal, bool fill = false) : this(name, horizontal, new System.Windows.Forms.ComboBox())
    {
    }

    private GLViewerSelectionControl(string name, bool horizontal, System.Windows.Forms.ComboBox comboBox) : base(CreateLayout(name, horizontal, comboBox.Native))
    {
        ComboBox = comboBox;
    }

    public System.Windows.Forms.ComboBox ComboBox { get; }

    private static AvaloniaControls.Control CreateLayout(string name, bool horizontal, AvaloniaControls.Control comboBox)
    {
        var label = new AvaloniaControls.TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };

        if (!horizontal)
        {
            return new AvaloniaControls.StackPanel { Spacing = 2, Children = { label, comboBox } };
        }

        var grid = new AvaloniaControls.Grid { ColumnDefinitions = new AvaloniaControls.ColumnDefinitions("Auto,*") };
        label.Margin = new(0, 0, 8, 0);
        AvaloniaControls.Grid.SetColumn(comboBox, 1);
        grid.Children.Add(label);
        grid.Children.Add(comboBox);
        return grid;
    }
}

/// <summary>A labelled checkbox row, for viewers that create and place checkboxes themselves.</summary>
internal sealed class GLViewerCheckboxControl : System.Windows.Forms.Control
{
    public GLViewerCheckboxControl(string name, bool isChecked) : this(new System.Windows.Forms.CheckBox())
    {
        CheckBox.Text = name;
        CheckBox.Checked = isChecked;
    }

    private GLViewerCheckboxControl(System.Windows.Forms.CheckBox checkBox) : base(checkBox.Native)
    {
        CheckBox = checkBox;
    }

    public System.Windows.Forms.CheckBox CheckBox { get; }
}

/// <summary>
/// Checkable items in a tree, as Hammer lists its auto visgroups: an item of a path such as
/// <c>Clip/Player Clip</c> is nested under the item of each group before it, which folds it away. Checking or
/// unchecking an item does the same to every item below it.
/// </summary>
internal sealed class GLViewerTreeSelectionControl : System.Windows.Forms.Control
{
    private const char PathSeparator = '/';

    private sealed record Item(string Path, AvaloniaControls.CheckBox CheckBox, AvaloniaControls.TreeViewItem TreeItem, List<Item> Children);

    private readonly List<Item> roots;

    /// <summary>Raised with the paths of the checked items after one is checked or unchecked.</summary>
    public event Action<IEnumerable<string>>? CheckedPathsChanged;

    public GLViewerTreeSelectionControl(string name, IEnumerable<string> paths, bool initiallyChecked, bool foldGroups)
        : this(name, BuildItems(paths, initiallyChecked, out var treeView), treeView, foldGroups)
    {
    }

    private GLViewerTreeSelectionControl(string name, List<Item> items, AvaloniaControls.TreeView treeView, bool foldGroups) : base(new AvaloniaControls.StackPanel
    {
        Spacing = 2,
        Children =
        {
            new AvaloniaControls.TextBlock { Text = name },
            new AvaloniaControls.Border
            {
                BorderThickness = new(1),
                BorderBrush = Avalonia.Media.Brushes.Gray,
                CornerRadius = new(3),
                Padding = new(2),
                Child = treeView,
            },
        },
    })
    {
        roots = items;

        foreach (var item in AllBelow(roots))
        {
            item.TreeItem.IsExpanded = !foldGroups;

            // Click is only raised by the user, rather than by the checks made here
            item.CheckBox.Click += (_, _) => SetChecked(item, item.CheckBox.IsChecked == true);
        }

        // A lone item at the top holds all the others, so it lists its groups, each folded away
        if (foldGroups && roots is [var root])
        {
            root.TreeItem.IsExpanded = true;
        }
    }

    /// <summary>Checks an item and every item below it.</summary>
    /// <param name="path">The path of the item.</param>
    public void Check(string path)
    {
        if (AllBelow(roots).FirstOrDefault(item => item.Path == path) is { } item)
        {
            SetChecked(item, true);
        }
    }

    private void SetChecked(Item item, bool isChecked)
    {
        foreach (var below in AllBelow([item]))
        {
            below.CheckBox.IsChecked = isChecked;
        }

        CheckedPathsChanged?.Invoke([.. AllBelow(roots).Where(static item => item.CheckBox.IsChecked == true).Select(static item => item.Path)]);
    }

    private static IEnumerable<Item> AllBelow(IEnumerable<Item> items)
        => items.SelectMany(static item => AllBelow(item.Children).Prepend(item));

    // Each group of a path gets the item the paths below it are nested in
    private static List<Item> BuildItems(IEnumerable<string> paths, bool isChecked, out AvaloniaControls.TreeView treeView)
    {
        var roots = new List<Item>();
        treeView = new AvaloniaControls.TreeView();

        foreach (var path in paths)
        {
            var items = roots;
            var parentItems = treeView.Items;
            var end = 0;

            while (end < path.Length)
            {
                end = path.IndexOf(PathSeparator, end + 1);
                end = end < 0 ? path.Length : end;

                var groupPath = path[..end];
                var item = items.Find(item => item.Path == groupPath);

                if (item == null)
                {
                    var checkBox = new AvaloniaControls.CheckBox { Content = groupPath[(groupPath.LastIndexOf(PathSeparator) + 1)..], IsChecked = isChecked };
                    item = new Item(groupPath, checkBox, new AvaloniaControls.TreeViewItem { Header = checkBox }, []);
                    items.Add(item);
                    parentItems.Add(item.TreeItem);
                }

                items = item.Children;
                parentItems = item.TreeItem.Items;
            }
        }

        return roots;
    }
}
