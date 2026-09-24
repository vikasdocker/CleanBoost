namespace CleanBoost.Core.Safety;

/// <summary>
/// Safety rating applied to every cleanable category and to every scanned item.
/// Higher tiers require explicit user confirmation in the UI.
/// </summary>
public enum RiskLevel
{
    /// <summary>Regenerable throwaway data (temp, thumbnails, caches). Safe to delete.</summary>
    Safe = 0,

    /// <summary>Rebuildable but wasteful (shader caches, prefetch, memory dumps). Safe but may cause minor repopulation.</summary>
    Caution = 1,

    /// <summary>Potentially user-valuable (recycle bin, browser history when privacy mode is enabled, hibernation file).</summary>
    ConfirmFirst = 2,

    /// <summary>Protected system paths. Never deletable by the engine.</summary>
    Protected = 3,
}