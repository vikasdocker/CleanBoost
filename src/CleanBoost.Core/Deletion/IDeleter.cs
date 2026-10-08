namespace CleanBoost.Core.Deletion;

/// <summary>Outcome of deleting a single item.</summary>
public enum DeleteOutcome
{
    Deleted,
    SkippedNotPresent,
    SkippedLocked,
    SkippedProtected,
    SkippedError,
}

public sealed record DeleteAttempt(Scan.ScanItem Item, DeleteOutcome Outcome, string? Error = null);

/// <summary>Pluggable file deletion backend. Windows ships a Recycle Bin implementation.</summary>
public interface IDeleter
{
    /// <summary>Deletes the file at <paramref name="fullPath"/>. Returns null on success or a reason on failure.</summary>
    string? Delete(string fullPath);
}

/// <summary>
/// Optional capability for backends that can remove many files in one backend call.
/// The Windows Recycle Bin uses this to amortise <c>SHFileOperationW</c>, which is
/// the difference between a clean that feels instant and one that looks hung.
/// Callers must still validate every path individually before batching.
/// </summary>
public interface IBatchDeleter : IDeleter
{
    /// <summary>
    /// Deletes every path in <paramref name="fullPaths"/>. The returned list is
    /// index-aligned with the input: null means deleted, otherwise a failure reason.
    /// </summary>
    IReadOnlyList<string?> DeleteBatch(IReadOnlyList<string> fullPaths);
}

/// <summary>Permanent deletion used on non-Windows hosts (tests) or explicit "permanent" mode.</summary>
public sealed class PermanentFileDeleter : IDeleter
{
    public string? Delete(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
                return "not present";
            File.Delete(fullPath);
            return null;
        }
        catch (IOException ex)
        {
            return $"locked: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"access denied: {ex.Message}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}