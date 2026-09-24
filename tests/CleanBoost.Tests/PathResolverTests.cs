using CleanBoost.Core.Scan;
using Xunit;

namespace CleanBoost.Core.Tests;

public class PathResolverTests
{
    [Fact]
    public void Expands_known_variables()
    {
        var resolver = new PathResolver(new Dictionary<string, string>
        {
            ["TestRoot"] = @"C:\fake\test",
            ["LocalAppData"] = @"C:\fake\Local",
        });

        Assert.Equal(@"C:\fake\test\cache", resolver.Expand(@"%TestRoot%\cache"));
        Assert.Equal(@"C:\fake\Local\Google", resolver.Expand(@"%LocalAppData%\Google"));
    }

    [Fact]
    public void Leaves_unknown_tokens_alone()
    {
        var resolver = new PathResolver();
        const string input = "%NoSuchVar%\\x";
        Assert.Equal(input, resolver.Expand(input));
    }

    [Fact]
    public void Expands_windows_temp_from_environment()
    {
        var resolver = new PathResolver();
        var expanded = resolver.Expand("%Temp%\\foo");
        Assert.Contains("foo", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%", expanded);
    }
}