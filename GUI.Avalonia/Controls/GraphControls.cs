using System.Linq;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaControls = Avalonia.Controls;

namespace GUI.Controls;

/// <summary>A sidebar button, the Avalonia counterpart of the WinForms ThemedButton.</summary>
internal sealed class ThemedButton : System.Windows.Forms.Control
{
    private readonly AvaloniaControls.Button button;

    /// <param name="repeatWhileHeld">Whether holding the button down keeps clicking it.</param>
    public ThemedButton(string text, Action? onClick = null, bool repeatWhileHeld = false)
        : this(repeatWhileHeld ? new AvaloniaControls.RepeatButton { Content = text } : new AvaloniaControls.Button { Content = text })
    {
        if (onClick != null)
        {
            Click += onClick;
        }
    }

    private ThemedButton(AvaloniaControls.Button button) : base(button)
    {
        this.button = button;
        button.Click += (_, _) => Click?.Invoke();
    }

    public event Action? Click;

    public override string Text
    {
        get => OnUIThread(() => button.Content as string ?? string.Empty);
        set => SetOnUIThread(() => button.Content = value);
    }
}

/// <summary>A button that stays pressed while on, the Avalonia counterpart of the WinForms ThemedToggleButton.</summary>
internal sealed class ThemedToggleButton : System.Windows.Forms.Control
{
    private readonly AvaloniaControls.Primitives.ToggleButton button;

    public ThemedToggleButton(string text) : this(new AvaloniaControls.Primitives.ToggleButton { Content = text })
    {
    }

    private ThemedToggleButton(AvaloniaControls.Primitives.ToggleButton button) : base(button)
    {
        this.button = button;
        button.IsCheckedChanged += (_, _) => Toggled?.Invoke(button.IsChecked == true);
    }

    /// <summary>Raised with the new state when the button is pressed or released.</summary>
    public event Action<bool>? Toggled;

    public bool Checked
    {
        get => OnUIThread(() => button.IsChecked == true);
        set => SetOnUIThread(() => button.IsChecked = value);
    }
}

/// <summary>
/// A fixed height list of named events, the Avalonia counterpart of the WinForms EventListControl. Lines that
/// are no longer current are dimmed.
/// </summary>
internal sealed class EventListControl : System.Windows.Forms.Control
{
    /// <param name="Name">The event name, on the left.</param>
    /// <param name="Kind">What kind of event it is, on the right.</param>
    /// <param name="Current">Whether the event is happening now, as opposed to having just ended.</param>
    public readonly record struct Line(string Name, string Kind, bool Current);

    private const double LineHeight = 17;

    private readonly AvaloniaControls.StackPanel panel;
    private readonly int visibleLines;

    public EventListControl(int visibleLines) : this(new AvaloniaControls.StackPanel { Height = LineHeight * visibleLines }, visibleLines)
    {
    }

    private EventListControl(AvaloniaControls.StackPanel panel, int visibleLines) : base(panel)
    {
        this.panel = panel;
        this.visibleLines = visibleLines;
        SetLines([]);
    }

    public string EmptyText { get; init; } = string.Empty;

    public void SetLines(Line[] lines)
    {
        panel.Children.Clear();

        if (lines.Length == 0)
        {
            panel.Children.Add(CreateLine(EmptyText, string.Empty, current: false));
            return;
        }

        // The last line counts the ones that do not fit
        var shown = lines.Length > visibleLines ? visibleLines - 1 : lines.Length;

        foreach (var line in lines.Take(shown))
        {
            panel.Children.Add(CreateLine(line.Name, line.Kind, line.Current));
        }

        if (shown < lines.Length)
        {
            panel.Children.Add(CreateLine($"+{lines.Length - shown} more", string.Empty, current: false));
        }
    }

    private static AvaloniaControls.Grid CreateLine(string name, string kind, bool current)
    {
        var nameText = new AvaloniaControls.TextBlock
        {
            Text = name,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = current ? 1 : 0.6,
        };

        var kindText = new AvaloniaControls.TextBlock
        {
            Text = kind,
            Opacity = 0.6,
            Margin = new(6, 0, 0, 0),
        };

        AvaloniaControls.Grid.SetColumn(kindText, 1);

        return new AvaloniaControls.Grid
        {
            Height = LineHeight,
            ColumnDefinitions = new AvaloniaControls.ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { nameText, kindText },
        };
    }
}
