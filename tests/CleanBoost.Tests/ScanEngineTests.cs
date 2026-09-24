using CleanBoost.Core.Catalog;
using CleanBoost.Core.Safety;
using CleanBoost.Core.Scan;
using Xunit;

namespace CleanBoost.Core.Tests;

/// <summary>
/// The scanner must find junk, respect patterns/age, skip protected areas,
/// and never follow junctions/symlinks (dry-run pass — nothing is deleted here).
/// </summary>
public class ScanEngineTests : IDisposable
{
    private readonly string _root;
    private readonly PathResolver _resolver;
    private readonly ScanEngine _engine;

    public ScanEngineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanboost-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "cache"));
        File.WriteAllText(Path.Combine(_root, "cache", "a.bin"), new string('x', 1024));
        File.WriteAllText(Path.Combine(_root, "cache", "b.bin"), new string('y', 512));
        File.WriteAllText(Path.Combine(_root, "cache", "keep.txt"), "keep me");

        _resolver = new PathResolver(new Dictionary<string, string>
        {
            ["TestRoot"] = _root,
        });

        var guard = new PathGuard(addBuiltInRoots: false);
        _engine = new ScanEngine(_resolver, guard);
    }

    [Fact]
    public void Finds_files_in_target_path()
    {
        var category = Category("cache", "*.bin");
        var report = _engine.Scan(new[] { category });

        Assert.Equal(2, report.TotalItems);
        Assert.Equal(1536, report.TotalSize);
        Assert.DoesNotContain(report.Items, i => i.Path.EndsWith("keep.txt"));
    }

    [Fact]
    public void Groups_by_category()
    {
        var report = _engine.Scan(new[]
        {
            Category("cache", "*.bin"),
            Category("cache2", "*.txt", recurse: true),
        });

        Assert.Equal(2, report.ByCategory.Count);
        Assert.Equal(2, report.ByCategory["cache"].Count);
        Assert.Equal(1, report.ByCategory["cache2"].Count);
    }

    [Fact]
    public void Skips_missing_paths_silently()
    {
        var report = _engine.Scan(new[] { CategoryAtPath("%TestRoot%/does-not-exist", "*.bin") });
        Assert.Equal(0, report.TotalItems);
    }

    [Fact]
    public void Refuses_targets_that_resolve_into_protected_areas()
    {
        var dirt = Path.Combine(_root, "system");
        Directory.CreateDirectory(dirt);
        File.WriteAllText(Path.Combine(dirt, "important.dll"), "do not touch");

        var guard = new PathGuard(addBuiltInRoots: false, extraProtectedRoots: new[] { dirt });
        var engine = new ScanEngine(_resolver, guard);
        var category = CategoryFromPath(dirt, "*");

        var report = engine.Scan(new[] { category });

        Assert.Equal(0, report.TotalItems);
        Assert.True(File.Exists(Path.Combine(dirt, "important.dll")));
    }

    [Fact]
    public void Does_not_follow_symlink_files()
    {
        if (OperatingSystem.IsWindows())
            return; // creating symlinks needs elevation on Windows

        var linked = Path.Combine(_root, "cache", "outside-secret.bin");
        File.WriteAllText(linked, "secret");
        var link = Path.Combine(_root, "cache", "link.bin");
        File.CreateSymbolicLink(link, linked);

        var category = Category("cache", "*.bin");
        var report = _engine.Scan(new[] { category });

        Assert.DoesNotContain(report.Items, i => i.Path == link);
        Assert.Contains(report.Items, i => i.Path == linked);
    }

    private static CleanCategory CategoryFromPath(string path, string patterns) => new()
    {
        Key = "t",
        Title = "T",
        Description = "D",
        Risk = RiskLevel.Safe,
        Targets = { new CleanTarget { Path = path, Patterns = patterns } },
    };

    private CleanCategory Category(string key, string patterns, bool recurse = false) => new()
    {
        Key = key,
        Title = key,
        Description = "D",
        Risk = RiskLevel.Safe,
        Targets = { new CleanTarget { Path = "%TestRoot%/cache", Patterns = patterns, Recurse = recurse, RemoveSelf = true } },
    };

    private CleanCategory CategoryAtPath(string key, string patterns) => new()
    {
        Key = key,
        Title = key,
        Description = "D",
        Risk = RiskLevel.Safe,
        Targets = { new CleanTarget { Path = key, Patterns = patterns } },
    };

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }
}