using System.Runtime.InteropServices;
using System.Text;

namespace CodexQuotaPanel;

/// <summary>
/// Creates a small support summary that intentionally excludes paths, account data,
/// environment variables, session contents, and other identity-bearing details.
/// </summary>
internal static class SanitizedDiagnostics
{
    public static string Create(
        string? dataSource,
        DateTimeOffset? lastUpdatedAt,
        QuotaHistoryStore history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var assembly = typeof(SanitizedDiagnostics).Assembly;
        var version = assembly.GetName().Version;
        var productVersion = version is null
            ? "Unknown"
            : version.Build >= 0 ? version.ToString(3) : version.ToString(2);
        var windowsVersion = RemoveControlCharacters(RuntimeInformation.OSDescription).Trim();
        if (string.IsNullOrWhiteSpace(windowsVersion)) windowsVersion = "Windows (version unavailable)";

        var builder = new StringBuilder(256);
        builder.AppendLine("Codex Quota Panel diagnostics");
        builder.Append("Application version: ").AppendLine(productVersion);
        builder.Append("Windows version: ").AppendLine(windowsVersion);
        builder.Append("Process: ").AppendLine(Environment.Is64BitProcess ? "64-bit" : "32-bit");
        builder.Append("Data source: ").AppendLine(SanitizeSourceName(dataSource));
        builder.Append("Last updated (UTC): ").AppendLine(
            lastUpdatedAt?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") ?? "Unavailable");
        builder.Append("History: ").AppendLine(history.Enabled ? "Enabled" : "Disabled");
        builder.Append("History points: ").Append(history.Count);
        return builder.ToString();
    }

    private static string SanitizeSourceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";

        var sanitized = RemoveControlCharacters(value).Trim();
        var userName = Environment.UserName;
        if (sanitized.IndexOfAny(['\\', '/']) >= 0 ||
            (!string.IsNullOrWhiteSpace(userName) &&
             sanitized.Contains(userName, StringComparison.OrdinalIgnoreCase)))
        {
            return "Redacted";
        }

        if (sanitized.Length > 80) sanitized = sanitized[..80];
        return sanitized.Length == 0 ? "Unknown" : sanitized;
    }

    private static string RemoveControlCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsControl(character) ? ' ' : character);
        return builder.ToString();
    }
}
