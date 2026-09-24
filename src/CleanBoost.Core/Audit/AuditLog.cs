using CleanBoost.Core.Deletion;

namespace CleanBoost.Core.Audit;

/// <summary>One append-only audit record describing a deletion.</summary>
public sealed record AuditRecord
{
    public DateTimeOffset Timestamp { get; init; }
    public required string Path { get; init; }
    public required string Category { get; init; }
    public required string Outcome { get; init; }
    public long Size { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Append-only JSON-lines audit trail of everything the cleaner removed.
/// The GUI exposes this to the user in the log viewer.
/// </summary>
public sealed class AuditLog
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly System.Text.Json.JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = false,
    };

    public AuditLog(string? path = null)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _path = path ?? System.IO.Path.Combine(baseDir, "CleanBoost", "audit.log");
    }

    public string FilePath => _path;

    public void Append(Scan.ScanItem item, DeleteAttempt attempt)
    {
        var record = new AuditRecord
        {
            Timestamp = DateTimeOffset.Now,
            Path = item.Path,
            Category = item.CategoryKey,
            Outcome = attempt.Outcome.ToString(),
            Size = item.Size,
            Error = attempt.Error,
        };
        Append(record);
    }

    public void Append(AuditRecord record)
    {
        var line = System.Text.Json.JsonSerializer.Serialize(record, _serializerOptions);
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // audit failures must never break cleanup
        }
    }

    public IReadOnlyList<AuditRecord> ReadAll()
    {
        var records = new List<AuditRecord>();
        if (!File.Exists(_path))
            return records;

        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                var record = System.Text.Json.JsonSerializer.Deserialize<AuditRecord>(line, _serializerOptions);
                if (record is not null)
                    records.Add(record);
            }
            catch (Exception) { /* skip corrupt line */ }
        }
        return records;
    }
}