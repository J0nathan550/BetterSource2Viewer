// See Keys.cs for why this namespace exists in an Avalonia project.

namespace System.Drawing;

/// <summary>
/// Stands in for the WinForms icon images that shared viewer code keeps in fields and disposes. The Avalonia
/// viewers label their buttons with text instead, so nothing creates one.
/// </summary>
public sealed class Image : IDisposable
{
    private Image()
    {
    }

    public void Dispose()
    {
    }
}
