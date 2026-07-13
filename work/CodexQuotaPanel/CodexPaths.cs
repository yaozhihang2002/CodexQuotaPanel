namespace CodexQuotaPanel;

internal static class CodexPaths
{
    public static string UserHome
    {
        get
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
                return fromEnvironment;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
    }

    public static string Home
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrWhiteSpace(configured) ? Path.Combine(UserHome, ".codex") : configured;
        }
    }
}
