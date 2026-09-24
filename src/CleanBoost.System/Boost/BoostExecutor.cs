using CleanBoost.Core.Safety;

namespace CleanBoost.System.Boost;

public enum BoostStepKind
{
    StartupOrphans,   // remove autostart entries pointing at missing files
    DisableDoSv2,     // Delivery Optimization download mode = 0 (needs admin)
    DebloatCurated,   // remove curated bloat packages (per user)
    EmptyRecycleBin,  // permanently deletes only what is already in the Recycle Bin
    FlushDns,         // clear cached DNS lookups
    FlushMemory,      // trim working sets (Turbo only, Caution)
}

public sealed record BoostStep(
    BoostStepKind Kind,
    string Title,
    string Description,
    RiskLevel Risk,
    bool RequiresElevation,
    bool IsTurboOnly);

public sealed record BoostStepResult(BoostStepStep Step, bool Succeeded, string? Message);

public record BoostStepStep(BoostStepKind Kind, string Title, string Result = "");

public static class BoostCatalog
{
    public static IReadOnlyList<BoostStep> Light() => new[]
    {
        new BoostStep(BoostStepKind.StartupOrphans,
            "Remove dead autostart entries",
            "Entries in HKCU/HKLM Run that point to files which no longer exist.",
            RiskLevel.Safe, RequiresElevation: false, IsTurboOnly: false),

        new BoostStep(BoostStepKind.DebloatCurated,
            "Remove curated bloat apps",
            "Only apps on our safe-to-remove list (news, weather, promotions).",
            RiskLevel.Caution, RequiresElevation: false, IsTurboOnly: false),

        new BoostStep(BoostStepKind.DisableDoSv2,
            "Disable Delivery Optimization",
            "Stops using your bandwidth to seed Windows updates to peers.",
            RiskLevel.Safe, RequiresElevation: true, IsTurboOnly: false),

        new BoostStep(BoostStepKind.EmptyRecycleBin,
            "Empty Recycle Bin",
            "Permanently deletes only what is already in the Recycle Bin — all other cleaning stays reversible.",
            RiskLevel.Caution, RequiresElevation: false, IsTurboOnly: false),

        new BoostStep(BoostStepKind.FlushDns,
            "Flush DNS cache",
            "Clears cached DNS lookups so the next resolution is fetched fresh.",
            RiskLevel.Safe, RequiresElevation: false, IsTurboOnly: false),
    };

    public static IReadOnlyList<BoostStep> Turbo() => new[]
    {
        new BoostStep(BoostStepKind.FlushMemory,
            "Flush unused memory",
            "Trims working sets so the OS frees RAM when memory pressure rises.",
            RiskLevel.Caution, RequiresElevation: false, IsTurboOnly: true),
    };
}

/// <summary>
/// Executes boost steps. Every potentially intrusive action (registry write,
/// package removal) is opt-in per step and guarded.
/// </summary>
public sealed class BoostExecutor
{
    public async Task<IReadOnlyList<BoostStepResult>> RunAsync(
        IReadOnlyList<BoostStep> steps,
        IProgress<BoostStepStep>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<BoostStepResult>();
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();
            var stepTitle = step.Title;
            progress?.Report(new BoostStepStep(step.Kind, stepTitle, "running"));
            // Steps touch the registry, WinRT package manager and system
            // processes — run them off the UI thread.
            var result = await Task.Run(() => ExecuteAsync(step, ct), ct).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(result.Step with { Result = result.Succeeded ? "done" : "skipped" });
        }
        return results;
    }

    private static Task<BoostStepResult> ExecuteAsync(BoostStep step, CancellationToken ct)
    {
        try
        {
            return step.Kind switch
            {
                BoostStepKind.StartupOrphans => Task.FromResult(RemoveStartupOrphans()),
                BoostStepKind.DebloatCurated => Task.FromResult(RemoveBloat()),
                BoostStepKind.DisableDoSv2 => Task.FromResult(DisableDoSv2()),
                BoostStepKind.EmptyRecycleBin => Task.FromResult(EmptyRecycleBinNow()),
                BoostStepKind.FlushDns => Task.FromResult(FlushDnsNow()),
                BoostStepKind.FlushMemory => Task.FromResult(FlushMemory()),
                _ => Task.FromResult(new BoostStepResult(ToStep(step), false, "unknown step")),
            };
        }
        catch (Exception ex)
        {
            return Task.FromResult(new BoostStepResult(ToStep(step), false, ex.Message));
        }
    }

    private static BoostStepResult RemoveStartupOrphans()
    {
        var removed = 0;
        foreach (var entry in Startup.StartupManager.GetEntries())
        {
            var path = ExtractExecutable(entry.Command);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                var hive = entry.Root.Contains("HKCU", StringComparison.OrdinalIgnoreCase)
                    ? @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run"
                    : @"HKLM\Software\Microsoft\Windows\CurrentVersion\Run";
                if (entry.Root.Contains("RunOnce", StringComparison.OrdinalIgnoreCase))
                    hive += "Once";
                if (Startup.StartupManager.Disable(hive, entry.Name))
                    removed++;
            }
        }
        return new BoostStepResult(new BoostStepStep(BoostStepKind.StartupOrphans, "startup-orphans"),
            true, $"{removed} dead entries removed");
    }

    private static string? ExtractExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 0 ? trimmed[1..end] : null;
        }
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static BoostStepResult RemoveBloat()
    {
        var removed = 0;
        foreach (var app in Debloat.AppxManager.GetRemovablePackages())
        {
            if (Debloat.AppxManager.Remove(app.FullName) is null)
                removed++;
        }
        return new BoostStepResult(new BoostStepStep(BoostStepKind.DebloatCurated, "debloat"),
            true, $"{removed} packages removed");
    }

    private static BoostStepResult DisableDoSv2()
    {
        var policyKey = Microsoft.Win32.Registry.LocalMachine
            .CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization");
        if (policyKey is null)
            return new BoostStepResult(new BoostStepStep(BoostStepKind.DisableDoSv2, "delivery-optimization"),
                false, "could not open policy key");
        policyKey.SetValue("DODownloadMode", 0, Microsoft.Win32.RegistryValueKind.DWord);
        policyKey.Dispose();
        return new BoostStepResult(new BoostStepStep(BoostStepKind.DisableDoSv2, "delivery-optimization"),
            true, "Delivery Optimization disabled");
    }

    private static BoostStepResult FlushMemory()
    {
        var trimmed = Processes.ProcessTrimmer.TrimAllUsers();
        return new BoostStepResult(new BoostStepStep(BoostStepKind.FlushMemory, "memory"),
            true, $"trimmed working sets of {trimmed} processes");
    }

    private static BoostStepResult EmptyRecycleBinNow()
    {
        var ok = Recycle.RecycleBin.EmptyAll();
        return new BoostStepResult(new BoostStepStep(BoostStepKind.EmptyRecycleBin, "recycle-bin"),
            ok, ok ? "Recycle Bin emptied" : "could not empty the Recycle Bin");
    }

    private static BoostStepResult FlushDnsNow()
    {
        var ok = SystemTweaks.DnsFlusher.Flush();
        return new BoostStepResult(new BoostStepStep(BoostStepKind.FlushDns, "dns"),
            ok, ok ? "DNS cache flushed" : "could not flush DNS cache");
    }

    private static BoostStepStep ToStep(BoostStep step) => new(step.Kind, step.Title);
}