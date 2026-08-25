namespace CodexQuota.Infrastructure;

public sealed class CodexHomeResolver
{
    private readonly Func<string, string?> _readEnvironment;
    private readonly Func<string> _readUserHome;

    public CodexHomeResolver(
        Func<string, string?>? readEnvironment = null,
        Func<string>? readUserHome = null)
    {
        _readEnvironment = readEnvironment ?? Environment.GetEnvironmentVariable;
        _readUserHome = readUserHome ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public string Resolve()
    {
        var configured = _readEnvironment("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));

        var home = _readUserHome();
        if (string.IsNullOrWhiteSpace(home))
            throw new InvalidOperationException("The user home directory is unavailable.");

        return Path.Combine(Path.GetFullPath(home), ".codex");
    }
}
