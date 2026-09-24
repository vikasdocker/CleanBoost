using Microsoft.Win32;

namespace CleanBoost.System.Startup;

public sealed record RunEntry(string Name, string Command, string Root, bool RequiresElevation);

/// <summary>
/// Reads the Run/RunOnce autostart keys. The one explicit registry WRITE this
/// app ever performs is removing a startup entry the user chose to disable —
/// it is a settings toggle, not a "cleaner".
/// </summary>
public static class StartupManager
{
    private static readonly (string HivePath, string RootName, bool Elev)[] Locations =
    {
        (@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run", false),
        (@"HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce", false),
        (@"HKLM\Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run", true),
        (@"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce", true),
    };

    public static IReadOnlyList<RunEntry> GetEntries()
    {
        var result = new List<RunEntry>();
        if (!OperatingSystem.IsWindows())
            return result;

        foreach (var (hivePath, name, elev) in Locations)
        {
            using var key = OpenKey(hivePath, writable: false);
            if (key is null)
                continue;
            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not string command)
                    continue;
                result.Add(new RunEntry(valueName, command, name, elev));
            }
        }

        return result;
    }

    /// <summary>Removes a single startup value (after the user confirms it).</summary>
    public static bool Disable(string hivePath, string name)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var key = OpenKey(hivePath, writable: true);
        if (key is null)
            return false;
        key.DeleteValue(name, throwOnMissingValue: false);
        return true;
    }

    private static RegistryKey? OpenKey(string hivePath, bool writable)
    {
        var separator = hivePath.IndexOf('\\');
        if (separator <= 0)
            return null;

        var hiveName = hivePath[..separator];
        var subPath = hivePath[(separator + 1)..];
        var hive = hiveName switch
        {
            "HKLM" => Registry.LocalMachine,
            "HKCU" => Registry.CurrentUser,
            _ => null,
        };
        return hive?.OpenSubKey(subPath, writable);
    }
}