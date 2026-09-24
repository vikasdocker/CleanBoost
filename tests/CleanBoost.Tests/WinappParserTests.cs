using CleanBoost.Core.Rules;
using CleanBoost.Core.Safety;
using Xunit;

namespace CleanBoost.Core.Tests;

public class WinappParserTests
{
    private const string Sample = """
        ; demo rules
        [Sample App *]
        Section=Application
        DetectFile=%LocalAppData%\SampleApp
        FileKey1=%LocalAppData%\SampleApp\Cache|*.tmp;*.log|RECURSE
        FileKey2=%AppData%\SampleApp\Thumbs|*|REMOVESELF

        [Scary Tool *]
        Section=Utilities
        Warning=This deletes saved sessions
        FileKey1=%AppData%\ScaryTool\sessions|*.dat|RECURSE|REMOVESELF

        [RegistryOnly *]
        Section=Application
        RegKey1=HKCU\Software\Foo
        """;

    [Fact]
    public void Parses_entries_into_categories()
    {
        using var reader = new StringReader(Sample);
        var categories = WinappParser.Parse(reader);

        // Registry-only entry dropped, only 2 categories remain.
        Assert.Equal(2, categories.Count);

        var sample = categories.Single(c => c.Title == "Sample App");
        Assert.Equal(RiskLevel.Caution, sample.Risk);
        Assert.Equal(2, sample.Targets.Count);

        var cacheTarget = sample.Targets[0];
        Assert.Equal("%LocalAppData%\\SampleApp\\Cache", cacheTarget.Path);
        Assert.Equal("*.tmp;*.log", cacheTarget.Patterns);
        Assert.True(cacheTarget.Recurse);
        Assert.False(cacheTarget.RemoveSelf);

        var thumbs = sample.Targets[1];
        Assert.True(thumbs.RemoveSelf);

        var scary = categories.Single(c => c.Title == "Scary Tool");
        Assert.Equal(RiskLevel.ConfirmFirst, scary.Risk);
    }

    [Fact]
    public void Parser_round_trips_a_real_winapp2_entry()
    {
        var categories = WinappParser.ParseFile(
            Path.Combine(TestPath.RulesDir, "winapp2.ini"));

        Assert.True(categories.Count > 100, $"expected many entries, got {categories.Count}");
        Assert.Contains(categories, c =>
            c.Title.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
            c.Title.Contains("Firefox", StringComparison.OrdinalIgnoreCase));

        var first = categories[0];
        Assert.All(first.Targets, t => Assert.False(string.IsNullOrWhiteSpace(t.Path)));
    }

    [Fact]
    public void LocateRulesFile_finds_the_shipped_database()
    {
        var path = WinappParser.LocateRulesFile();
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        Assert.EndsWith("winapp2.ini", path, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class TestPath
{
    public static string RulesDir { get; } = Find();

    private static string Find()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "rules");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("rules directory not found");
    }
}