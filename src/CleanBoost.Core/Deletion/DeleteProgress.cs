namespace CleanBoost.Core.Deletion;

/// <summary>
/// Raised once per item while a delete batch runs. <see cref="Processed"/> is the
/// 1-based index of the item just finished, so a determinate progress bar only
/// needs Processed/Total to be exact.
/// </summary>
public sealed record DeleteProgress(
    int Processed,
    int Total,
    string Path,
    string CategoryTitle,
    DeleteOutcome Outcome,
    long SizeBytes);