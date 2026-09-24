using System.Diagnostics;

namespace CleanBoost.System.SystemTweaks;

/// <summary>Non-destructive system maintenance invoked by boost steps.</summary>
public static class DnsFlusher
{
    public static bool Flush()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(15_000);
            return p is not null && p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}