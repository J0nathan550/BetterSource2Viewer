namespace GUI.Automation;

/// <summary>
/// The automation server only runs in the WinForms viewer, so shared code that checks for it always finds it off here.
/// </summary>
static class Automation
{
    /// <summary>Always false, as no agent can drive this viewer.</summary>
    public static bool IsEnabled => false;
}
