using System.Diagnostics;
using System.Security.Principal;

namespace CleanBoost.System.Elevation;

/// <summary>
/// Detects and triggers administrator elevation. The app runs normally by
/// default; the user opts into elevated mode (restart as administrator) so
/// protected categories can be cleaned and admin-only boost steps can run.
/// </summary>
public static class ElevationHelper
{
    /// <summary>True when the current process is running with an administrator token.</summary>
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            return new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Restarts the current app with the "run as administrator" verb.
    /// Returns false when elevation was denied by the user or failed.
    /// </summary>
    public static bool RelaunchElevated(string[]? args = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(processPath))
            return false;

        var psi = new ProcessStartInfo
        {
            FileName = processPath,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };

        if (args is { Length: > 0 })
            psi.Arguments = string.Join(' ', args.Select(a => $"\"{a}\""));

        try
        {
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false; // UAC dismissed or launch failed — stay non-elevated
        }
    }
}