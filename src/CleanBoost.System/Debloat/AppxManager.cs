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

    public static string? Remove(string packageFullName)
    {
        if (!OperatingSystem.IsWindows())
            return "not supported on this host";

        try
        {
            var manager = new Windows.Management.Deployment.PackageManager();
            var op = manager.RemovePackageAsync(packageFullName);
            if (op is null)
                return "remove operation failed";

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (op.Status == Windows.Foundation.AsyncStatus.Started)
            {
                if (DateTime.UtcNow > deadline)
                {
                    op.Cancel();
                    op.Close();
                    return "remove timed out";
                }
                global::System.Threading.Thread.Sleep(50);
            }

            var status = op.Status;
            var error = status == Windows.Foundation.AsyncStatus.Error
                ? op.ErrorCode?.Message ?? "remove failed"
                : null;
            op.Close();
            return status == Windows.Foundation.AsyncStatus.Completed ? null : error;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}