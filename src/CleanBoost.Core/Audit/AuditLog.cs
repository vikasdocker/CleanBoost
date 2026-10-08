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
    /// <summary>Rotate once the log passes this size so reads stay cheap.</summary>
    public const long MaxBytes = 24L * 1024 * 1024;

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
                RotateIfOversized();
                // The directory is created once here rather than on every single
                // append — this runs once per deleted file.
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // audit failures must never break cleanup
        }
    }

    /// <summary>Keeps only the newest <see cref="MaxBytes"/>, moving the tail aside.</summary>
    private void RotateIfOversized()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaxBytes)
            return;

        try
        {
            var previous = _path + ".1";
            if (File.Exists(previous))
                File.Delete(previous);
            File.Move(_path, previous);
        }
        catch (Exception)
        {
            // rotation is best-effort; an oversized log is not a reason to stop cleaning
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

    /// <summary>
    /// Reads at most <paramref name="maxRecords"/> of the newest entries, newest first.
    ///
    /// <see cref="ReadAll"/> has to deserialise every line in the file before a caller
    /// can take the last few — with one record per deleted file that is unbounded work,
    /// and the History tab used to run it on the UI thread. This seeks to the end of
    /// the file and only parses the tail, so the cost is bounded no matter how large
    /// the log has grown.
    /// </summary>
    public IReadOnlyList<AuditRecord> ReadTail(int maxRecords = 500, int maxBytes = 1 << 20)
    {
        var records = new List<AuditRecord>();
        if (maxRecords <= 0 || !File.Exists(_path))
            return records;

        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = stream.Length;
            if (length == 0)
                return records;

            // Read the last maxBytes, then keep only whole lines.
            var take = (int)Math.Min(length, maxBytes);
            stream.Seek(length - take, SeekOrigin.Begin);

            var buffer = new byte[take];
            var read = 0;
            while (read < take)
            {
                var n = stream.Read(buffer, read, take - read);
                if (n <= 0)
                    break;
                read += n;
            }

            var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            var lines = text.Split('\n');

            // A seek into the middle of the file leaves a partial first line.
            var start = 0;
            if (take < length || (lines.Length > 0 && !text.EndsWith('\n') && read == take))
                start = 1;

            for (var i = lines.Length - 1; i >= start && records.Count < maxRecords; i--)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Length == 0)
                    continue;
                try
                {
                    var record = System.Text.Json.JsonSerializer.Deserialize<AuditRecord>(line, _serializerOptions);
                    if (record is not null)
                        records.Add(record);
                }
                catch (Exception) { /* skip corrupt line */ }
            }
        }
        catch (Exception)
        {
            // A log we cannot read is an empty log, never a crash.
        }

        return records;
    }
}