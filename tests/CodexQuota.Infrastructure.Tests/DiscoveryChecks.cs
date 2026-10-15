using CodexQuota.Infrastructure;

static class DiscoveryChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "quota-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string Add(string relative)
            {
                var path = Path.GetFullPath(Path.Combine(root, relative));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "test fixture - never executed");
                return path;
            }
            var stable = Add("custom folder/ChatGPT/app/resources/codex.exe");
            var beta = Add("other drive/Beta/resources/codex.exe");
            var cache = Path.Combine(root, "local");
            string? envOverride = null;
            string? pathEnv = null;
            var installed = new List<string> { Path.Combine(root, "missing"), Path.Combine(root, "custom folder/ChatGPT") };
            var running = new List<string>();
            var resolver = new CodexExecutableResolver(key => key == "CODEX_CLI_PATH" ? envOverride :
                key == "PATH" ? pathEnv : null, cache, () => installed, () => running);
            Check.Equal(stable, resolver.ResolveWindows(), "registered custom installation without running app");
            installed.Clear(); installed.Add(Path.Combine(root, "other drive/Beta"));
            Check.Equal(beta, resolver.ResolveWindows(), "registered Beta in arbitrary directory");
            installed.Clear(); running.Add(Path.Combine(root, "custom folder/ChatGPT/app"));
            Check.Equal(stable, resolver.ResolveWindows(), "running portable client directory");
            var cached = Add("local/OpenAI/Codex/bin/build/codex.exe");
            Check.Equal(cached, resolver.ResolveWindows(), "existing cache remains preferred");
            envOverride = '"' + beta + '"';
            Check.Equal(beta, resolver.ResolveWindows(), "quoted explicit override");
            envOverride = Path.Combine(root, "missing.exe");
            Check.Equal(cached, resolver.ResolveWindows(), "missing override falls back");
            File.Delete(cached); running.Clear();
            pathEnv = Path.GetDirectoryName(beta);
            Check.Equal(beta, resolver.ResolveWindows(), "explicit PATH executable");
            File.Delete(beta);
            Check.True(resolver.ResolveWindows() is null, "removed installations are rediscovered, no cached stale path");
            pathEnv = ".";
            Check.True(resolver.ResolveWindows() is null, "relative PATH not searched");
            Console.WriteLine("PASS executable discovery: stable, Beta, custom, portable, override, PATH, upgrade/removal");
        }
        finally { Directory.Delete(root, true); }
    }
}
