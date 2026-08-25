using CodexQuota.Application;

namespace CodexQuota.Platform.macOS;

public sealed class MacOSPlatformShell : IPlatformShell
{
    public string PlatformName => "macOS";
    public bool SupportsClickThrough => true;
    public bool SupportsMenuBarOrTray => true;
}
