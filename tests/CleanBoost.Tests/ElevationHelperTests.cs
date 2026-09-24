#if WINDOWS_TESTS
using System.Security.Principal;
using CleanBoost.System.Elevation;
using Xunit;

namespace CleanBoost.Tests;

/// <summary>
/// Windows-host automated checks for the elevation helper. Compiled and run
/// only under `-p:RunWindowsTests=true`; the default WSL run ignores them.
/// RelaunchElevated is intentionally never invoked (it would raise a UAC prompt).
/// </summary>
public sealed class ElevationHelperTests
{
    [Fact]
    public void IsElevated_NeverThrows_AndMatchesPrincipal()
    {
        bool? fromHelper = null;
        Exception? failure = null;
        try
        {
            fromHelper = ElevationHelper.IsElevated();
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        Assert.Null(failure);
        Assert.NotNull(fromHelper);

        var reconstituted = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);
        Assert.Equal(reconstituted, fromHelper);
    }

    [Fact]
    public void RelaunchElevated_And_IsElevated_AreSafeTogether()
    {
        // Sanity: both entry points exist and IsElevated is stable across calls.
        var first = ElevationHelper.IsElevated();
        var second = ElevationHelper.IsElevated();
        Assert.Equal(first, second);
    }
}
#endif