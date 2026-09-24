using System.Runtime.InteropServices;

namespace CleanBoost.Core.Safety;

/// <summary>
/// Guards exactly which paths the engine is allowed to touch.
/// Protected roots can never be deleted, and any scan that resolves into a
/// protected tree is refused outright. Symlink/junction components are also
/// refused: the engine never follows links during deletion.
/// </summary>
public sealed class PathGuard
{
    private readonly List<string> _protectedRoots = new();
    private readonly HashSet<string> _exactRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _allowances = new();

    public PathGuard(bool addBuiltInRoots = true,
                     IEnumerable<string>? extraProtectedRoots = null,
                     IEnumerable<string>? extraAllowances = null)
    {
        if (addBuiltInRoots)
            AddBuiltInRoots();
        if (extraProtectedRoots is not null)
            foreach (var root in extraProtectedRoots)
                AddRoot(root);
        if (extraAllowances is not null)
            foreach (var allowance in extraAllowances)
                AddAllowance(allowance);
    }

    public bool IsWindowsPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private void AddBuiltInRoots()
    {
        // Drive roots — with or without trailing separator, on the current OS.
        foreach (var drive in Environment.GetLogicalDrives())
            AddRoot(drive);

        var windir = Environment.GetEnvironmentVariable("WINDIR");
        if (!string.IsNullOrWhiteSpace(windir))
        {
            AddRoot(windir); // e.g. C:\Windows — covers System32, WinSxS, DriverStore, Temp (system)
            AddRoot(Path.Combine(windir, "System32"));
            AddRoot(Path.Combine(windir, "WinSxS"));
            AddRoot(Path.Combine(windir, "SoftwareDistribution"));
        }

        var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        if (!string.IsNullOrWhiteSpace(programFiles))
            AddRoot(programFiles);

        var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            AddRoot(programFilesX86);

        var programData = Environment.GetEnvironmentVariable("ProgramData");
        if (!string.IsNullOrWhiteSpace(programData))
            AddRoot(programData);

        // Never touch the profile root or the WindowsApps package store.
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
            AddRoot(Path.Combine(userProfile, "AppData", "Local", "Packages"));
    }

    public void AddRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;
        var normalized = Normalize(root);
        if (IsDriveRootToken(normalized))
        {
            // A bare drive (C:) must only refuse the drive itself, never the
            // whole drive tree — otherwise every user path would be un-cleanable.
            _exactRoots.Add(normalized);
            return;
        }
        if (_protectedRoots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return;
        _protectedRoots.Add(normalized);
    }

    private static bool IsDriveRootToken(string path)
        => path.Length == 2 && path[0] is >= 'a' and <= 'z' or >= 'A' and <= 'Z' && path[1] == ':';

    /// <summary>
    /// Carves out a cleanable subtree inside a protected root (e.g.
    /// C:\Windows\Temp, SoftwareDistribution\Download). The allowance itself
    /// and its children become legal; siblings stay protected.
    /// </summary>
    public void AddAllowance(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var normalized = Normalize(path);
        if (_allowances.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return;
        _allowances.Add(normalized);
    }

    public IReadOnlyList<string> ProtectedRoots => _protectedRoots;

    private bool IsAllowedPath(string normalized)
    {
        foreach (var allowance in _allowances)
        {
            if (IsWithinRoot(normalized, allowance))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> is inside a protected root
    /// (or is one itself). Comparisons are case-insensitive on all platforms
    /// because Windows paths are, and Linux comparisons here are conservative.
    /// </summary>
    public bool IsProtected(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        var normalized = Normalize(path);

        if (IsAllowedPath(normalized))
            return false;

        if (_exactRoots.Contains(normalized))
            return true;

        foreach (var root in _protectedRoots)
        {
            if (root.Length == 0)
                continue;
            if (IsWithinRoot(normalized, root))
                return true;
        }

        return false;
    }

    /// <summary>True when <paramref name="path"/> equals or lives under <paramref name="root"/>.
    /// Both slash styles are recognised so Windows-style strings compare correctly everywhere.</summary>
    private static bool IsWithinRoot(string path, string root)
    {
        if (path.Length < root.Length)
            return false;
        if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length == root.Length)
                return true;
            var next = path[root.Length];
            return next is '/' or '\\';
        }
        return false;
    }

    /// <summary>Determines whether a target root is legal to scan/delete.</summary>
    public PathGuardResult Evaluate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PathGuardResult.Refused("Path is empty.");
        if (!LooksAbsolute(path))
            return PathGuardResult.Refused($"Path is not absolute: {path}");
        if (IsProtected(path))
            return PathGuardResult.Refused($"Path resolves into a protected area: {path}");
        return PathGuardResult.Allowed(path);
    }

    private static bool LooksAbsolute(string path)
    {
        if (Path.IsPathRooted(path))
            return true;
        // Windows drive or UNC paths still count as absolute when running on Unix.
        if (path.Length >= 3 && path[1] == ':' && path[2] is '\\' or '/')
            return true;
        return path.StartsWith(@"\\", StringComparison.Ordinal);
    }

    private static string Normalize(string path)
        => path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ' ');

    public static bool IsSymbolicLink(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget is not null;
        }
        catch
        {
            return false;
        }
    }
}

public readonly record struct PathGuardResult(string? AllowedPath, string? Reason)
{
    public bool IsAllowed => AllowedPath is not null;

    public static PathGuardResult Allowed(string path) => new(path, null);
    public static PathGuardResult Refused(string reason) => new(null, reason);
}