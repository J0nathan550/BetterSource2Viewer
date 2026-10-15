using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Utils;

namespace GUI.Utils;

/// <summary>
/// Shared wording and dialog for showing an error to the user, with advice that depends on the known update state.
/// The Avalonia counterpart of GUI/Utils/ErrorReport.cs, which shows a WinForms task dialog.
/// </summary>
static class ErrorReport
{
    private const string NewerFormatHeading = "This file may use a format that this version of S2V does not support";
    private const string GenericHeading = "An error occurred";

    /// <summary>
    /// Logs the exception and shows an error dialog. Can be called from any thread, and before the main window exists.
    /// </summary>
    /// <param name="exception">The exception to report.</param>
    /// <param name="context">What was being done when the exception happened, such as a file path.</param>
    public static void ShowError(Exception exception, string? context = null)
    {
        Log.Error(nameof(ErrorReport), context == null ? exception.ToString() : $"Failed on \"{context}\": {exception}");

        var report = new StringBuilder(512);
        AppendExceptionWithVersion(report, exception, context);

        var text = context == null ? exception.Message : $"{exception.Message}{Environment.NewLine}{context}";

        ShowDialog(GetHeading(exception), text, report.ToString());
    }

    /// <summary>
    /// Starts the once per session update check, and refreshes the bottom panel badge from the recorded update state.
    /// Safe to call from any thread.
    /// </summary>
    public static void ShowUpdateAfterError()
    {
        UpdateChecker.CheckAfterError();
        RefreshUpdateBadge();
    }

    /// <summary>
    /// Refreshes the bottom panel badge from the recorded update state. Safe to call from any thread.
    /// </summary>
    public static void RefreshUpdateBadge() => Dispatcher.UIThread.Post(static () => Program.MainForm?.ShowUpdateAfterError());

    /// <summary>
    /// Writes the exception in a code block, followed by the version line the issue bot reads.
    /// </summary>
    public static void AppendExceptionWithVersion(StringBuilder output, Exception exception, string? context = null)
    {
        output.AppendLine("```");

        if (context != null)
        {
            output.Append("Context: ");
            output.AppendLine(context);
            output.AppendLine();
        }

        output.AppendLine(exception.ToString());

        if (exception.Data[AnimationGraph.ExceptionDataKey] is string graphState)
        {
            output.AppendLine();
            output.AppendLine(AnimationGraph.ExceptionDataKey + ":");
            output.Append(graphState);
        }

        output.AppendLine();
        output.Append("S2V ");
        output.Append(Program.ProductVersion.Replace('+', ' '));
        output.Append(CultureInfo.InvariantCulture, $" on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

        if (GLEnvironment.GpuRendererAndDriver != null)
        {
            output.Append(CultureInfo.InvariantCulture, $" ({GLEnvironment.GpuRendererAndDriver})");
        }

        output.AppendLine();
        output.AppendLine("```");
    }

    public static string GetHeading(Exception exception)
    {
        // These are mostly thrown for versions and types the parsers do not know yet, which a game update can introduce
        var mayBeNewerFormat = exception is UnexpectedMagicException or NotImplementedException or NotSupportedException;

        return mayBeNewerFormat ? NewerFormatHeading : GenericHeading;
    }

    /// <summary>
    /// A sentence about what to do next, based on the recorded update state. Empty for local builds.
    /// </summary>
    public static string GetAdvice() => GetAdvice(UpdateChecker.GetUpdateStatus());

    /// <summary>
    /// Shows an error dialog with the report in it. Can be called from any thread, and before the main window exists.
    /// </summary>
    /// <param name="heading">The title of the dialog.</param>
    /// <param name="text">The body text, which is followed by advice based on the update state.</param>
    /// <param name="report">The text the copy button places on the clipboard.</param>
    public static void ShowDialog(string heading, string text, string report)
    {
        ShowUpdateAfterError();

        var advice = GetAdvice();
        var body = advice.Length > 0 ? $"{text}{Environment.NewLine}{Environment.NewLine}{advice}" : text;

        // Errors can be raised from loader and render threads, the dialog must be created on the UI thread
        if (Dispatcher.UIThread.CheckAccess())
        {
            ErrorDialog.Show(heading, body, report);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ErrorDialog.Show(heading, body, report));
        }
    }

    private static string GetAdvice(UpdateChecker.UpdateStatus status)
    {
        var state = Settings.Config.Update;
        string newerBuild;

        switch (status)
        {
            case UpdateChecker.UpdateStatus.NewerDev:
                newerBuild = $"dev build {state.LatestDevVersion}";
                break;
            case UpdateChecker.UpdateStatus.NewerStable:
                newerBuild = $"version {state.LatestStableVersion}";
                break;
            case UpdateChecker.UpdateStatus.UpToDate:
                return "You are on the latest build. Report this error with your exact steps.";
            case UpdateChecker.UpdateStatus.Unknown:
                return "Check for updates in the About window and try the latest dev build before reporting this error.";
            default:
                return string.Empty;
        }

        var where = "from the About window";

        if (status == UpdateChecker.UpdateStatus.NewerDev && state.Channel == Settings.UpdateChannel.Stable)
        {
            where = "by switching to the Dev update channel in the About window";
        }

        return $"A newer build ({newerBuild}) is available {where}. Try it before reporting this error.";
    }
}
