using CleanBoost.Core.Audit;

namespace CleanBoost.Core.Deletion;

/// <summary>
/// The single funnel every deletion must pass through. Applies the path guard,
/// skips locked/in-use files, records every attempt and never follows links.
///
/// A run happens in two stages so that progress is exact and cheap:
///
///  1. <b>Validate</b> — every item is checked individually (path guard, still
///     exists, not a symlink). Refused items are decided here and reported
///     immediately. No safety decision is ever made on a batch.
///  2. <b>Dispatch</b> — only the paths that survived validation are handed to
///     the backend, in chunks, when the backend supports batching. The Windows
///     Recycle Bin backend does one shell call per chunk instead of one per
///     file, which is what makes a large clean finish quickly.
/// </summary>
public sealed class SafeDeleter
{
    /// <summary>How many paths go to an <see cref="IBatchDeleter"/> in one backend call.</summary>
    public const int BatchSize = 64;

    private readonly Safety.PathGuard _guard;
    private readonly AuditLog _audit;

    public SafeDeleter(Safety.PathGuard? guard = null, AuditLog? audit = null)
    {
        _guard = guard ?? new Safety.PathGuard();
        _audit = audit ?? new AuditLog();
    }

    public AuditLog Audit => _audit;

    /// <summary>
    /// Deletes the given items, returning per-item outcomes in input order.
    /// </summary>
    /// <param name="items">Items discovered by the scan engine.</param>
    /// <param name="deleter">Backend to remove files with.</param>
    /// <param name="pruneEmptyRoots">
    /// Directories that pruning may ascend <i>up to and including</i>. Empty or null
    /// disables pruning entirely — pruning never walks above a target it was not given.
    /// </param>
    /// <param name="cancellationToken">Cancels between items and between batches.</param>
    /// <param name="progress">Receives one callback per item, in order.</param>
    public IReadOnlyList<DeleteAttempt> Delete(IEnumerable<Scan.ScanItem> items,
                                               IDeleter deleter,
                                               IReadOnlySet<string>? pruneEmptyRoots = null,
                                               CancellationToken cancellationToken = default,
                                               IProgress<DeleteProgress>? progress = null)
    {
        var queue = items as IReadOnlyList<Scan.ScanItem> ?? items.ToArray();
        var total = queue.Count;
        var results = new DeleteAttempt?[total];
        var pending = new List<int>(total);

        // ── Stage 1: per-item validation. No safety decision is batched. ──
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = queue[i];
            var refusal = Validate(item);
            if (refusal is null)
                pending.Add(i);
            else
                RecordDecided(i, refusal, results, progress);
        }

        // ── Stage 2: dispatch the survivors to the backend. ──
        if (pending.Count > 0)
        {
            if (deleter is IBatchDeleter batch)
                DispatchBatched(queue, pending, batch, cancellationToken, results, progress);
            else
                DispatchIndividually(queue, pending, deleter, cancellationToken, results, progress);
        }

        var attempts = new DeleteAttempt[total];
        for (var i = 0; i < total; i++)
            attempts[i] = results[i] ?? throw new InvalidOperationException($"item {i} was never resolved");

        // ── Prune last, sequentially, so parallel workers cannot race on the same folder. ──
        if (pruneEmptyRoots is { Count: > 0 })
            foreach (var attempt in attempts)
                if (attempt.Outcome == DeleteOutcome.Deleted)
                    PruneEmptyDirectories(attempt.Item.Path, pruneEmptyRoots);

        return attempts;
    }

    /// <summary>Returns a decided attempt, or null when the item is safe to delete.</summary>
    private DeleteAttempt? Validate(Scan.ScanItem item)
    {
        var evaluation = _guard.Evaluate(item.Path);
        if (!evaluation.IsAllowed)
            return Finish(item, DeleteOutcome.SkippedProtected, "protected path");

        if (!File.Exists(item.Path))
            return Finish(item, DeleteOutcome.SkippedNotPresent, null);

        try
        {
            if (Safety.PathGuard.IsSymbolicLink(new FileInfo(item.Path)))
                return Finish(item, DeleteOutcome.SkippedProtected, "symbolic link");
        }
        catch (Exception)
        {
            return Finish(item, DeleteOutcome.SkippedError, "cannot inspect file");
        }

        return null;
    }

    private void DispatchBatched(IReadOnlyList<Scan.ScanItem> queue,
                                 List<int> pending,
                                 IBatchDeleter deleter,
                                 CancellationToken cancellationToken,
                                 DeleteAttempt?[] results,
                                 IProgress<DeleteProgress>? progress)
    {
        var paths = new List<string>(pending.Count);
        for (var offset = 0; offset < pending.Count; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(BatchSize, pending.Count - offset);
            paths.Clear();
            for (var n = 0; n < count; n++)
                paths.Add(queue[pending[offset + n]].Path);

            var reasons = deleter.DeleteBatch(paths);

            for (var n = 0; n < count; n++)
            {
                var index = pending[offset + n];
                var item = queue[index];
                var reason = n < reasons.Count ? reasons[n] : "batch backend returned no result";
                Record(index, item, reason, results, progress);
            }
        }
    }

    private void DispatchIndividually(IReadOnlyList<Scan.ScanItem> queue,
                                      List<int> pending,
                                      IDeleter deleter,
                                      CancellationToken cancellationToken,
                                      DeleteAttempt?[] results,
                                      IProgress<DeleteProgress>? progress)
    {
        foreach (var index in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = queue[index];
            string? reason;
            try
            {
                reason = deleter.Delete(item.Path);
            }
            catch (Exception ex)
            {
                reason = ex.Message;
            }
            Record(index, item, reason, results, progress);
        }
    }

    /// <summary>
    /// Maps a backend reason onto an outcome. A batch call reports one code for the
    /// whole chunk, so the still-exists check is what separates "gone" from "skipped".
    /// </summary>
    private static DeleteOutcome Classify(string path, string? reason)
    {
        if (reason is not null)
            return reason.Contains("locked", StringComparison.OrdinalIgnoreCase)
                ? DeleteOutcome.SkippedLocked
                : DeleteOutcome.SkippedError;

        if (File.Exists(path))
            return DeleteOutcome.SkippedError;

        return DeleteOutcome.Deleted;
    }

    private DeleteAttempt Finish(Scan.ScanItem item, DeleteOutcome outcome, string? error)
    {
        var attempt = new DeleteAttempt(item, outcome, error);
        _audit.Append(item, attempt);
        return attempt;
    }

    /// <summary>Classifies a backend reason, records the attempt and reports progress.</summary>
    private void Record(int index,
                        Scan.ScanItem item,
                        string? reason,
                        DeleteAttempt?[] results,
                        IProgress<DeleteProgress>? progress)
    {
        var outcome = Classify(item.Path, reason);

        // Keep the reason on the attempt: it is the only thing that makes a
        // "SkippedError" diagnosable in the audit log.
        string? error = null;
        if (outcome != DeleteOutcome.Deleted && outcome != DeleteOutcome.SkippedNotPresent)
            error = reason ?? "in use or could not be removed";

        var attempt = new DeleteAttempt(item, outcome, error);
        _audit.Append(item, attempt);
        results[index] = attempt;
        progress?.Report(new DeleteProgress(
            index + 1,
            results.Length,
            attempt.Item.Path,
            attempt.Item.CategoryTitle,
            attempt.Outcome,
            attempt.Item.Size));
    }

    /// <summary>Records an attempt that was already decided and audited during validation.</summary>
    private void RecordDecided(int index,
                               DeleteAttempt attempt,
                               DeleteAttempt?[] results,
                               IProgress<DeleteProgress>? progress)
    {
        results[index] = attempt;
        progress?.Report(new DeleteProgress(
            index + 1,
            results.Length,
            attempt.Item.Path,
            attempt.Item.CategoryTitle,
            attempt.Outcome,
            attempt.Item.Size));
    }

    /// <summary>
    /// Removes empty directories ascending from the file's folder, but never above a
    /// supplied prune root and never at all when none were supplied.
    /// </summary>
    private static void PruneEmptyDirectories(string filePath, IReadOnlySet<string> pruneRoots)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(dir) && IsWithinPruneRoot(dir, pruneRoots))
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

    /// <summary>True when <paramref name="dir"/> is a prune root itself or lives under one.</summary>
    private static bool IsWithinPruneRoot(string dir, IReadOnlySet<string> pruneRoots)
    {
        foreach (var root in pruneRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            if (dir.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;

            var prefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (dir.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;

            // Compare with separators normalised so a root captured as "C:/Temp"
            // still matches an item reported as "C:\Temp".
            var alt = prefix.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (alt != prefix && dir.StartsWith(alt, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}