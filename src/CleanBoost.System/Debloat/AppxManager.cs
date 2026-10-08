namespace CleanBoost.System.Debloat;

public sealed record AppInfo(string Name, string PackageFamilyName, string FullName, string Version);

/// <summary>
/// Lists and optionally removes appx packages (for the current user). The
/// curated ignore list keeps Windows Store apps that are load-bearing, even
/// though several appear on typical "debloat" lists.
/// </summary>
public static class AppxManager
{
    // These can be safely removed for the current user; many are preinstalled
    // junk on consumer builds. Anything not on this list is never touched.
    private static readonly HashSet<string> CuratedRemovable = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.BingNews", "Microsoft.BingWeather", "Microsoft.BingSports", "Microsoft.BingFinance",
        "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.MicrosoftOfficeHub", "Microsoft.MicrosoftSolitaireCollection",
        "Microsoft.Office.OneNote", "Microsoft.People", "Microsoft.SkypeApp", "Microsoft.WindowsFeedbackHub",
        "Microsoft.WindowsMaps", "Microsoft.XboxApp", "Microsoft.XboxGameCallableUI", "Microsoft.XboxGamingOverlay",
        "Microsoft.XboxSpeechToTextOverlay", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo", "Microsoft.YourPhone",
        "Microsoft.Todos", "Microsoft.WindowsCamera", "Microsoft.WindowsCommunicationsApps", "Microsoft.549981C3F5F10",
        "Clipchamp.Clipchamp", "SpotifyAB.SpotifyMusic", "Disney.37853FC22B2CE", "Netflix.Netflix",
        "xandr.BingRewards", "Microsoft.WindowsAlarms", "Microsoft.WindowsCalculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe",
        "Microsoft.Advertising.Xaml", "Microsoft.MixedReality.Portal", "Microsoft.MSPaint", "Microsoft.Office.Sway",
    };

    public static IReadOnlyList<AppInfo> GetRemovablePackages()
    {
        var result = new List<AppInfo>();
        if (!OperatingSystem.IsWindows())
            return result;

        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            var packages = manager.FindPackages();
            foreach (var package in packages)
            {
                var name = package.Id.Name;
                if (!CuratedRemovable.Contains(name))
                    continue;

                var version = package.Id.Version;
                result.Add(new AppInfo(
                    name,
                    package.Id.FamilyName,
                    package.Id.FullName,
                    $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"));
            }
        }
        catch
        {
            // WinRT projection unavailable (e.g. non-Windows host) — nothing listed.
        }

        return result;
    }

    /// <summary>Removes an appx package, genuinely awaiting the WinRT operation.</summary>
    /// <param name="packageFullName">Full package name to remove.</param>
    /// <param name="timeout">Hard ceiling on how long a single removal may take.</param>
    /// <param name="ct">Cancellation, honoured both before and during the wait.</param>
    public static async Task<string?> RemoveAsync(string packageFullName,
                                                 TimeSpan? timeout = null,
                                                 CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return "not supported on this host";

        ct.ThrowIfCancellationRequested();

        var manager = new Windows.Management.Deployment.PackageManager();
        var op = manager.RemovePackageAsync(packageFullName);
        if (op is null)
            return "remove operation failed";

        // A real await rather than a Thread.Sleep poll loop. The old loop blocked a
        // thread-pool thread for up to 60s per package, so "Applying boost" could sit
        // frozen for half an hour with no way to escape it.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));

        try
        {
            await op.AsTask(deadline.Token).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { op.Cancel(); } catch { /* best effort */ }
            return "remove timed out";
        }
        catch (OperationCanceledException)
        {
            try { op.Cancel(); } catch { /* best effort */ }
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { op.Close(); } catch { /* best effort */ }
        }
    }
}