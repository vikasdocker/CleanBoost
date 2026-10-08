using System.Collections.Concurrent;
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

    // ───────────────────────────── progress reporting ─────────────────────────────

    [Fact]
    public void Reports_progress_for_every_item_with_a_monotonic_index()
    {
        const int count = 25;
        var report = new ScanReport();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(_root, $"p{i:00}.tmp");
            File.WriteAllText(file, "x");
            report.Add(Item(file, "safe"));
        }

        var progress = new CollectingProgress<DeleteProgress>();
        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit5.log")));

        deleter.Delete(report.Items, new PermanentFileDeleter(), progress: progress);

        Assert.Equal(count, progress.Items.Count);
        Assert.All(progress.Items, p => Assert.Equal(count, p.Total));
        Assert.Equal(Enumerable.Range(1, count), progress.Items.Select(p => p.Processed));
        Assert.All(progress.Items, p => Assert.Equal(DeleteOutcome.Deleted, p.Outcome));
        Assert.Equal(count, progress.Items.Select(p => p.Path).Distinct().Count());
    }

    [Fact]
    public void Progress_still_reports_items_refused_during_validation()
    {
        var allowed = Path.Combine(_root, "allowed.tmp");
        var refused = Path.Combine(_root, "refused.tmp");
        File.WriteAllText(allowed, "x");
        File.WriteAllText(refused, "x");

        var report = new ScanReport();
        report.Add(Item(allowed, "safe"));
        report.Add(Item(refused, "rogue"));

        var guard = new PathGuard(addBuiltInRoots: false, extraProtectedRoots: new[] { refused });
        var progress = new CollectingProgress<DeleteProgress>();
        var deleter = new SafeDeleter(guard, new AuditLog(Path.Combine(_root, "audit6.log")));

        deleter.Delete(report.Items, new PermanentFileDeleter(), progress: progress);

        Assert.Equal(2, progress.Items.Count);
        Assert.Contains(progress.Items, p => p.Outcome == DeleteOutcome.SkippedProtected);
        Assert.Contains(progress.Items, p => p.Outcome == DeleteOutcome.Deleted);
        Assert.True(File.Exists(refused));
    }

    [Fact]
    public void Honours_a_token_that_is_already_cancelled()
    {
        var file = Path.Combine(_root, "cancelled.tmp");
        File.WriteAllText(file, "x");
        var report = new ScanReport();
        report.Add(Item(file, "safe"));

        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit7.log")));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => deleter.Delete(report.Items, new PermanentFileDeleter(), cancellationToken: cts.Token));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Stops_partway_when_cancelled_mid_run()
    {
        const int count = 12;
        var report = new ScanReport();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(_root, $"c{i:00}.tmp");
            File.WriteAllText(file, "x");
            report.Add(Item(file, "safe"));
        }

        using var cts = new CancellationTokenSource();
        var canceller = new CancellingDeleter(cts, after: 3);
        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit8.log")));

        Assert.Throws<OperationCanceledException>(
            () => deleter.Delete(report.Items, canceller, cancellationToken: cts.Token));

        Assert.True(canceller.Calls < count, "the run must stop early, not delete everything");
    }

    // ───────────────────────────────── batch backends ─────────────────────────────

    [Fact]
    public void Uses_the_batch_path_and_keeps_per_item_outcomes()
    {
        const int count = 200; // spans four 64-path batches
        var report = new ScanReport();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(_root, $"b{i:000}.tmp");
            File.WriteAllText(file, "x");
            report.Add(Item(file, "safe"));
        }

        var batch = new RecordingBatchDeleter();
        var progress = new CollectingProgress<DeleteProgress>();
        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit9.log")));

        var attempts = deleter.Delete(report.Items, batch, progress: progress);

        Assert.Equal(count, attempts.Count);
        Assert.All(attempts, a => Assert.Equal(DeleteOutcome.Deleted, a.Outcome));
        Assert.Equal(count, progress.Items.Count);

        // Four batches for 200 files, and every target file was actually removed.
        Assert.Equal(4, batch.Calls);
        Assert.Equal(count, batch.PathsSeen.Count);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public void Batch_failures_are_attributed_to_the_right_file()
    {
        var ok = Path.Combine(_root, "ok.tmp");
        var bad = Path.Combine(_root, "bad.tmp");
        File.WriteAllText(ok, "x");
        File.WriteAllText(bad, "x");

        var report = new ScanReport();
        report.Add(Item(ok, "safe"));
        report.Add(Item(bad, "safe"));

        var batch = new RecordingBatchDeleter { FailPath = bad };
        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit10.log")));

        var attempts = deleter.Delete(report.Items, batch);

        Assert.Equal(DeleteOutcome.Deleted, attempts[0].Outcome);
        Assert.Equal(DeleteOutcome.SkippedError, attempts[1].Outcome);
        Assert.Equal("simulated failure", attempts[1].Error);
        Assert.True(File.Exists(bad));
        Assert.False(File.Exists(ok));
    }

    [Fact]
    public void A_file_that_survives_a_successful_call_is_recorded_as_in_use()
    {
        var stubborn = Path.Combine(_root, "stubborn.tmp");
        File.WriteAllText(stubborn, "x");

        var report = new ScanReport();
        report.Add(Item(stubborn, "safe"));

        var batch = new RecordingBatchDeleter { KeepPath = stubborn };
        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit14.log")));

        var attempt = Assert.Single(deleter.Delete(report.Items, batch));

        Assert.Equal(DeleteOutcome.SkippedError, attempt.Outcome);
        Assert.Equal("in use or could not be removed", attempt.Error);
        Assert.True(File.Exists(stubborn));
    }

    // ──────────────────────────────── prune bounding ─────────────────────────────

    [Fact]
    public void Pruning_is_bounded_to_the_supplied_roots()
    {
        var baseDir = Path.Combine(_root, "prune");
        var target = Path.Combine(baseDir, "target");
        var junk = Path.Combine(target, "junk");
        Directory.CreateDirectory(junk);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep me");
        var file = Path.Combine(junk, "one.tmp");
        File.WriteAllText(file, "x");

        var report = new ScanReport();
        report.Add(Item(file, "safe"));

        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit11.log")));
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { target };

        deleter.Delete(report.Items, new PermanentFileDeleter(), roots);

        Assert.False(Directory.Exists(junk), "the emptied folder under the target is pruned");
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")), "the target itself still holds a file");
        Assert.True(Directory.Exists(baseDir), "nothing above the prune root may be touched");
    }

    [Fact]
    public void No_prune_roots_means_no_pruning_at_all()
    {
        var junk = Path.Combine(_root, "untouched", "junk");
        Directory.CreateDirectory(junk);
        var file = Path.Combine(junk, "one.tmp");
        File.WriteAllText(file, "x");

        var report = new ScanReport();
        report.Add(Item(file, "safe"));

        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit12.log")));

        deleter.Delete(report.Items, new PermanentFileDeleter());

        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(junk), "without prune roots the folder must survive");
    }

    [Fact]
    public void A_prune_root_that_does_not_cover_the_path_prunes_nothing()
    {
        var junk = Path.Combine(_root, "other", "junk");
        var unrelated = Path.Combine(_root, "unrelated");
        Directory.CreateDirectory(junk);
        Directory.CreateDirectory(unrelated);
        var file = Path.Combine(junk, "one.tmp");
        File.WriteAllText(file, "x");

        var report = new ScanReport();
        report.Add(Item(file, "safe"));

        var deleter = new SafeDeleter(new PathGuard(addBuiltInRoots: false),
                                      new AuditLog(Path.Combine(_root, "audit13.log")));
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { unrelated };

        deleter.Delete(report.Items, new PermanentFileDeleter(), roots);

        Assert.False(File.Exists(file));
        Assert.True(Directory.Exists(junk));
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

    /// <summary>Thread-safe sink so assertions can read reports after the run.</summary>
    private sealed class CollectingProgress<T> : IProgress<T>
    {
        private readonly ConcurrentQueue<T> _items = new();
        public IReadOnlyList<T> Items => _items.ToArray();
        public void Report(T value) => _items.Enqueue(value);
    }

    /// <summary>Per-file backend that cancels the run after a fixed number of calls.</summary>
    private sealed class CancellingDeleter : IDeleter
    {
        private readonly CancellationTokenSource _cts;
        private readonly int _after;
        private int _calls;

        public CancellingDeleter(CancellationTokenSource cts, int after)
        {
            _cts = cts;
            _after = after;
        }

        public int Calls => _calls;

        public string? Delete(string fullPath)
        {
            if (Interlocked.Increment(ref _calls) >= _after)
                _cts.Cancel();
            File.Delete(fullPath);
            return null;
        }
    }

    /// <summary>
    /// Batch backend that records how it was called. It honours <see cref="FailPath"/>
    /// so per-item attribution inside a chunk can be verified.
    /// </summary>
    private sealed class RecordingBatchDeleter : IBatchDeleter
    {
        private int _calls;

        public int Calls => _calls;
        public List<string> PathsSeen { get; } = new();
        public string? FailPath { get; init; }

        /// <summary>Reported deleted but never actually removed, to exercise the "still exists" path.</summary>
        public string? KeepPath { get; init; }

        public string? Delete(string fullPath) => DeleteBatch(new[] { fullPath })[0];

        public IReadOnlyList<string?> DeleteBatch(IReadOnlyList<string> fullPaths)
        {
            Interlocked.Increment(ref _calls);
            var reasons = new List<string?>(fullPaths.Count);
            foreach (var path in fullPaths)
            {
                PathsSeen.Add(path);
                if (path == FailPath)
                {
                    reasons.Add("simulated failure");
                    continue;
                }
                if (path != KeepPath)
                    File.Delete(path);
                reasons.Add(null);
            }
            return reasons;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }
}