namespace GUI.Automation;

/// <summary>
/// Stands in for the clock an agent can pause and step, which only the WinForms viewer has.
/// </summary>
static class AutomationClock
{
    /// <summary>Leaves the timestep alone, as nothing holds the simulation here.</summary>
    /// <returns>Always false.</returns>
    public static bool Advance(ref float timestep) => false;
}
