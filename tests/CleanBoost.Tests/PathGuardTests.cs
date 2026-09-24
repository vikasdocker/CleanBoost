using CleanBoost.Core.Safety;
using Xunit;

namespace CleanBoost.Core.Tests;

/// <summary>Safety-critical: the path guard must refuse anything it should never touch.</summary>
public class PathGuardTests
{
    private static PathGuard Guard() => new(addBuiltInRoots: false,
        extraProtectedRoots: new[]
        {
            "C:\\", "C:\\Windows", "C:\\Windows\\System32",
            "C:\\Program Files", "C:\\Program Files (x86)",
        },
        extraAllowances: new[] { "C:\\Windows\\Temp", "C:\\Windows\\SoftwareDistribution\\Download" });

    [Theory]
    [InlineData("C:\\Windows")]
    [InlineData("C:\\Windows\\System32")]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("C:\\Program Files\\SomeApp")]
    [InlineData("C:\\Program Files (x86)\\SomeApp")]
    [InlineData("C:\\")]
    [InlineData("C:\\Windows\\WinSxS")]
    [InlineData("C:\\Windows\\DriverStore")]
    [InlineData("C:\\Windows\\SoftwareDistribution")]
    public void Refuses_protected_paths(string path)
    {
        var guard = Guard();
        Assert.True(guard.IsProtected(path), $"expected {path} to be protected");
    }

    [Theory]
    [InlineData("C:\\Windows\\Temp")]                           // carve-out itself
    [InlineData("C:\\Windows\\Temp\\app.log")]                  // inside carve-out
    [InlineData("C:\\Windows\\SoftwareDistribution\\Download\\cab")] // carve-out subfolder
    [InlineData("C:\\Users\\vikas\\AppData\\Local\\Temp\\x.tmp")]    // normal user path
    public void Allows_guarded_allowance_and_user_paths(string path)
    {
        var guard = Guard();
        Assert.False(guard.IsProtected(path), $"expected {path} to be allowed");
    }

    [Fact]
    public void Evaluate_is_allowed_for_clean_path()
    {
        var guard = Guard();
        var result = guard.Evaluate("C:\\Users\\vikas\\AppData\\Local\\Temp\\x.tmp");
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_is_refused_for_protected_path()
    {
        var guard = Guard();
        var result = guard.Evaluate("C:\\Windows\\System32\\kernel32.dll");
        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
    }
}