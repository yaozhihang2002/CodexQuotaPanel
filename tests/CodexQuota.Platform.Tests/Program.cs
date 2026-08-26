using CodexQuota.Application;
using CodexQuota.Platform.macOS;
using CodexQuota.Platform.Windows;

IPlatformShell shell = OperatingSystem.IsWindows() ? new WindowsPlatformShell() : new MacOSPlatformShell();
Check.True(shell.SupportsClickThrough, "click-through capability");
Check.True(shell.SupportsMenuBarOrTray, "tray capability");
Check.True(shell.SupportsGlobalShortcut, "global shortcut capability");
shell.SetClickThrough(0, true);
shell.SetWindowTopMost(0, true);
shell.SetWindowDarkMode(0, true);
_ = shell.GetStartWithSystem();
_ = shell.GetInitialLanguage();
if (OperatingSystem.IsWindows())
{
    using var registration = shell.RegisterRecoveryShortcut(() => { });
    Check.True(registration is not null, "Windows recovery hotkey registration");
}
var checkCount = 4;
if (string.Equals(Environment.GetEnvironmentVariable("CODEXQUOTA_PLATFORM_MUTATION_TESTS"), "1",
        StringComparison.Ordinal))
{
    var priorStartup = shell.GetStartWithSystem();
    try
    {
        shell.SetStartWithSystem(true);
        Check.True(shell.GetStartWithSystem(), "startup integration enable");
        shell.SetStartWithSystem(false);
        Check.True(!shell.GetStartWithSystem(), "startup integration disable");
        using var recovery = shell.RegisterRecoveryShortcut(() => { });
        Check.True(recovery?.IsRegistered == true, "platform recovery shortcut registration");
        checkCount += 3;
    }
    finally
    {
        shell.SetStartWithSystem(priorStartup);
    }
}
Console.WriteLine($"Platform checks passed: {shell.PlatformName} ({checkCount})");

static class Check
{
    public static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }
}
