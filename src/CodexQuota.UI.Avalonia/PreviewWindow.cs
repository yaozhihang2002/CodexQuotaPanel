using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public sealed record PreviewScenario(AppLanguage Language, AppTheme Theme, bool DualRing)
{
    public static PreviewScenario Default { get; } = new(AppLanguage.English, AppTheme.Dark, false);
}

public sealed class PreviewWindow : Window
{
    private readonly PreviewScenario _scenario;
    private readonly PreviewPalette _palette;
    private readonly PreviewCopy _copy;
    private TextBlock _remainingValue = null!;
    private TextBlock _resetValue = null!;

    public Grid ContentRegion { get; private set; } = null!;
    public StackPanel SummaryCards { get; private set; } = null!;
    public Border OrbPreviewPanel { get; private set; } = null!;
    public OrbControl OrbPreviewControl { get; private set; } = null!;

    public PreviewWindow() : this(PreviewScenario.Default) { }

    public PreviewWindow(PreviewScenario scenario)
    {
        _scenario = scenario;
        _palette = PreviewPalette.For(scenario.Theme);
        _copy = PreviewCopy.For(scenario.Language);
        Title = _copy.WindowTitle;
        Width = 980;
        Height = 620;
        MinWidth = 760;
        MinHeight = 500;
        Background = _palette.Canvas;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var shell = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("196,*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = _palette.Canvas
        };

        var header = new Border
        {
            Padding = new Thickness(28, 18),
            Background = _palette.Header,
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    Text("CODEX  /  QUOTA", 24, FontWeight.Bold, _palette.TextPrimary),
                    Text(_copy.Subtitle, 12, FontWeight.Normal, _palette.TextMuted)
                }
            }
        };
        Grid.SetColumnSpan(header, 2);
        shell.Children.Add(header);

        var nav = new StackPanel { Spacing = 8, Margin = new Thickness(16, 20) };
        foreach (var (label, active) in _copy.Navigation.Select((label, index) => (label, index == 0)))
        {
            nav.Children.Add(new Border
            {
                Padding = new Thickness(14, 10),
                CornerRadius = new CornerRadius(9),
                Background = active ? _palette.ActiveNav : Brushes.Transparent,
                BorderBrush = active ? _palette.ActiveBorder : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Child = Text(label, 13, active ? FontWeight.SemiBold : FontWeight.Normal,
                    active ? _palette.TextPrimary : _palette.TextMuted)
            });
        }

        var sidebar = new Border
        {
            Background = _palette.Sidebar,
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = nav
        };
        Grid.SetRow(sidebar, 1);
        shell.Children.Add(sidebar);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,260"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(30, 24)
        };
        ContentRegion = content;
        Grid.SetRow(content, 1);
        Grid.SetColumn(content, 1);

        var heading = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                Text(_copy.Heading, 25, FontWeight.Bold, _palette.TextPrimary),
                Text(_copy.Description, 13, FontWeight.Normal, _palette.TextMuted)
            }
        };
        Grid.SetColumnSpan(heading, 2);
        content.Children.Add(heading);

        SummaryCards = new StackPanel { Margin = new Thickness(0, 24, 20, 0), Spacing = 12 };
        Grid.SetRow(SummaryCards, 1);
        SummaryCards.Children.Add(CurrentWindowCard());
        SummaryCards.Children.Add(Card(_copy.Pace, "0.4% / h", _copy.PaceDetail, _palette.Blue));
        SummaryCards.Children.Add(Card(_copy.Forecast, _copy.Sustainable, _copy.ForecastDetail, _palette.Amber));
        content.Children.Add(SummaryCards);

        OrbPreviewControl = new OrbControl
        {
            Width = 190,
            Height = 190,
            MinWidth = 190,
            MinHeight = 190,
            HorizontalAlignment = HorizontalAlignment.Center,
            RemainingPercent = 62,
            SecondaryRemainingPercent = _scenario.DualRing ? 38 : double.NaN,
            Caption = _copy.OrbCaption
        };
        OrbPreviewPanel = new Border
        {
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(18),
            Background = _palette.Surface,
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 14,
                Children =
                {
                    Text(_copy.OrbPreview, 11, FontWeight.Bold, _palette.Mint),
                    OrbPreviewControl,
                    Text(_scenario.DualRing ? _copy.DualRing : _copy.SingleRing,
                        11, FontWeight.Normal, _palette.TextMuted)
                }
            }
        };
        Grid.SetRow(OrbPreviewPanel, 1);
        Grid.SetColumn(OrbPreviewPanel, 1);
        content.Children.Add(OrbPreviewPanel);
        shell.Children.Add(content);
        return shell;
    }

    public void ApplyQuota(OfficialQuotaSnapshot snapshot)
    {
        var windows = snapshot.VisibleWindows;
        if (windows.Count == 0) return;
        OrbPreviewControl.RemainingPercent = windows[0].ClampedRemainingPercent;
        OrbPreviewControl.SecondaryRemainingPercent = windows.Count > 1
            ? windows[1].ClampedRemainingPercent
            : double.NaN;
        var remaining = windows.Min(window => window.ClampedRemainingPercent);
        _remainingValue.Text = _scenario.Language == AppLanguage.SimplifiedChinese
            ? $"剩余 {remaining:0}%"
            : $"{remaining:0}% remaining";
        var reset = windows.Where(window => window.ResetsAt is not null)
            .OrderBy(window => window.ResetsAt).FirstOrDefault()?.ResetsAt;
        if (reset is not null)
        {
            var duration = reset.Value - snapshot.ObservedAt;
            _resetValue.Text = _scenario.Language == AppLanguage.SimplifiedChinese
                ? $"{Math.Max(0, (int)duration.TotalDays)} 天 {Math.Max(0, duration.Hours)} 小时后重置"
                : $"Resets in {Math.Max(0, (int)duration.TotalDays)}d {Math.Max(0, duration.Hours)}h";
        }
    }

    private Border CurrentWindowCard()
    {
        _remainingValue = Text(_copy.Remaining, 19, FontWeight.SemiBold, _palette.TextPrimary);
        _resetValue = Text(_copy.Reset, 12, FontWeight.Normal, _palette.TextMuted);
        var body = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                Text(_copy.CurrentWindow, 10, FontWeight.Bold, _palette.Mint),
                _remainingValue,
                _resetValue
            }
        };
        Grid.SetColumn(body, 2);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("5,16,*") };
        layout.Children.Add(new Border { Background = _palette.Mint, CornerRadius = new CornerRadius(3) });
        layout.Children.Add(body);
        return new Border
        {
            Padding = new Thickness(18, 14), CornerRadius = new CornerRadius(13),
            Background = _palette.Surface, BorderBrush = _palette.Border,
            BorderThickness = new Thickness(1), Child = layout
        };
    }

    private Border Card(string eyebrow, string title, string detail, IBrush accent)
    {
        var body = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                Text(eyebrow, 10, FontWeight.Bold, accent),
                Text(title, 19, FontWeight.SemiBold, _palette.TextPrimary),
                Text(detail, 12, FontWeight.Normal, _palette.TextMuted)
            }
        };
        Grid.SetColumn(body, 2);
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("5,16,*") };
        layout.Children.Add(new Border { Background = accent, CornerRadius = new CornerRadius(3) });
        layout.Children.Add(body);
        return new Border
        {
            Padding = new Thickness(18, 14),
            CornerRadius = new CornerRadius(13),
            Background = _palette.Surface,
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(1),
            Child = layout
        };
    }

    private static TextBlock Text(string value, double size, FontWeight weight, IBrush brush) => new()
    {
        Text = value,
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, PingFang SC, Microsoft YaHei UI, SF Pro Display, sans-serif"),
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = size * 1.35d,
        VerticalAlignment = VerticalAlignment.Center
    };
}

internal sealed record PreviewPalette(
    IBrush Canvas, IBrush Header, IBrush Sidebar, IBrush Surface, IBrush Border,
    IBrush ActiveNav, IBrush ActiveBorder, IBrush TextPrimary, IBrush TextMuted,
    IBrush Mint, IBrush Blue, IBrush Amber)
{
    public static PreviewPalette For(AppTheme theme) => theme == AppTheme.Light
        ? new PreviewPalette(
            Brush("#F3F6F2"), Brush("#EAF1ED"), Brush("#EEF3F0"), Brush("#FBFCFA"),
            Brush("#CCD8D2"), Brush("#DDEBE4"), Brush("#83B9A4"), Brush("#17211D"),
            Brush("#5D7067"), Brush("#168A67"), Brush("#287EAC"), Brush("#9A650B"))
        : new PreviewPalette(
            Brush("#0D1210"), Brush("#121916"), Brush("#101613"), Brush("#151C19"),
            Brush("#2B3933"), Brush("#20322B"), Brush("#3D725F"), Brush("#F2F4EF"),
            Brush("#9BADA4"), Brush("#57D9AA"), Brush("#7EC4FF"), Brush("#E6B966"));

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
}

internal sealed record PreviewCopy(
    string WindowTitle, string Subtitle, IReadOnlyList<string> Navigation, string Heading,
    string Description, string CurrentWindow, string Remaining, string Reset, string Pace,
    string PaceDetail, string Forecast, string Sustainable, string ForecastDetail,
    string OrbPreview, string OrbCaption, string SingleRing, string DualRing)
{
    public static PreviewCopy For(AppLanguage language) => language == AppLanguage.SimplifiedChinese
        ? new PreviewCopy(
            "Codex 额度面板 · vNext", "vNext · 环境仪表原型",
            ["概览", "外观", "交互", "通知", "数据与关于"],
            "额度状态，一眼看清", "实际消耗与均匀使用参考线采用同一套安静、清晰的视觉语言。",
            "当前窗口", "剩余 62%", "5 天 16 小时后重置", "使用速度",
            "低于 0.5% / 小时的安全速度", "续航预测", "可以持续到重置",
            "保守估计 · 中等置信度", "悬浮球预览", "剩余", "自动单环 · 一次绘制", "自动双环 · 一次绘制")
        : new PreviewCopy(
            "CodexQuota vNext Preview", "vNext · ambient instrument prototype",
            ["Overview", "Appearance", "Interaction", "Notifications", "Data & About"],
            "Your quota at a glance", "Actual pace and the even-use guide share one calm visual language.",
            "CURRENT WINDOW", "62% remaining", "5d 16h until reset", "PACE",
            "Safely inside the 0.5% / hour guide", "FORECAST", "Sustainable",
            "Conservative estimate · medium confidence", "ORB PREVIEW", "REMAINING",
            "Adaptive single ring · one render pass", "Adaptive dual ring · one render pass");
}
