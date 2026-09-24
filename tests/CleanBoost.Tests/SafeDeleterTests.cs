using CleanBoost.Core.Audit;
using CleanBoost.Core.Catalog;
using CleanBoost.Core.Deletion;
using CleanBoost.Core.Safety;
using CleanBoost.Core.Scan;
using Xunit;

namespace CleanBoost.Core.Tests;

/// <summary>
/// Every deletion must route through the SafeDeleter funnel: dry-run
/// semantics, protected-path refusal, lock skipping and audit recording.
/// </summary>
public class SafeDeleterTests : IDisposable
{
    private readonly string _root;

    public SafeDeleterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanboost-del-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Deletes_allowed_files_and_audits()
    {
        var file = Path.Combine(_root, "junk.tmp");
        File.WriteAllText(file, "x");

        var report = new ScanReport();
        report.Add(Item(file, "safe"));

        var audit = new AuditLog(Path.Combine(_root, "audit.log"));
        var guard = new PathGuard(addBuiltInRoots: false);
        var deleter = new SafeDeleter(guard, audit);

        var attempts = deleter.Delete(report.Items, new PermanentFileDeleter());

        Assert.False(File.Exists(file));
        var attempt = Assert.Single(attempts);
        Assert.Equal(DeleteOutcome.Deleted, attempt.Outcome);
        Assert.NotEmpty(audit.ReadAll());
    }

    [Fact]
    public void Refuses_protected_files_even_when_handed_a_scan_item()
    {
        var protectedFile = Path.Combine(_root, "protected.dll");
        File.WriteAllText(protectedFile, "y");

        var report = new ScanReport();
        report.Add(Item(protectedFile, "rogue"));

        var guard = new PathGuard(addBuiltInRoots: false, extraProtectedRoots: new[] { _root });
        var deleter = new SafeDeleter(guard, new AuditLog(Path.Combine(_root, "audit2.log")));

        var attempts = deleter.Delete(report.Items, new PermanentFileDeleter());

        Assert.True(File.Exists(protectedFile));
        Assert.Equal(DeleteOutcome.SkippedProtected, Assert.Single(attempts).Outcome);
    }

    [Fact]
    public void Does_not_follow_symlinks_when_deleting()
    {
        if (OperatingSystem.IsWindows())
            return;

        var target = Path.Combine(_root, "real-file.txt");
        File.WriteAllText(target, "valuable");
        var link = Path.Combine(_root, "link.txt");
        File.CreateSymbolicLink(link, target);

        var report = new ScanReport();
        report.Add(Item(link, "trap"));

        var guard = new PathGuard(addBuiltInRoots: false);
        var deleter = new SafeDeleter(guard, new AuditLog(Path.Combine(_root, "audit3.log")));

        var attempts = deleter.Delete(report.Items, new PermanentFileDeleter());

        Assert.Equal(DeleteOutcome.SkippedProtected, Assert.Single(attempts).Outcome);
        Assert.True(File.Exists(target), "the real file behind the symlink must survive");
    }

    [Fact]
    public void Missing_files_are_recorded_as_skipped()
    {
        var report = new ScanReport();
        report.Add(Item(Path.Combine(_root, "gone.txt"), "safe"));

        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit4.log")));

        var attempts = deleter.Delete(report.Items, new PermanentFileDeleter());

        Assert.Equal(DeleteOutcome.SkippedNotPresent, Assert.Single(attempts).Outcome);
    }

    private static ScanItem Item(string path, string category) => new()
    {
        Path = path,
        Size = 1,
        LastWriteTimeUtc = DateTime.UtcNow,
        CategoryKey = category,
        CategoryTitle = category,
        Risk = RiskLevel.Safe,
    };

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }
}