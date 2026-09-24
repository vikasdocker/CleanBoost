using System.Collections.Concurrent;
using CleanBoost.Core.Audit;

namespace CleanBoost.Core.Deletion;

/// <summary>
/// The single funnel every deletion must pass through. Applies the path guard,
/// skips locked/in-use files, records every attempt and never follows links.
/// </summary>
public sealed class SafeDeleter
{
    private readonly Safety.PathGuard _guard;
    private readonly AuditLog _audit;

    public SafeDeleter(Safety.PathGuard? guard = null, AuditLog? audit = null)
    {
        _guard = guard ?? new Safety.PathGuard();
        _audit = audit ?? new AuditLog();
    }

    public AuditLog Audit => _audit;

    /// <summary>Deletes the given items, returning per-item outcomes.</summary>
    public IReadOnlyList<DeleteAttempt> Delete(IEnumerable<Scan.ScanItem> items,
                                               IDeleter deleter,
                                               IReadOnlySet<string>? pruneEmptyRoots = null,
                                               CancellationToken cancellationToken = default)
    {
        var attempts = new ConcurrentQueue<DeleteAttempt>();
        var pruneRoots = pruneEmptyRoots ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Parallel.ForEachAsync(items, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = cancellationToken,
        }, (item, token) =>
        {
            token.ThrowIfCancellationRequested();
            var result = DeleteOne(item, deleter);
            attempts.Enqueue(result);
            if (result.Outcome == DeleteOutcome.Deleted)
                PruneEmptyDirectories(item.Path, pruneRoots);
            return ValueTask.CompletedTask;
        }).GetAwaiter().GetResult();

        return attempts.ToArray();
    }

    private DeleteAttempt DeleteOne(Scan.ScanItem item, IDeleter deleter)
    {
        var evaluation = _guard.Evaluate(item.Path);
        if (!evaluation.IsAllowed)
        {
            var protectedAttempt = new DeleteAttempt(item, DeleteOutcome.SkippedProtected, "protected path");
            _audit.Append(item, protectedAttempt);
            return protectedAttempt;
        }

        if (!File.Exists(item.Path))
        {
            var missing = new DeleteAttempt(item, DeleteOutcome.SkippedNotPresent);
            _audit.Append(item, missing);
            return missing;
        }

        var info = new FileInfo(item.Path);
        if (Safety.PathGuard.IsSymbolicLink(info))
        {
            var link = new DeleteAttempt(item, DeleteOutcome.SkippedProtected, "symbolic link");
            _audit.Append(item, link);
            return link;
        }

        var error = deleter.Delete(item.Path);
        var outcome = error switch
        {
            null => DeleteOutcome.Deleted,
            var e when e.Contains("locked", StringComparison.OrdinalIgnoreCase) => DeleteOutcome.SkippedLocked,
            _ => DeleteOutcome.SkippedError,
        };
        var attempt = new DeleteAttempt(item, outcome, error);
        _audit.Append(item, attempt);
        return attempt;
    }

    /// <summary>Removes empty directories ascending from the file's folder, stopping at the prune root.</summary>
    private static void PruneEmptyDirectories(string filePath, IReadOnlySet<string> pruneRoots)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(dir) && !pruneRoots.Contains(dir))
            {
                if (!Directory.Exists(dir))
                    return;
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                    return;
                try
                {
                    Directory.Delete(dir, false);
                }
                catch (Exception)
                {
                    return; // in use or not empty — stop ascending
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch (Exception)
        {
            // pruning is best-effort and never fatal
        }
    }
}