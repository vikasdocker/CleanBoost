using CleanBoost.Core.Audit;
using Xunit;

namespace CleanBoost.Core.Tests;

/// <summary>
/// The audit log is append-only and grows by one line per deleted file, with no
/// upper bound. History used to deserialise every line on the UI thread before
/// showing the last few hundred — these tests pin the bounded tail read.
/// </summary>
public class AuditLogTests : IDisposable
{
    private readonly string _root;
    private readonly AuditLog _log;

    public AuditLogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cleanboost-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _log = new AuditLog(Path.Combine(_root, "audit.log"));
    }

    [Fact]
    public void ReadTail_returns_newest_entries_first()
    {
        for (var i = 0; i < 50; i++)
            Append($"file-{i:00}.tmp", i);

        var tail = _log.ReadTail(10);

        Assert.Equal(10, tail.Count);
        Assert.Equal("file-49.tmp", tail[0].Path);
        Assert.Equal("file-40.tmp", tail[9].Path);
    }

    [Fact]
    public void ReadTail_handles_more_records_than_requested()
    {
        Append("only.tmp", 1);

        var tail = _log.ReadTail(500);

        Assert.Equal("only.tmp", Assert.Single(tail).Path);
    }

    [Fact]
    public void ReadTail_on_a_missing_log_is_empty_not_a_crash()
    {
        var missing = new AuditLog(Path.Combine(_root, "nope.log"));
        Assert.Empty(missing.ReadTail(10));
    }

    [Fact]
    public void ReadTail_matches_ReadAll_ordering_for_a_small_log()
    {
        for (var i = 0; i < 20; i++)
            Append($"same.tmp", i);

        var all = _log.ReadAll();
        var tail = _log.ReadTail(5);

        Assert.Equal(all.Count, tail.Count + 15);
        Assert.Equal(all[^1].Path, tail[0].Path);
        Assert.Equal(all[^5].Path, tail[4].Path);
    }

    [Fact]
    public void ReadTail_skips_corrupt_lines()
    {
        Append("good-1.tmp", 1);
        File.AppendAllText(_log.FilePath, "{ this is not json\n");
        Append("good-2.tmp", 2);

        var tail = _log.ReadTail(10);

        Assert.Equal(2, tail.Count);
        Assert.Equal("good-2.tmp", tail[0].Path);
    }

    [Fact]
    public void ReadTail_stays_correct_when_the_window_starts_mid_line()
    {
        for (var i = 0; i < 200; i++)
            Append($"windowed-{i:000}.tmp", i);

        // Force a window that lands in the middle of the file so the first line in
        // the buffer is a partial record that must not be parsed.
        var tail = _log.ReadTail(5, maxBytes: 512);

        Assert.NotEmpty(tail);
        Assert.Equal("windowed-199.tmp", tail[0].Path);
        Assert.All(tail, r => Assert.False(string.IsNullOrWhiteSpace(r.Path)));
    }

    private void Append(string path, long size) => _log.Append(new AuditRecord
    {
        Timestamp = DateTimeOffset.Now,
        Path = path,
        Category = "safe",
        Outcome = "Deleted",
        Size = size,
    });

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best effort */ }
    }
}