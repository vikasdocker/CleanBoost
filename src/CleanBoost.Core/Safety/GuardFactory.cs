using CleanBoost.Core.Safety;

namespace CleanBoost.Core.Safety;

/// <summary>
/// Builds the default <see cref="PathGuard"/> including the well-known safe
/// carve-outs that legitimately live inside protected roots (Windows\Temp,
/// SoftwareDistribution download leftovers, WER reports, …).
/// </summary>
public static class GuardFactory
{
    public static PathGuard CreateDefault()
    {
        var guard = new PathGuard();
        ApplySafeCarveOuts(guard);
        return guard;
    }

    public static void ApplySafeCarveOuts(PathGuard guard)
    {
        var resolver = new Scan.PathResolver();

        void Allow(params string[] raw)
        {
            foreach (var r in raw)
                guard.AddAllowance(resolver.Expand(r));
        }

        Allow("%WinDir%\\Temp");
        Allow("%WinDir%\\SoftwareDistribution\\Download");
        Allow("%WinDir%\\SoftwareDistribution\\DeliveryOptimization");
        Allow("%WinDir%\\SoftwareDistribution\\DataStore");
        Allow("%WinDir%\\Prefetch");
        Allow("%WinDir%\\Minidump");
        Allow("%WinDir%\\MEMORY.DMP");
        Allow("%ProgramData%\\Microsoft\\Windows\\WER");
    }
}