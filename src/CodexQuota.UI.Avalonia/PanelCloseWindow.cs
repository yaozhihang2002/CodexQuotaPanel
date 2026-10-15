using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed class PanelCloseWindow : Window
{
    private readonly CheckBox _remember;
    public bool RememberChoice => _remember.IsChecked == true;

    public PanelCloseWindow(AppSettings settings, bool systemDark)
    {
        string T(string zh, string en) => settings.Language == AppLanguage.SimplifiedChinese ? zh : en;
        var palette = UiPalette.For(settings.Theme, systemDark);
        Title = T("关闭面板", "Close panel");
        Width = Math.Max(380, (settings.Language == AppLanguage.English ? 440 : 380) * UiElements.ScaleFactor);
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        RequestedThemeVariant = UiPalette.IsDarkTheme(settings.Theme, systemDark)
            ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        _remember = new CheckBox { Content = T("记住我的选择", "Remember my choice"), Foreground = palette.TextSecondary,
            FontFamily = UiElements.AppFont, FontSize = 11.5 * UiElements.ScaleFactor, MinHeight = 26,
            HorizontalAlignment = HorizontalAlignment.Center };
        _remember.Resources["CheckBoxCheckBackgroundStrokeUnchecked"] = palette.TextSecondary;
        _remember.Resources["CheckBoxCheckBackgroundStrokeUncheckedPointerOver"] = palette.TextPrimary;
        _remember.Resources["CheckBoxCheckBackgroundStrokeUncheckedPressed"] = palette.Mint;
        var tray = UiElements.Button(T("隐藏到托盘", "Hide to tray"), palette, true);
        var exit = UiElements.Button(T("退出应用", "Quit app"), palette);
        foreach (var button in new[] { tray, exit })
        {
            button.MinHeight = 34;
            button.FontSize = 12 * UiElements.ScaleFactor;
            button.FontWeight = FontWeight.Medium;
            button.Padding = new Thickness(10, 6);
        }
        tray.Click += (_, _) => Close(PanelCloseAction.Tray);
        exit.Click += (_, _) => Close(PanelCloseAction.Exit);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        tray.HorizontalAlignment = HorizontalAlignment.Stretch;
        exit.HorizontalAlignment = HorizontalAlignment.Stretch;
        tray.HorizontalContentAlignment = HorizontalAlignment.Center;
        exit.HorizontalContentAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(exit, 1);
        actions.Children.Add(tray);
        actions.Children.Add(exit);
        KeyDown += (_, e) => { if (e.Key == global::Avalonia.Input.Key.Escape) Close(PanelCloseAction.Ask); };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(UiElements.Text(Title!, 17, FontWeight.SemiBold, palette.TextPrimary));
        var close = UiElements.Button("×", palette);
        close.Width = 28;
        close.MinHeight = close.Height = 28;
        close.Padding = new Thickness(0);
        close.Background = close.BorderBrush = Brushes.Transparent;
        close.FontSize = 20;
        global::Avalonia.Automation.AutomationProperties.SetName(close, T("取消关闭", "Cancel closing"));
        ToolTip.SetTip(close, T("取消", "Cancel"));
        close.Click += (_, _) => Close(PanelCloseAction.Ask);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        Content = new Border { Background = palette.Canvas, BorderBrush = palette.Border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), ClipToBounds = true,
            Child = new StackPanel { Margin = new Thickness(18), Spacing = 10, Children =
        {
            header,
            UiElements.Text(T("隐藏后继续后台刷新，退出后停止运行。", "Hide to keep refreshing in the background; quit to stop."), 11.5, FontWeight.Normal, palette.TextSecondary),
            actions, _remember
        }}};
    }
}
