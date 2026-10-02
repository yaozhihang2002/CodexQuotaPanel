using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed class ResetToastWindow : Window
{
    public ResetToastWindow(AppSettings settings, string message)
    {
        var palette = UiPalette.For(settings.Theme);
        Title = settings.Language == AppLanguage.SimplifiedChinese ? "额度已恢复" : "Quota reset";
        Width = 330; Height = 100; CanResize = false; ShowActivated = false;
        ShowInTaskbar = false; Topmost = true; Background = palette.Canvas;
        Content = new StackPanel { Margin = new Thickness(18, 12), Spacing = 7, Children =
        {
            UiElements.Text(Title, 17, FontWeight.Bold, palette.Mint),
            UiElements.Text(message, 12, FontWeight.Normal, palette.TextPrimary)
        }};
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) => Close();
        Opened += (_, _) =>
        {
            if (Screens.Primary is { } screen)
                Position = new PixelPoint(screen.WorkingArea.Right - (int)((Width + 20) * screen.Scaling),
                    screen.WorkingArea.Bottom - (int)((Height + 40) * screen.Scaling));
            timer.Start();
        };
        Closed += (_, _) => timer.Stop();
    }
}
