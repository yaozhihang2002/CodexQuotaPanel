using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CodexQuota.UI.Avalonia;

public sealed class PreviewWindow : Window
{
    private static readonly IBrush Canvas = Brush("#0D1210");
    private static readonly IBrush Surface = Brush("#151C19");
    private static readonly IBrush BorderColor = Brush("#2B3933");
    private static readonly IBrush TextPrimary = Brush("#F2F4EF");
    private static readonly IBrush TextMuted = Brush("#9BADA4");
    private static readonly IBrush Mint = Brush("#57D9AA");

    public Grid ContentRegion { get; private set; } = null!;
    public StackPanel SummaryCards { get; private set; } = null!;
    public Border OrbPreviewPanel { get; private set; } = null!;

    public PreviewWindow()
    {
        Title = "CodexQuota vNext Preview";
        Width = 980;
        Height = 620;
        MinWidth = 760;
        MinHeight = 500;
        Background = Canvas;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var shell = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("196,*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Background = Canvas
        };

        var header = new Border
        {
            Padding = new Thickness(28, 18),
            Background = Brush("#121916"),
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    Text("CODEX  /  QUOTA", 24, FontWeight.Bold, TextPrimary),
                    Text("vNext · ambient instrument prototype", 12, FontWeight.Normal, TextMuted)
                }
            }
        };
        Grid.SetColumnSpan(header, 2);
        shell.Children.Add(header);

        var nav = new StackPanel { Spacing = 8, Margin = new Thickness(16, 20) };
        foreach (var (label, active) in new[]
                 {
                     ("Overview", true),
                     ("Appearance", false),
                     ("Interaction", false),
                     ("Notifications", false),
                     ("Data & About", false)
                 })
        {
            nav.Children.Add(new Border
            {
                Padding = new Thickness(14, 10),
                CornerRadius = new CornerRadius(9),
                Background = active ? Brush("#20322B") : Brushes.Transparent,
                BorderBrush = active ? Brush("#3D725F") : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Child = Text(
                    label,
                    13,
                    active ? FontWeight.SemiBold : FontWeight.Normal,
                    active ? TextPrimary : TextMuted)
            });
        }

        var sidebar = new Border
        {
            Background = Brush("#101613"),
            BorderBrush = BorderColor,
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
                Text("Your quota at a glance", 25, FontWeight.Bold, TextPrimary),
                Text(
                    "Actual pace and the even-use guide share one calm visual language.",
                    13,
                    FontWeight.Normal,
                    TextMuted)
            }
        };
        Grid.SetColumnSpan(heading, 2);
        content.Children.Add(heading);

        var cards = new StackPanel
        {
            Margin = new Thickness(0, 24, 20, 0),
            Spacing = 12
        };
        SummaryCards = cards;
        Grid.SetRow(cards, 1);
        cards.Children.Add(Card("CURRENT WINDOW", "62% remaining", "5d 16h until reset", Mint));
        cards.Children.Add(Card("PACE", "0.4% / hour", "Safely inside the 0.5% / hour guide", Brush("#7EC4FF")));
        cards.Children.Add(Card(
            "FORECAST",
            "Sustainable",
            "Conservative estimate · medium confidence",
            Brush("#E6B966")));
        content.Children.Add(cards);

        var orbPanel = new Border
        {
            Margin = new Thickness(0, 24, 0, 0),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(18),
            Background = Surface,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 14,
                Children =
                {
                    Text("ORB PREVIEW", 11, FontWeight.Bold, Mint),
                    new OrbControl { Width = 190, Height = 190, RemainingPercent = 62 },
                    Text("One render pass · no child overlays", 11, FontWeight.Normal, TextMuted)
                }
            }
        };
        OrbPreviewPanel = orbPanel;
        Grid.SetRow(orbPanel, 1);
        Grid.SetColumn(orbPanel, 1);
        content.Children.Add(orbPanel);

        shell.Children.Add(content);
        return shell;
    }

    private static Border Card(string eyebrow, string title, string detail, IBrush accent)
    {
        var body = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                Text(eyebrow, 10, FontWeight.Bold, accent),
                Text(title, 19, FontWeight.SemiBold, TextPrimary),
                Text(detail, 12, FontWeight.Normal, TextMuted)
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
            Background = Surface,
            BorderBrush = BorderColor,
            BorderThickness = new Thickness(1),
            Child = layout
        };
    }

    private static TextBlock Text(string value, double size, FontWeight weight, IBrush brush) => new()
    {
        Text = value,
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI, SF Pro Display, sans-serif"),
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = size * 1.35d,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
}
