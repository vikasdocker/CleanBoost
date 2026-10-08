using System.Diagnostics;

namespace CleanBoost.System.SystemTweaks;

/// <summary>Non-destructive system maintenance invoked by boost steps.</summary>
public static class DnsFlusher
{
    public static async Task<bool> FlushAsync(CancellationToken ct = default)
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
            if (p is null)
                return false;

            // Awaited rather than WaitForExit, so a boost run can be cancelled
            // instead of parking a thread-pool thread for the full timeout.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(15_000);

            try
            {
                await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}