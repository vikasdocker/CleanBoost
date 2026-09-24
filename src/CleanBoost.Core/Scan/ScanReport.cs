using CleanBoost.Core.Safety;

namespace CleanBoost.Core.Scan;

/// <summary>A single file (or empty directory) identified as cleanable.</summary>
public sealed record ScanItem
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required DateTime LastWriteTimeUtc { get; init; }
    public required string CategoryKey { get; init; }
    public required string CategoryTitle { get; init; }
    public required RiskLevel Risk { get; init; }
}

/// <summary>Aggregated results grouped by category.</summary>
public sealed class ScanReport
{
    public List<ScanItem> Items { get; } = new();
    public Dictionary<string, CategoryStat> ByCategory { get; } = new(StringComparer.Ordinal);

    public long TotalSize => Items.Sum(i => i.Size);
    public int TotalItems => Items.Count;

    public void Add(ScanItem item)
    {
        Items.Add(item);
        if (!ByCategory.TryGetValue(item.CategoryKey, out var stat))
        {
            stat = new CategoryStat(item.CategoryKey, item.CategoryTitle, item.Risk);
            ByCategory[item.CategoryKey] = stat;
        }
        stat.Count++;
        stat.Size += item.Size;
    }
}

public sealed class CategoryStat
{
    public CategoryStat(string key, string title, RiskLevel risk)
    {
        Key = key;
        Title = title;
        Risk = risk;
    }

    public string Key { get; }
    public string Title { get; }
    public RiskLevel Risk { get; }
    public long Size { get; set; }
    public int Count { get; set; }
}

/// <summary>Raised during scanning with the watched target path and current file count.</summary>
public sealed record ScanProgress(string Path, long FilesScanned, string CategoryTitle)
{
    public static readonly ScanProgress Done = new(string.Empty, 0, string.Empty);
}