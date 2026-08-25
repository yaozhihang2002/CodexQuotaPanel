using CodexQuota.Application;

namespace CodexQuota.Platform.Windows;

public sealed class WindowsPlatformShell : IPlatformShell
{
    public string PlatformName => "Windows";
    public bool SupportsClickThrough => true;
    public bool SupportsMenuBarOrTray => true;
}
