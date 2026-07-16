using System.Reflection;

namespace CodexQuotaPanel;

/// <summary>
/// Single source of truth for the user-facing product version.  Keeping the
/// RPC client and GitHub update checker on the same value prevents release
/// metadata from drifting between subsystems.
/// </summary>
internal static class ProductVersionInfo
{
    internal static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(ProductVersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var metadataIndex = informational.IndexOf('+');
            return metadataIndex >= 0 ? informational[..metadataIndex] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
