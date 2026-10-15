// See Keys.cs for why this namespace exists in an Avalonia project.

using System.Collections;
using AvaloniaControls = Avalonia.Controls;

namespace System.Windows.Forms;

/// <summary>A vertical stack of compatibility controls, for the shared code that hosts its rows in a WinForms panel.</summary>
public class Panel : Control
{
    private readonly AvaloniaControls.StackPanel stackPanel;

    public Panel() : this(new AvaloniaControls.StackPanel())
    {
    }

    private Panel(AvaloniaControls.StackPanel stackPanel) : base(stackPanel)
    {
        this.stackPanel = stackPanel;
        Controls = new ControlCollection(stackPanel);
        stackPanel.SizeChanged += (_, _) => SizeChanged?.Invoke(this, EventArgs.Empty);
    }

    public ControlCollection Controls { get; }

    public event EventHandler? SizeChanged;

    public Drawing.Size ClientSize => OnUIThread(() => new Drawing.Size((int)stackPanel.Bounds.Width, (int)stackPanel.Bounds.Height));

    /// <summary>The panel's height, or 0 to collapse it. Unlike WinForms, a shown panel sizes to its rows.</summary>
    public int Height
    {
        get => OnUIThread(() => (int)stackPanel.Bounds.Height);
        set => SetOnUIThread(() => stackPanel.Height = value > 0 ? double.NaN : 0);
    }

    public sealed class ControlCollection(AvaloniaControls.Panel owner) : IEnumerable<Control>
    {
        private readonly List<Control> controls = [];

        public int Count => controls.Count;

        public void Add(Control control)
        {
            controls.Add(control);
            SetOnUIThread(() => owner.Children.Add(control.Native));
        }

        public IEnumerator<Control> GetEnumerator() => controls.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
