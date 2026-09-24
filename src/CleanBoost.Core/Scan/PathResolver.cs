using System.Collections.Frozen;

namespace CleanBoost.Core.Scan;

/// <summary>
/// Resolves winapp2-style path variables (e.g. %LocalAppData%, %WinDir%)
/// to concrete absolute paths. Falls back to real environment variables and
/// finally to common Windows locations so both the app and the test suite work.
/// </summary>
public sealed class PathResolver
{
    private readonly FrozenDictionary<string, string> _vars;

    public PathResolver(IDictionary<string, string>? overrides = null)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE")
                          ?? Environment.GetEnvironmentVariable("HOME") ?? string.Empty;

        void Add(string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) vars[name] = value; }

        Add("UserProfile", userProfile);
        Add("AppData", Env("APPDATA") ?? Path.Combine(userProfile, "AppData", "Roaming"));
        Add("LocalAppData", Env("LOCALAPPDATA") ?? Path.Combine(userProfile, "AppData", "Local"));
        Add("LocalLowAppData", Path.Combine(userProfile, "AppData", "LocalLow"));
        Add("ProgramData", Env("ProgramData") ?? Env("ALLUSERSPROFILE") ?? "C:\\ProgramData");
        Add("ProgramFiles", Env("ProgramFiles") ?? "C:\\Program Files");
        Add("ProgramFiles(x86)", Env("ProgramFiles(x86)") ?? "C:\\Program Files (x86)");
        Add("CommonProgramFiles", Env("CommonProgramFiles") ?? "C:\\Program Files\\Common Files");
        Add("Temp", Env("TEMP") ?? Env("TMP") ?? Path.Combine(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)));
        var windir = Env("WINDIR") ?? "C:\\Windows";
        Add("WinDir", windir);
        Add("System", Path.Combine(windir, "System32"));
        Add("System32", Path.Combine(windir, "System32"));
        Add("SystemDrive", Path.GetPathRoot(windir) ?? "C:");
        Add("UserDocuments", Path.Combine(userProfile, "Documents"));
        Add("Documents", Path.Combine(userProfile, "Documents"));
        Add("Desktop", Path.Combine(userProfile, "Desktop"));
        Add("Music", Path.Combine(userProfile, "Music"));
        Add("Pictures", Path.Combine(userProfile, "Pictures"));
        Add("Videos", Path.Combine(userProfile, "Videos"));

        if (overrides is not null)
            foreach (var kv in overrides)
                vars[kv.Key] = kv.Value;

        _vars = vars.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    public bool TryLookup(string name, out string value) => _vars.TryGetValue(name.Trim('%'), out value!);

    /// <summary>Expands <c>%VarName%</c> tokens. Unknown tokens are left untouched.</summary>
    public string Expand(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('%') < 0)
            return path;

        var sb = new System.Text.StringBuilder(path.Length);
        int i = 0;
        while (i < path.Length)
        {
            int start = path.IndexOf('%', i);
            if (start < 0)
            {
                sb.Append(path, i, path.Length - i);
                break;
            }
            sb.Append(path, i, start - i);
            int end = path.IndexOf('%', start + 1);
            if (end < 0)
            {
                sb.Append(path, start, path.Length - start);
                break;
            }
            var name = path[(start + 1)..end];
            if (_vars.TryGetValue(name, out var value))
            {
                sb.Append(value);
            }
            else
            {
                var resolved = Environment.GetEnvironmentVariable(name);
                sb.Append(resolved ?? path.Substring(start, end - start + 1));
            }
            i = end + 1;
        }
        return sb.ToString();
    }
}