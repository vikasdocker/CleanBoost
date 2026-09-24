using System.Diagnostics;
using CleanBoost.System.Interop;

namespace CleanBoost.System.Processes;

/// <summary>
/// Trims the working set of running processes — flushes RAM that the OS
/// paged in but the process is no longer using. It never kills anything.
/// This backs the optional RAM step in Turbo Boost.
/// </summary>
public static class ProcessTrimmer
{
    public static int TrimWorkingSet(IEnumerable<int> pids)
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        var trimmed = 0;
        foreach (var pid in pids.Distinct())
        {
            var handle = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_SET_QUOTA,
                bInheritHandle: false, (uint)pid);
            if (handle == IntPtr.Zero)
                continue;

            try
            {
                if (NativeMethods.SetProcessWorkingSetSize(handle, -1, -1))
                    trimmed++;
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
        return trimmed;
    }

    public static int TrimAllUsers()
    {
        var pids = Process.GetProcesses()
            .Where(p => p.Id is not 0 and not 4)
            .Select(p => p.Id);
        return TrimWorkingSet(pids);
    }
}