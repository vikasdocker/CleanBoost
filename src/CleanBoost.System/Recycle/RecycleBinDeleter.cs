using System.Runtime.InteropServices;
using CleanBoost.Core.Deletion;
using CleanBoost.System.Interop;

namespace CleanBoost.System.Recycle;

/// <summary>
/// Sends files to the Recycle Bin instead of deleting permanently, so a
/// mistake is always reversible.
///
/// Batches paths into a single <c>SHFileOperationW</c> call, because one shell
/// round trip per file is what used to make large cleans look like a hang.
/// The shell reports a single status for a chunk, so each path is verified
/// individually afterwards to classify it as deleted or skipped.
/// </summary>
public sealed class RecycleBinDeleter : IBatchDeleter
{
    /// <summary>Paths per shell call. Larger chunks cut call overhead further but grow the tail delay.</summary>
    public const int PathsPerCall = 64;

    public string? Delete(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
            return "Recycle Bin is only available on Windows";

        try
        {
            if (!File.Exists(fullPath))
                return "not present";

            var reasons = Shell(new[] { fullPath });
            return reasons.Count > 0 ? reasons[0] : null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public IReadOnlyList<string?> DeleteBatch(IReadOnlyList<string> fullPaths)
    {
        if (!OperatingSystem.IsWindows())
            return Enumerable.Repeat<string?>("Recycle Bin is only available on Windows", fullPaths.Count).ToArray();

        var reasons = new List<string?>(fullPaths.Count);
        var chunk = new List<string>(PathsPerCall);

        foreach (var path in fullPaths)
        {
            chunk.Add(path);
            if (chunk.Count < PathsPerCall)
                continue;

            Fill(reasons, chunk);
            chunk.Clear();
        }

        if (chunk.Count > 0)
            Fill(reasons, chunk);

        return reasons;
    }

    private static void Fill(List<string?> reasons, List<string> chunk)
    {
        var sent = Shell(chunk);
        for (var i = 0; i < chunk.Count; i++)
        {
            var reason = sent.Count > i ? sent[i] : null;
            if (reason is null && File.Exists(chunk[i]))
            {
                // The shell reports one status per chunk, so a file that survives a
                // "successful" batch needs a second, individual attempt to find out
                // why. Retry just the stragglers — they are a tiny fraction of a run,
                // and it turns a vague "failed" into an accurate locked/in-use reason.
                reason = RetryIndividually(chunk[i]);
            }
            reasons.Add(reason);
        }
    }

    private static string? RetryIndividually(string path)
    {
        var single = Shell(new[] { path });
        if (single.Count > 0 && single[0] is not null)
            return single[0];

        // Still there after a successful individual recycle: it is in use.
        return File.Exists(path) ? "locked or in use" : null;
    }

    /// <summary>Recycles the chunk in one shell call, returning a per-path reason list.</summary>
    private static List<string?> Shell(IReadOnlyList<string> paths)
    {
        var results = new List<string?>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
            results.Add(null);

        // Missing files need no shell call at all.
        var present = new List<string>(paths.Count);
        var presentIndexes = new List<int>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            if (File.Exists(paths[i]))
            {
                present.Add(paths[i]);
                presentIndexes.Add(i);
            }
            else
            {
                results[i] = "not present";
            }
        }

        if (present.Count == 0)
            return results;

        try
        {
            // pFrom must be a double-null-terminated list of paths.
            var list = string.Join("\0", present) + "\0\0";

            NativeMethods.SHFileOperationStructW op = default;
            op.hwnd = IntPtr.Zero;
            op.wFunc = NativeMethods.FO_DELETE;
            op.pFrom = list;
            op.pTo = null;
            op.fFlags = NativeMethods.FOF_ALLOWUNDO
                        | NativeMethods.FOF_NOCONFIRMATION
                        | NativeMethods.FOF_NOERRORUI
                        | NativeMethods.FOF_SILENT;
            op.lpszProgressTitle = null;

            var code = NativeMethods.SHFileOperationW(ref op);
            if (code != 0)
            {
                var reason = Describe(code);
                for (var i = 0; i < presentIndexes.Count; i++)
                    results[presentIndexes[i]] = reason;
            }
        }
        catch (Exception ex)
        {
            for (var i = 0; i < presentIndexes.Count; i++)
                results[presentIndexes[i]] = ex.Message;
        }

        return results;
    }

    /// <summary>Turns a shell status code into something a user can act on.</summary>
    private static string Describe(int code) => code switch
    {
        // ERROR_INVALID_LEVEL — the shell refuses Explorer's thumbcache/iconcache
        // databases outright; they are never recyclable while Explorer is running.
        124 => "locked by Windows Explorer — thumbnail/icon caches cannot be recycled while Explorer runs",

        // ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION
        32 or 33 => "locked or in use",

        5 => "access denied",
        3 => "path not found",
        112 => "disk full — the Recycle Bin has no room left",
        _ => $"recycle failed (win32 {code})",
    };
}