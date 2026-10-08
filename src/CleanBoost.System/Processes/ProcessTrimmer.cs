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
    /// <summary>
    /// Processes that must never be trimmed. Trimming is harmless in principle but
    /// these own session and security state that the OS does not expect to lose.
    /// </summary>
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "registry", "smss", "csrss", "wininit", "services", "lsass",
        "lsm", "svchost", "winlogon", "dwm", "spoolsv", "win32k", "fontdrvhost",
        "sihost", "ctfmon", "conhost", "logonui", "winframe",
    };

    public static int TrimWorkingSet(IEnumerable<int> pids)
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        var trimmed = 0;
        foreach (var pid in pids.Distinct())
        {
            if (pid is 0 or 4)
                continue;

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
        if (!OperatingSystem.IsWindows())
            return 0;

        var ids = new List<int>(256);
        // Process objects hold an OS handle each. Disposing them matters: without
        // it a single Turbo pass leaked one handle per process on the machine.
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (Critical.Contains(process.ProcessName))
                        continue;
                    ids.Add(process.Id);
                }
                catch
                {
                    // process exited between enumeration and inspection
                }
            }
        }

        return TrimWorkingSet(ids);
    }
}