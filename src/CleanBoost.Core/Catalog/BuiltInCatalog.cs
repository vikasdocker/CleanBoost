using CleanBoost.Core.Catalog;
using CleanBoost.Core.Safety;

namespace CleanBoost.Core.Catalog;

/// <summary>
/// The built-in, hand-curated cleanup catalog: Windows leftovers, browser
/// caches and the most common application caches. Community coverage for the
/// long tail comes from the winapp2.ini database (see Rules.WinappParser).
/// </summary>
public static class BuiltInCatalog
{
    public static IReadOnlyList<CleanCategory> Create() => new List<CleanCategory>
    {
        Files("user-temp", "User temporary files", RiskLevel.Safe,
            "Throwaway files created by apps and Windows in your profile. Files in use are locked and skipped automatically.",
            "\uE74C",
            T("%Temp%", recurse: true, removeSelf: true)),

        FilesElevated("windows-temp", "Windows temporary files", RiskLevel.Safe,
            "System temp files under C:\\Windows\\Temp. Needs administrator rights.",
            "\uEDAB", T("%WinDir%\\Temp", recurse: true, removeSelf: true)),

        FilesElevated("update-leftovers", "Windows Update leftovers", RiskLevel.Safe,
            "Downloaded updates that have already been installed. Safe to remove.",
            "\uE768",
            T("%WinDir%\\SoftwareDistribution\\Download", recurse: true, removeSelf: true)),

        FilesElevated("delivery-optimization", "Delivery Optimization files", RiskLevel.Safe,
            "Background-update peer cache. Rebuilt automatically by Windows.",
            "\uE8AB", T("%WinDir%\\SoftwareDistribution\\DeliveryOptimization", recurse: true, removeSelf: true)),

        Files("thumbnails", "Thumbnail cache", RiskLevel.Safe,
            "File-Explorer previews. Windows rebuilds them on demand.",
            "\uE7B0", T("%LocalAppData%\\Microsoft\\Windows\\Explorer", "thumbcache_*.db;iconcache_*.db")),

        Files("wer-reports", "Windows Error Reports", RiskLevel.Safe,
            "Crash and error reports sent to Microsoft diagnostics. Diagnostic only.",
            "\uE783",
            T("%LocalAppData%\\Microsoft\\Windows\\WER", recurse: true, removeSelf: true),
            T("%ProgramData%\\Microsoft\\Windows\\WER", recurse: true, removeSelf: true)),

        Files("crash-dumps", "Crash dump files", RiskLevel.Caution,
            "Crash dumps (user CrashDumps, Minidump and MEMORY.DMP). Only useful for debugging crashes.",
            "\uE7BA",
            T("%LocalAppData%\\CrashDumps", recurse: true, removeSelf: true),
            T("%WinDir%\\Minidump", recurse: true, removeSelf: true),
            T("%WinDir%\\MEMORY.DMP")),

        Files("inet-cache", "Temporary Internet files", RiskLevel.Safe,
            "Legacy Internet Explorer / system web cache.",
            "\uED23", T("%LocalAppData%\\Microsoft\\Windows\\INetCache", recurse: true, removeSelf: true)),

        FilesElevated("prefetch", "Prefetch data", RiskLevel.Caution,
            "Fast-launch hints. Harmless to remove; apps just pre-warm again. No benefit on SSDs.",
            "\uE7C4", T("%WinDir%\\Prefetch", "*.pf", recurse: true, removeSelf: true)), 

        Files("shader-caches", "GPU shader caches", RiskLevel.Caution,
            "Compiled shaders (DirectX/NVIDIA). Games recompile briefly on first launch after cleaning.",
            "\uE7FC",
            T("%LocalAppData%\\D3DSCache", recurse: true, removeSelf: true),
            T("%LocalAppData%\\NVIDIA\\DXCache", recurse: true, removeSelf: true),
            T("%LocalAppData%\\NVIDIA\\GLCache", recurse: true, removeSelf: true)),

        Files("chrome-cache", "Chrome / Chromium caches", RiskLevel.Safe,
            "Cached web assets. Passwords, cookies and local storage are never touched.",
            "\uE85A",
            T("%LocalAppData%\\Google\\Chrome\\User Data\\*\\Cache", recurse: true, removeSelf: true),
            T("%LocalAppData%\\Google\\Chrome\\User Data\\*\\Code Cache", recurse: true, removeSelf: true),
            T("%LocalAppData%\\Chromium\\User Data\\*\\Cache", recurse: true, removeSelf: true)),

        Files("edge-cache", "Microsoft Edge cache", RiskLevel.Safe,
            "Edge cache, including Code Cache. Personal data is never touched.",
            "\uEA8A",
            T("%LocalAppData%\\Microsoft\\Edge\\User Data\\*\\Cache", recurse: true, removeSelf: true),
            T("%LocalAppData%\\Microsoft\\Edge\\User Data\\*\\Code Cache", recurse: true, removeSelf: true)),

        Files("firefox-cache", "Firefox cache", RiskLevel.Safe,
            "Firefox disk and startup cache. Bookmarks and logins are untouched.",
            "\uE85B",
            T("%LocalAppData%\\Mozilla\\Firefox\\Profiles\\*\\cache2", recurse: true, removeSelf: true),
            T("%LocalAppData%\\Mozilla\\Firefox\\Profiles\\*\\startupCache", recurse: true, removeSelf: true)),

        Files("opera-cache", "Opera / Brave caches", RiskLevel.Safe,
            "Cache folders for Opera and Brave.",
            "\uE85A",
            T("%LocalAppData%\\Opera Software\\Opera Stable\\Cache", recurse: true, removeSelf: true),
            T("%AppData%\\Opera Software\\Opera Stable\\Cache", recurse: true, removeSelf: true),
            T("%LocalAppData%\\BraveSoftware\\Brave-Browser\\User Data\\*\\Cache", recurse: true, removeSelf: true)),

        Files("discord-cache", "Discord cache", RiskLevel.Safe,
            "Discord's image/font cache folders.",
            "\uE8F1",
            T("%AppData%\\discord\\Cache", recurse: true, removeSelf: true),
            T("%AppData%\\discord\\Code Cache", recurse: true, removeSelf: true),
            T("%AppData%\\discord\\GPUCache", recurse: true, removeSelf: true)),

        Files("slack-cache", "Slack cache", RiskLevel.Safe,
            "Slack's webview cache (keeps signatures intact).",
            "\uE8F1",
            T("%AppData%\\Slack\\Cache", recurse: true, removeSelf: true),
            T("%AppData%\\Slack\\Code Cache", recurse: true, removeSelf: true)),

        Files("teams-cache", "Teams cache", RiskLevel.Safe,
            "Microsoft Teams cache and logs.",
            "\uEA8A",
            T("%AppData%\\Microsoft\\Teams\\cache", recurse: true, removeSelf: true),
            T("%AppData%\\Microsoft\\Teams\\Code Cache", recurse: true, removeSelf: true),
            T("%AppData%\\Microsoft\\Teams\\GPUCache", recurse: true, removeSelf: true),
            T("%AppData%\\Microsoft\\Teams\\logs", recurse: true, removeSelf: true)),

        Files("zoom-cache", "Zoom cache", RiskLevel.Safe,
            "Zoom's cache folders.",
            "\uE8BD",
            T("%AppData%\\Zoom\\Logs", recurse: true, removeSelf: true),
            T("%AppData%\\Zoom\\data\\VirtualBrick\\Cache", recurse: true, removeSelf: true)),

        Files("vscode-cache", "Visual Studio Code cache", RiskLevel.Safe,
            "Code editor cache and logs.",
            "\uE943",
            T("%AppData%\\Code\\Cache", recurse: true, removeSelf: true),
            T("%AppData%\\Code\\CachedData", recurse: true, removeSelf: true),
            T("%AppData%\\Code\\CachedExtensionVSIXs", recurse: true, removeSelf: true),
            T("%AppData%\\Code\\GPUCache", recurse: true, removeSelf: true),
            T("%AppData%\\Code\\logs", recurse: true, removeSelf: true)),

        Files("spotify-cache", "Spotify cache", RiskLevel.Safe,
            "Spotify cached artwork and audio previews.",
            "\uE96B",
            T("%LocalAppData%\\Spotify\\Data", recurse: true, removeSelf: true),
            T("%AppData%\\Spotify\\Cache", recurse: true, removeSelf: true)),

        FilesElevated("steam-cache", "Steam app cache", RiskLevel.Safe,
            "Steam library caches. Requires administrator.",
            "\uE89C",
            T("%ProgramFiles(x86)%\\Steam\\appcache", recurse: true, removeSelf: true)),
            

        Files("recent-history", "Recent documents history", RiskLevel.ConfirmFirst,
            "Quick-jump history of recently opened files. Privacy option — does not delete the files themselves.",
            "\uE8F4",
            T("%AppData%\\Microsoft\\Windows\\Recent", recurse: true, removeSelf: true)),
    };

    private static CleanCategory Files(string key, string title, RiskLevel risk, string description,
                                       string icon, params CleanTarget[] targets) =>
        new()
        {
            Key = key,
            Title = title,
            Description = description,
            Risk = risk,
            Icon = icon,
            Targets = targets.ToList(),
        };

    private static CleanCategory FilesElevated(string key, string title, RiskLevel risk, string description,
                                               string icon, params CleanTarget[] targets) =>
        new()
        {
            Key = key,
            Title = title,
            Description = description,
            Risk = risk,
            Icon = icon,
            RequiresElevation = true,
            Targets = targets.ToList(),
        };

    private static CleanTarget T(string path, string patterns = "*", bool recurse = false,
                                 bool removeSelf = false) =>
        new() { Path = path, Patterns = patterns, Recurse = recurse, RemoveSelf = removeSelf };
}