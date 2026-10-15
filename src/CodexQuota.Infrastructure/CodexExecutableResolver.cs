using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace CodexQuota.Infrastructure;

/// <summary>Discovers installed clients without scanning drives or reading authentication data.</summary>
public sealed class CodexExecutableResolver
{
    private readonly Func<string, string?> _environment;
    private readonly string _localData;
    private readonly Func<IEnumerable<string>> _installedRoots;
    private readonly Func<IEnumerable<string>> _runningRoots;

    public CodexExecutableResolver(Func<string, string?>? environment = null, string? localData = null,
        Func<IEnumerable<string>>? installedRoots = null, Func<IEnumerable<string>>? runningRoots = null)
    {
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _localData = localData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _installedRoots = installedRoots ?? (() => OperatingSystem.IsWindows() ? WindowsInstallRoots() : []);
        _runningRoots = runningRoots ?? RunningAppRoots;
    }

    public string? ResolveWindows()
    {
        var configured = _environment("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"'));
            if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path;
        }

        // Preserve existing working installations. Version directories are bounded;
        // do not recursively walk arbitrary user-selected installation roots.
        var cache = Path.Combine(_localData, "OpenAI", "Codex", "bin");
        try
        {
            if (Directory.Exists(cache))
            {
                var candidate = Directory.EnumerateFiles(cache, "codex.exe", new EnumerationOptions
                { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true })
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (candidate is not null) return candidate;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        foreach (var root in _runningRoots().Concat(_installedRoots()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = FindInAppRoot(root);
            if (candidate is not null) return candidate;
        }
        // Explicit PATH only: do not accidentally execute codex.exe from the working directory.
        foreach (var directory in (_environment("PATH") ?? "").Split(Path.PathSeparator))
        {
            var root = directory.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(root)) continue;
            var candidate = Path.Combine(root, "codex.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    public static string? FindInAppRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return null;
        foreach (var relative in new[] { "resources/codex.exe", "app/resources/codex.exe",
                     "resources/bin/codex.exe", "app/resources/bin/codex.exe", "bin/codex.exe", "codex.exe" })
        {
            var candidate = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static IEnumerable<string> RunningAppRoots()
    {
        var roots = new List<string>();
        using var current = Process.GetCurrentProcess();
        var session = current.SessionId;
        foreach (var name in new[] { "ChatGPT", "ChatGPT (Beta)", "ChatGPT Beta", "Codex", "Codex (Beta)", "Codex Beta" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session) continue;
                    var path = process.MainModule?.FileName;
                    if (path is not null) roots.Add(Path.GetDirectoryName(path)!);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                               or NotSupportedException) { }
            }
        }
        return roots;
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> WindowsInstallRoots()
    {
        var roots = new List<string>();
        // Current user's Store registrations include packages moved to other drives.
        // A fixed script and system PowerShell avoid PATH/script injection. No elevation.
        const string script = "$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
            "Get-AppxPackage | Where-Object { $_.Name -in @('OpenAI.Codex','OpenAI.CodexBeta','OpenAI.ChatGPT','OpenAI.ChatGPTBeta','OpenAI.ChatGPT-Desktop') } | " +
            "Sort-Object Version -Descending | ForEach-Object { $_.InstallLocation }";
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8
            }};
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand",
                         Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) process.StartInfo.ArgumentList.Add(arg);
            if (process.Start())
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (process.WaitForExit(6000))
                {
                    foreach (var line in output.GetAwaiter().GetResult().Split('\n'))
                        if (!string.IsNullOrWhiteSpace(line)) roots.Add(line.Trim());
                    _ = errors.GetAwaiter().GetResult();
                }
                else process.Kill();
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var registry = RegistryKey.OpenBaseKey(hive, view);
                foreach (var executableName in new[] { "ChatGPT.exe", "ChatGPT (Beta).exe", "Codex.exe", "Codex (Beta).exe" })
                {
                    using var appPath = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + executableName);
                    if (appPath?.GetValue("") is not string executable) continue;
                    executable = Environment.ExpandEnvironmentVariables(executable.Trim().Trim('"'));
                    if (Path.IsPathFullyQualified(executable)) roots.Add(Path.GetDirectoryName(executable)!);
                }
                using var uninstall = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var key in uninstall.GetSubKeyNames())
                {
                    using var app = uninstall.OpenSubKey(key);
                    var name = app?.GetValue("DisplayName") as string;
                    if (name is not ("ChatGPT" or "ChatGPT (Beta)" or "ChatGPT Beta" or "Codex" or "Codex (Beta)" or "Codex Beta")) continue;
                    if (app?.GetValue("InstallLocation") is string location && !string.IsNullOrWhiteSpace(location))
                        roots.Add(Environment.ExpandEnvironmentVariables(location.Trim().Trim('"')));
                    if (app?.GetValue("DisplayIcon") is string icon)
                    {
                        var executable = icon.StartsWith('"') ? icon.Split('"').ElementAtOrDefault(1)
                            : icon.Split(',')[0];
                        if (!string.IsNullOrWhiteSpace(executable) && Path.IsPathFullyQualified(executable))
                            roots.Add(Path.GetDirectoryName(executable)!);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return roots;
    }
}
