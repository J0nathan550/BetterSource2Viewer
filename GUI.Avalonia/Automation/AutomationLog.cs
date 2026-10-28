using GUI.Utils;

namespace GUI.Automation;

/// <summary>
/// Stands in for the log the automation server reads, which only the WinForms viewer keeps.
/// </summary>
static class AutomationLog
{
    /// <summary>Does nothing, as no agent reads the log here.</summary>
    public static void Add(Log.Category category, string component, string message)
    {
    }
}
