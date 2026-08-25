using CodexQuota.Application;
using CodexQuota.Infrastructure;

var overrideResolver = new CodexHomeResolver(
    key => key == "CODEX_HOME" ? Path.Combine(Path.GetTempPath(), "custom-codex") : null,
    () => Path.Combine(Path.GetTempPath(), "home"));
Check.Equal(
    Path.GetFullPath(Path.Combine(Path.GetTempPath(), "custom-codex")),
    overrideResolver.Resolve(),
    "CODEX_HOME override");

var fallbackResolver = new CodexHomeResolver(
    _ => null,
    () => Path.Combine(Path.GetTempPath(), "home"));
Check.Equal(
    Path.GetFullPath(Path.Combine(Path.GetTempPath(), "home", ".codex")),
    fallbackResolver.Resolve(),
    "home fallback");

var root = Path.Combine(Path.GetTempPath(), "CodexQuotaPanel-vnext-tests", Guid.NewGuid().ToString("N"));
var path = Path.Combine(root, "settings.json");
try
{
    var store = new JsonSettingsStore(path);
    await store.WriteAsync(
        AppSettings.Default with { OrbSize = 140, Theme = AppTheme.Light },
        CancellationToken.None);
    var read = await store.ReadAsync(CancellationToken.None);
    Check.Equal(140, read?.OrbSize, "settings round trip size");
    Check.Equal(AppTheme.Light, read?.Theme, "settings round trip theme");
}
finally
{
    if (Directory.Exists(root))
        Directory.Delete(root, true);
}

Console.WriteLine("Infrastructure checks passed: 4");

static class Check
{
    public static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }
}
