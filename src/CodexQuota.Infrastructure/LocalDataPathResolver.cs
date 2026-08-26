namespace CodexQuota.Infrastructure;

public static class LocalDataPathResolver
{
    public static string ResolveApplicationData()
    {
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "CodexQuotaPanel");
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexQuotaPanel");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "CodexQuotaPanel");
    }
}
