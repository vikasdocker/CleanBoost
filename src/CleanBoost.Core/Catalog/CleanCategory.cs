using CleanBoost.Core.Safety;

namespace CleanBoost.Core.Catalog;

/// <summary>How a category is executed. Everything except <see cref="Files"/> is dispatched to the platform layer.</summary>
public enum CleanKind
{
    /// <summary>Normal file scanning + deletion.</summary>
    Files,

    /// <summary>Empties the Windows Recycle Bin (shell API). Marked ConfirmFirst.</summary>
    RecycleBin,

    /// <summary>Flushes a cache via a platform command (e.g. DNS, icon cache).</summary>
    SystemCommand,
}

/// <summary>
/// A single concrete cleanup location: a path (possibly with wildcard patterns)
/// plus flags controlling recursion, self removal and exclusions.
/// </summary>
public sealed record CleanTarget
{
    /// <summary>Expanded filesystem path root to scan. Environment variables are expanded by the engine.</summary>
    public required string Path { get; init; }

    /// <summary>Semicolon-separated file patterns, e.g. <c>*.log;*.tmp</c>. Empty/asterisk means match everything.</summary>
    public string Patterns { get; init; } = "";

    /// <summary>Recurse into subdirectories when matching files.</summary>
    public bool Recurse { get; init; }

    /// <summary>After deleting matching files, prune now-empty directories up to the target path (REMOVESELF semantics).</summary>
    public bool RemoveSelf { get; init; }

    /// <summary>Only delete files whose LastWriteTime is older than this age. Null = any age.</summary>
    public TimeSpan? OlderThan { get; init; }
}

/// <summary>
/// A named, user-visible group of cleanup targets with an overall risk rating.
/// </summary>
public sealed record CleanCategory
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required RiskLevel Risk { get; init; }

    /// <summary>Segoe Fluent Icon glyph, e.g. &amp;#xE74C;. Kept as raw codepoint string for binding.</summary>
    public string Icon { get; init; } = "\uE74C";

    /// <summary>How the category is executed (files or a platform action).</summary>
    public CleanKind Kind { get; init; } = CleanKind.Files;

    /// <summary>Arbitrary payload for <see cref="SystemCommand"/> kinds, e.g. recycle-bin, dns-flush.</summary>
    public string? SystemAction { get; init; }

    /// <summary>If set, this category is hidden unless the app is running elevated. Services/AppX cleanup etc.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>Targets that must all exist before any target is deleted (mirrors winapp2 DetectFile semantics lightly).</summary>
    public List<CleanTarget> Targets { get; init; } = new();
}