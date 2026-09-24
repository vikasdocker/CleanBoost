using CleanBoost.System.Interop;

namespace CleanBoost.System.Recycle;

/// <summary>
/// Empty-all-drives Recycle Bin. This is the one permanent-delete operation
/// the app offers, and it only ever touches files already in the Recycle Bin —
/// regular cleaning stays reversible.
/// </summary>
public static class RecycleBin
{
    public static bool EmptyAll()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        const uint flags = NativeMethods.SHERB_NOCONFIRMATION
                           | NativeMethods.SHERB_NOPROGRESSUI
                           | NativeMethods.SHERB_NOSOUND;
        return NativeMethods.SHEmptyRecycleBinW(IntPtr.Zero, null, flags) == 0;
    }
}