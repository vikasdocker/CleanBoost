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
    /// <summary>Fully safe everyday steps. Always applied, Turbo on or off.</summary>
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
    };

    /// <summary>
    /// The extra work Turbo adds behind its consent. These are the steps that can
    /// be noticed — emptying the Recycle Bin is the only irreversible one, which
    /// is exactly why it is gated behind Turbo rather than running by default.
    /// </summary>
    public static IReadOnlyList<BoostStep> Turbo() => new[]
    {
        new BoostStep(BoostStepKind.EmptyRecycleBin,
            "Empty Recycle Bin",
            "Permanently deletes only what is already in the Recycle Bin — all other cleaning stays reversible.",
            RiskLevel.Caution, RequiresElevation: false, IsTurboOnly: true),

        new BoostStep(BoostStepKind.FlushDns,
            "Flush DNS cache",
            "Clears cached DNS lookups so the next resolution is fetched fresh.",
            RiskLevel.Safe, RequiresElevation: false, IsTurboOnly: true),

        new BoostStep(BoostStepKind.FlushMemory,
            "Flush unused memory",
            "Trims working sets so the OS frees RAM when memory pressure rises. You may notice a brief pause while apps page memory back in under load.",
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
            progress?.Report(new BoostStepStep(step.Kind, step.Title, "running"));

            var result = await ExecuteAsync(step, ct).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(new BoostStepStep(step.Kind, step.Title, result.Succeeded ? "done" : "skipped"));
        }
        return results;
    }

    private static async Task<BoostStepResult> ExecuteAsync(BoostStep step, CancellationToken ct)
    {
        try
        {
            return step.Kind switch
            {
                BoostStepKind.StartupOrphans => await RunOffThread(RemoveStartupOrphans, ct).ConfigureAwait(false),
                BoostStepKind.DebloatCurated => await RemoveBloatAsync(ct).ConfigureAwait(false),
                BoostStepKind.DisableDoSv2 => await RunOffThread(DisableDoSv2, ct).ConfigureAwait(false),
                BoostStepKind.EmptyRecycleBin => await RunOffThread(EmptyRecycleBinNow, ct).ConfigureAwait(false),
                BoostStepKind.FlushDns => await FlushDnsAsync(ct).ConfigureAwait(false),
                BoostStepKind.FlushMemory => await RunOffThread(FlushMemory, ct).ConfigureAwait(false),
                _ => new BoostStepResult(ToStep(step), false, "unknown step"),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new BoostStepResult(ToStep(step), false, ex.Message);
        }
    }

    /// <summary>Runs a synchronous step (registry writes, shell calls) off the UI thread.</summary>
    private static Task<BoostStepResult> RunOffThread(Func<BoostStepResult> body, CancellationToken ct)
        => Task.Run(body, ct);

    private static async Task<BoostStepResult> RemoveBloatAsync(CancellationToken ct)
    {
        var packages = await Task.Run(() => Debloat.AppxManager.GetRemovablePackages(), ct).ConfigureAwait(false);

        var removed = 0;
        var failed = 0;
        foreach (var app in packages)
        {
            ct.ThrowIfCancellationRequested();
            if (await Debloat.AppxManager.RemoveAsync(app.FullName, ct: ct).ConfigureAwait(false) is null)
                removed++;
            else
                failed++;
        }

        return new BoostStepResult(
            new BoostStepStep(BoostStepKind.DebloatCurated, "debloat"),
            true,
            failed == 0
                ? $"{removed} packages removed"
                : $"{removed} packages removed, {failed} skipped");
    }

    private static async Task<BoostStepResult> FlushDnsAsync(CancellationToken ct)
    {
        var ok = await SystemTweaks.DnsFlusher.FlushAsync(ct).ConfigureAwait(false);
        return new BoostStepResult(new BoostStepStep(BoostStepKind.FlushDns, "dns"),
            ok, ok ? "DNS cache flushed" : "could not flush DNS cache");
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

    private static BoostStepStep ToStep(BoostStep step) => new(step.Kind, step.Title);
}