using System.Reflection;

namespace CleanBoost.App;

/// <summary>
/// Single source of truth for on-screen brand identity.
/// Mirrors the values declared in CleanBoost.App.csproj so the version(s)
/// shown in the About surface, the Installer metadata and the UI never drift.
/// </summary>
public static class ProductInfo
{
    /// <summary>Company behind the product. Copy of csproj &lt;Company&gt;.</summary>
    public const string Company = "JS BlueFluteX";

    /// <summary>Human owner of the software. Copy of csproj &lt;Authors&gt;.</summary>
    public const string Owner = "vikas shelar";

    /// <summary>Product display name. Copy of csproj &lt;Product&gt;.</summary>
    public const string Product = "CleanBoost";

    /// <summary>Short copyright line. Copy of csproj &lt;Copyright&gt;.</summary>
    public const string Copyright = "(c) 2026 vikas shelar (JS BlueFluteX)";

    /// <summary>
    /// Build/version stamp embedded in the assembly at compile time.
    /// Kept identical to the version used when publishing the MSIX + zip.
    /// </summary>
    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
