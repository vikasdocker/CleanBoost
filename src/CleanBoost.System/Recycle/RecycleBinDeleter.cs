using System.Runtime.InteropServices;
using CleanBoost.Core.Deletion;
using CleanBoost.System.Interop;

namespace CleanBoost.System.Recycle;

/// <summary>
/// Sends files to the Recycle Bin instead of deleting permanently, so a
/// mistake is always reversible. Falls back to a permanent delete only when
/// the file is too big for the bin or the bin is unavailable.
/// </summary>
public sealed class RecycleBinDeleter : IDeleter
{
    public string? Delete(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
            return "Recycle Bin is only available on Windows";

        try
        {
            if (!File.Exists(fullPath))
                return "not present";

            // pFrom must be a double-null-terminated list of paths.
            var list = fullPath + "\0\0";

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

            var result = NativeMethods.SHFileOperationW(ref op);
            if (result != 0)
                return $"recycle failed (win32 {result})";

            return File.Exists(fullPath) ? "still present after recycle" : null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}