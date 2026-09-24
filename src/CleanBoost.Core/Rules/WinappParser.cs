using CleanBoost.Core.Catalog;
using CleanBoost.Core.Safety;

namespace CleanBoost.Core.Rules;

/// <summary>
/// Parses the community-maintained winapp2.ini database of cleaning recipes
/// into <see cref="CleanCategory"/> instances. Only file-based recipes are
/// used: the engine intentionally never clears the registry, so registry-only
/// entries are dropped.
/// </summary>
public static class WinappParser
{
    public static IReadOnlyList<CleanCategory> Parse(TextReader reader)
    {
        var sections = new List<(string RawName, Dictionary<string, string> Keys)>();
        Dictionary<string, string>? current = null;
        string currentName = "";

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(';'))
                continue;

            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (current is not null)
                    sections.Add((currentName, current));
                currentName = trimmed[1..^1].Trim();
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (current is null)
                continue;

            var eq = trimmed.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            current[key] = value;
        }

        if (current is not null)
            sections.Add((currentName, current));

        var categories = new List<CleanCategory>();
        foreach (var (rawName, keys) in sections)
        {
            var name = rawName.EndsWith('*') ? rawName[..^1].Trim() : rawName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var targets = new List<CleanTarget>();
            foreach (var (k, v) in keys)
            {
                if (!k.StartsWith("FileKey", StringComparison.OrdinalIgnoreCase))
                    continue;
                var target = TryParseFileKey(v);
                if (target is not null)
                    targets.Add(target);
            }

            if (targets.Count == 0)
                continue; // registry-only or empty recipe

            var hasWarning = keys.ContainsKey("Warning");
            var section = keys.TryGetValue("Section", out var s) ? s : "Application";

            categories.Add(new CleanCategory
            {
                Key = Slugify(name),
                Title = name,
                Description = BuildDescription(name, section, hasWarning),
                Risk = hasWarning ? RiskLevel.ConfirmFirst : RiskLevel.Caution,
                Icon = "\uE74C",
                Targets = targets,
            });
        }

        return categories;
    }

    public static IReadOnlyList<CleanCategory> ParseFile(string path)
    {
        using var reader = new StreamReader(path);
        return Parse(reader);
    }

    /// <summary>
    /// Locates the community rules database. Search order: explicit candidates,
    /// the app's own rules folder, the per-user override folder
    /// (%LocalAppData%\CleanBoost\rules), then any parent folder walking up from
    /// the base directory (development fallback). Returns null when not found.
    /// </summary>
    public static string? LocateRulesFile(params string[] extraCandidates)
    {
        var candidates = new List<string>();
        candidates.AddRange(extraCandidates.Where(c => !string.IsNullOrWhiteSpace(c)));

        foreach (var baseDir in new[] { AppContext.BaseDirectory })
            candidates.Add(Path.Combine(baseDir, "rules", "winapp2.ini"));

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanBoost", "rules", "winapp2.ini"));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            candidates.Add(Path.Combine(dir.FullName, "rules", "winapp2.ini"));
            dir = dir.Parent;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string BuildDescription(string name, string section, bool hasWarning)
    {
        var text = $"Community cleaning rule for {name} (winapp2.ini, section {section}).";
        if (hasWarning)
            text += " This recipe carries a caution note — review listed items before running.";
        return text;
    }

    /// <summary>Parse a FileKey value: %path%|pattern1;pattern2[|RECURSE][|REMOVESELF]</summary>
    private static CleanTarget? TryParseFileKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var parts = value.Split('|');
        var path = parts[0].Trim();
        if (string.IsNullOrWhiteSpace(path) || path == "*")
            return null;

        if (!path.StartsWith('%') && !Path.IsPathRooted(path))
            return null; // un-addressable relative or pseudo path

        var patterns = "*";
        bool recurse = false;
        bool removeSelf = false;

        for (int i = 1; i < parts.Length; i++)
        {
            var flag = parts[i].Trim();
            if (flag.Equals("RECURSE", StringComparison.OrdinalIgnoreCase))
                recurse = true;
            else if (flag.Equals("REMOVESELF", StringComparison.OrdinalIgnoreCase))
                removeSelf = true;
            else if (flag.Length > 0 && patterns == "*")
                patterns = flag;
        }

        return new CleanTarget
        {
            Path = path,
            Patterns = patterns,
            Recurse = recurse,
            RemoveSelf = removeSelf,
        };
    }

    private static string Slugify(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return string.IsNullOrEmpty(slug) ? "winapp-" + Guid.NewGuid().ToString("N")[..8] : slug;
    }
}