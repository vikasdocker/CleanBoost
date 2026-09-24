using Microsoft.Win32;

namespace CleanBoost.System.Startup;

/// <summary>
/// Lists Windows services with their autostart (Start) value. Start mapping:
/// 0 Boot, 1 System, 2 Automatic, 3 Manual, 4 Disabled.
/// </summary>
public sealed record ServiceInfo(string Name, string? DisplayName, string? ImagePath, int Start);

public static class ServiceTuner
{
    public static IReadOnlyList<ServiceInfo> GetServices(string? filterName = null)
    {
        var result = new List<ServiceInfo>();
        if (!OperatingSystem.IsWindows())
            return result;

        using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (baseKey is null)
            return result;

        foreach (var serviceName in baseKey.GetSubKeyNames())
        {
            if (filterName is not null &&
                !serviceName.Contains(filterName, StringComparison.OrdinalIgnoreCase))
                continue;

            using var key = baseKey.OpenSubKey(serviceName);
            if (key is null)
                continue;

            var start = (int)(key.GetValue("Start", 3) ?? 3);
            var imagePath = key.GetValue("ImagePath") as string;
            var displayName = key.GetValue("DisplayName") as string;
            result.Add(new ServiceInfo(serviceName, displayName, imagePath, start));
        }

        return result;
    }

    /// <summary>
    /// Sets Start to matching start/warning entries. Start value is filtered to
    /// the canonical set {2,3,4} (Automatic/Manual/Disabled).
    /// </summary>
    public static bool SetAutostart(string serviceName, int start)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        if (start is not (2 or 3 or 4))
            return false;

        var subKey = $@"SYSTEM\CurrentControlSet\Services\{serviceName}";
        using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: true);
        if (key is null)
            return false;

        using var before = Registry.LocalMachine.OpenSubKey(subKey);
        if (before?.GetValue("Start") is int existing && existing is 0 or 1)
            return false; // boot/system drivers are off-limits

        key.SetValue("Start", start, RegistryValueKind.DWord);
        return true;
    }
}