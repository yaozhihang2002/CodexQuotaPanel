using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed class AlertWindow : Window
{
    public event EventHandler? DismissForCycleRequested;

    public AlertWindow(AppSettings settings, string title, string message, bool critical)
    {
        var palette = UiPalette.For(settings.Theme);
        Title = title;
        var scale = settings.InterfaceScalePercent / 100d;
        Width = Math.Clamp(390 * (.75 + .25 * scale), 370, 480);
        Height = Math.Clamp(190 * (.75 + .25 * scale), 180, 245);
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = palette.Canvas;
        var dismiss = UiElements.Button(settings.Language == AppLanguage.SimplifiedChinese ? "本周期不再提醒" : "Dismiss for this cycle", palette);
        dismiss.Click += (_, _) => { DismissForCycleRequested?.Invoke(this, EventArgs.Empty); Close(); };
        var close = UiElements.Button(settings.Language == AppLanguage.SimplifiedChinese ? "知道了" : "Got it", palette, true);
        close.Click += (_, _) => Close();
        Content = new StackPanel { Margin = new Thickness(22), Spacing = 10, Children =
        {
            UiElements.Text(title, 18, FontWeight.Bold, critical ? palette.Red : palette.Amber),
            UiElements.Text(message, 12, FontWeight.Normal, palette.TextSecondary),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9,
                HorizontalAlignment = HorizontalAlignment.Right, Children = { dismiss, close } }
        }};
    }
}
