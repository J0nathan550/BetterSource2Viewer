using Avalonia.Controls;

namespace GUI.Controls;

/// <summary>
/// Shows a control and disposes what it depends on when its tab closes, for tabs that have no file to export.
/// </summary>
sealed class OwningDecorator(params IDisposable[] owned) : Decorator, IDisposable
{
    /// <summary>What this decorator disposes along with itself, such as the viewer of the tab it fills.</summary>
    public IReadOnlyList<IDisposable> Owned => owned;

    public void Dispose()
    {
        foreach (var disposable in owned)
        {
            disposable.Dispose();
        }
    }
}
