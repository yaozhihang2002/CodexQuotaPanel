using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed class MessageWindow : Window
{
    public MessageWindow(AppSettings settings, string title, string message, bool showOpenButton = false)
    {
        var palette = UiPalette.For(settings.Theme);
        Title = title;
        var scale = settings.InterfaceScalePercent / 100d;
        Width = Math.Clamp(430 * (.75 + .25 * scale), 400, 520);
        Height = Math.Clamp(205 * (.75 + .25 * scale), 195, 260);
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = palette.Canvas;
        var close = UiElements.Button(settings.Language == AppLanguage.SimplifiedChinese ? "确定" : "OK", palette, true);
        close.Click += (_, _) => Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right, Children = { close } };
        if (showOpenButton)
        {
            var open = UiElements.Button(settings.Language == AppLanguage.SimplifiedChinese ? "打开发布页" : "Open release page", palette);
            open.Click += (_, _) => Close(true);
            buttons.Children.Insert(0, open);
        }
        Content = new StackPanel { Margin = new Thickness(22), Spacing = 12, Children =
        {
            UiElements.Text(title, 18, FontWeight.Bold, palette.TextPrimary),
            UiElements.Text(message, 12, FontWeight.Normal, palette.TextSecondary),
            buttons
        }};
    }
}

public sealed class ClickThroughReminderWindow : Window
{
    public bool DoNotShowAgain { get; private set; }

    public ClickThroughReminderWindow(AppSettings settings)
    {
        var palette = UiPalette.For(settings.Theme);
        Title = settings.Language == AppLanguage.SimplifiedChinese ? "鼠标穿透提醒" : "Click-through reminder";
        var scale = settings.InterfaceScalePercent / 100d;
        Width = Math.Clamp(450 * (.75 + .25 * scale), 420, 550);
        Height = Math.Clamp(235 * (.75 + .25 * scale), 220, 290);
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = palette.Canvas;
        var never = new CheckBox { Content = settings.Language == AppLanguage.SimplifiedChinese ? "不再提醒（可在设置中恢复）" : "Do not remind me again (reversible in Settings)",
            FontFamily = UiElements.AppFont, Foreground = palette.TextSecondary };
        never.IsCheckedChanged += (_, _) => DoNotShowAgain = never.IsChecked == true;
        var cancel = UiElements.Button(settings.Language == AppLanguage.SimplifiedChinese ? "取消" : "Cancel", palette);
        cancel.Click += (_, _) => Close(false);
        var enable = UiElements.Button(settings.Language == AppLanguage.SimplifiedChinese ? "启用穿透" : "Enable", palette, true);
        enable.Click += (_, _) => Close(true);
        Content = new StackPanel { Margin = new Thickness(22), Spacing = 12, Children =
        {
            UiElements.Text(Title!, 18, FontWeight.Bold, palette.TextPrimary),
            UiElements.Text(settings.Language == AppLanguage.SimplifiedChinese
                ? "启用后悬浮球不再接收鼠标操作。可从托盘菜单关闭，或使用 Ctrl+Alt+Shift+Q 恢复。"
                : "The orb will stop receiving mouse input. Disable it from the tray or press Ctrl+Alt+Shift+Q.",
                12, FontWeight.Normal, palette.TextSecondary),
            never,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancel, enable } }
        }};
    }
}
