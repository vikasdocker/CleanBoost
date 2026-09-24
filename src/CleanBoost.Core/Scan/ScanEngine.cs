
using System.Text;
using System.Text.RegularExpressions;
using CleanBoost.Core.Catalog;
using CleanBoost.Core.Safety;

namespace CleanBoost.Core.Scan;

/// <summary>
/// Walks cleanup categories and enumerates candidate files in a dry-run pass.
/// Nothing is deleted here — this is the preview layer.
/// </summary>
public sealed class ScanEngine
{
    private readonly PathResolver _resolver;
    private readonly PathGuard _guard;

    public ScanEngine(PathResolver? resolver = null, PathGuard? guard = null)
    {
        _resolver = resolver ?? new PathResolver();
        _guard = guard ?? new PathGuard();
    }

    public ScanReport Scan(IEnumerable<CleanCategory> categories,
                           IProgress<ScanProgress>? progress = null,
                           CancellationToken cancellationToken = default)
    {
        var report = new ScanReport();
        var nowUtc = DateTime.UtcNow;

        foreach (var category in categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long seen = 0;

            foreach (var target in category.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var resolved = _resolver.Expand(target.Path);
                var evaluation = _guard.Evaluate(resolved);
                if (!evaluation.IsAllowed)
                    continue; // protected system area — refuse silently

                Visit(resolved, target, category, report, nowUtc, ref seen, progress, cancellationToken);
                progress?.Report(new ScanProgress(resolved, seen, category.Title));
            }
        }

        return report;
    }

    private void Visit(string path, CleanTarget target, CleanCategory category, ScanReport report,
                       DateTime nowUtc, ref long seen, IProgress<ScanProgress>? progress,
                       CancellationToken token)
    {
        // The target may include wildcards inside directory segments.
        var concreteRoots = ExpandPathWildcards(path);
        foreach (var root in concreteRoots)
        {
            token.ThrowIfCancellationRequested();
            WalkRoot(root, target, category, report, nowUtc, ref seen, progress, token);
        }
    }

    private void WalkRoot(string root, CleanTarget target, CleanCategory category, ScanReport report,
                          DateTime nowUtc, ref long seen, IProgress<ScanProgress>? progress,
                          CancellationToken token)
    {
        if (File.Exists(root))
        {
            AddFile(root, target, category, report, nowUtc, ref seen, progress, token);
            return;
        }

        var matcher = BuildMatcher(target.Patterns);

        // Files directly in the root.
        if (Directory.Exists(root))
        {
            EnumerationOptions options = new() { IgnoreInaccessible = true, ReturnSpecialDirectories = false, RecurseSubdirectories = false };
            try
            {
                foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (matcher != null && !matcher.IsMatch(file.Name))
                        continue;
                    if (SatisfiesAge(file, target, nowUtc))
                        AddFileInfo(file, target, category, report, ref seen, progress, token);
                }
            }
            catch (Exception) { /* inaccessible — skip */ }

            if (target.Recurse)
            {
                try
                {
                    foreach (var dir in new DirectoryInfo(root).EnumerateDirectories("*", options))
                    {
                        token.ThrowIfCancellationRequested();
                        WalkDirectory(dir, target, category, report, nowUtc, ref seen, progress, token);
                    }
                }
                catch (Exception) { /* inaccessible — skip */ }
            }
        }
    }

    private void WalkDirectory(DirectoryInfo dir, CleanTarget target, CleanCategory category, ScanReport report,
                               DateTime nowUtc, ref long seen, IProgress<ScanProgress>? progress,
                               CancellationToken token)
    {
        if (PathGuard.IsSymbolicLink(dir))
            return; // never follow junctions/symlinks

        var matcher = BuildMatcher(target.Patterns);
        try
        {
            foreach (var file in dir.EnumerateFiles("*", new EnumerationOptions { IgnoreInaccessible = true }))
            {
                token.ThrowIfCancellationRequested();
                if (matcher != null && !matcher.IsMatch(file.Name))
                    continue;
                if (SatisfiesAge(file, target, nowUtc))
                    AddFileInfo(file, target, category, report, ref seen, progress, token);
            }
            foreach (var sub in dir.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true }))
            {
                token.ThrowIfCancellationRequested();
                WalkDirectory(sub, target, category, report, nowUtc, ref seen, progress, token);
            }
        }
        catch (Exception) { /* inaccessible — skip */ }
    }

    private bool SatisfiesAge(FileInfo file, CleanTarget target, DateTime nowUtc)
        => target.OlderThan is null || file.LastWriteTimeUtc < nowUtc - target.OlderThan.Value;

    private void AddFile(string filePath, CleanTarget target, CleanCategory category, ScanReport report,
                         DateTime nowUtc, ref long seen, IProgress<ScanProgress>? progress,
                         CancellationToken token)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (SatisfiesAge(info, target, nowUtc))
                AddFileInfo(info, target, category, report, ref seen, progress, token);
        }
        catch (Exception) { /* inaccessible — skip */ }
    }

    private void AddFileInfo(FileInfo file, CleanTarget target, CleanCategory category, ScanReport report,
                             ref long seen, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (PathGuard.IsSymbolicLink(file))
            return;

        long size;
        try { size = file.Length; }
        catch (Exception) { size = 0; }

        report.Add(new ScanItem
        {
            Path = file.FullName,
            Size = size,
            LastWriteTimeUtc = file.LastWriteTimeUtc,
            CategoryKey = category.Key,
            CategoryTitle = category.Title,
            Risk = category.Risk,
        });
        seen++;
    }

    /// <summary>Expands wildcards that appear inside path segments to concrete existing paths.</summary>
    private static List<string> ExpandPathWildcards(string path)
    {
        if (path.IndexOf('*') < 0)
            return new List<string> { path };

        var parts = path.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        if (parts.Length == 0)
            return new List<string> { path };

        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root))
            return new List<string> { path };

        var current = new List<string> { root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) };

        foreach (var part in parts)
        {
            var next = new List<string>();
            var hasWildcard = part.IndexOf('*') >= 0 || part.IndexOf('?') >= 0;
            var pattern = hasWildcard ? new Regex(RegexFromWildcard(part), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) : null;
            foreach (var dir in current)
            {
                if (!Directory.Exists(dir))
                    continue;
                if (pattern is null)
                {
                    var candidate = Path.Combine(dir, part);
                    if (Directory.Exists(candidate) || File.Exists(candidate))
                        next.Add(candidate);
                    continue;
                }
                try
                {
                    foreach (var sub in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true }))
                    {
                        if (pattern.IsMatch(sub.Name))
                            next.Add(sub.FullName);
                    }
                }
                catch (Exception) { /* skip */ }
            }
            current = next;
        }

        return current;
    }

    private static readonly object _matcherCacheLock = new();
    private static readonly Dictionary<string, Regex?> _matcherCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Builds a matcher for a semicolon-separated pattern list (e.g. *.log;*.tmp).
    /// Returns null when patterns are empty or match anything (the caller then accepts all).
    /// </summary>
    private static Regex? BuildMatcher(string patterns)
    {
        if (string.IsNullOrWhiteSpace(patterns))
            return null;

        var trimmed = patterns.Trim();
        if (trimmed is "*" or "*.*")
            return null;

        lock (_matcherCacheLock)
        {
            if (_matcherCache.TryGetValue(trimmed, out var cached))
                return cached;

            var alternatives = trimmed.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .Select(RegexFromWildcard)
                                      .ToList();
            var regex = new Regex(string.Join("|", alternatives), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            _matcherCache[trimmed] = regex;
            return regex;
        }
    }

    private static string RegexFromWildcard(string pattern)
    {
        var sb = new StringBuilder();
        foreach (var ch in pattern)
        {
            sb.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                '.' => @"\.",
                '\\' => @"\\",
                '^' => @"\^",
                '$' => @"\$",
                '|' => @"\|",
                '(' => @"\(",
                ')' => @"\)",
                '[' => @"\[",
                ']' => @"\]",
                '{' => @"\{",
                '}' => @"\}",
                '+' => @"\+",
                _ => ch.ToString(),
            });
        }
        return "^" + sb + "$";
    }
}