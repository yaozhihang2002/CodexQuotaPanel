using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Themes.Fluent;
using CodexQuota.Application;
using CodexQuota.Platform.Windows;
using CodexQuota.UI.Avalonia;

if (!OperatingSystem.IsWindows()) return;

AppBuilder.Configure<ThemeProbeApp>().UsePlatformDetect().SetupWithoutStarting();
var settings = new SettingsWindow(AppSettings.Default with { Theme = AppTheme.Dark }, systemDark: true);
settings.WindowStartupLocation = WindowStartupLocation.Manual;
settings.Position = new PixelPoint(30000, 30000);
settings.ShowActivated = false;
var handle = settings.TryGetPlatformHandle()?.Handle ?? 0;
if (handle == 0) throw new InvalidOperationException("Settings HWND is unavailable before Show.");

new WindowsPlatformShell().SetWindowDarkMode(handle, true);
var dark = 0;
var result = DwmGetWindowAttribute(handle, 20, ref dark, sizeof(int));
if (result != 0 || dark != 1)
    throw new InvalidOperationException($"Pre-show dark title bar was not applied: HRESULT={result:X8}, value={dark}.");

settings.Show();
dark = 0;
result = DwmGetWindowAttribute(handle, 20, ref dark, sizeof(int));
if (result != 0 || dark != 1)
    throw new InvalidOperationException($"Show reset the dark title bar: HRESULT={result:X8}, value={dark}.");

settings.ClosePermanently();
Console.WriteLine("PASS: Settings HWND accepts dark title-bar mode before Show and retains it after Show.");

[DllImport("dwmapi.dll")]
static extern int DwmGetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

sealed class ThemeProbeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
