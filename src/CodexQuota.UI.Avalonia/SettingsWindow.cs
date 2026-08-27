using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CodexQuota.Application;
using System.Reflection;

namespace CodexQuota.UI.Avalonia;

public sealed partial class SettingsWindow : Window
{
    private AppSettings _persisted;
    private AppSettings _draft;
    private UiPalette _palette;
    private readonly bool _systemDark;
    private readonly int[] _availableWindowMinutes;
    private Grid _pages;
    private TextBlock _status;
    private OrbControl? _orbPreview;
    private readonly List<Button> _navButtons = [];
    private readonly List<Control> _pageControls = [];
    private bool _allowClose;
    private bool _building = true;
    private int _selectedPage;
    private int _appliedInterfaceScale;

    public event Action<AppSettings>? PreviewChanged;
    public event Action<AppSettings>? SaveRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler? ExportRequested;
    public event EventHandler? UpdateCheckRequested;
    public event EventHandler? ClearHistoryRequested;
    public event EventHandler? CopyDiagnosticsRequested;
    public event EventHandler? RestoreDefaultsRequested;
    public event EventHandler? OpenProjectRequested;
    public event EventHandler? OpenPricingRequested;

    public SettingsWindow(AppSettings settings, bool systemDark = true, IReadOnlyList<int>? availableWindowMinutes = null)
    {
        _persisted = _draft = settings.Normalize();
        _systemDark = systemDark;
        _availableWindowMinutes = (availableWindowMinutes ?? [])
            .Append(_draft.OuterWindowMinutes).Append(_draft.InnerWindowMinutes)
            .Where(minutes => minutes > 0).Distinct().OrderBy(minutes => minutes).ToArray();
        _palette = UiPalette.For(_draft.Theme, _systemDark);
        _appliedInterfaceScale = _draft.InterfaceScalePercent;
        Title = T("Codex 额度面板设置", "CodexQuota Settings");
        Width = Math.Clamp(900 + (_draft.InterfaceScalePercent - 100) * 3.2, 820, 1060);
        Height = Math.Clamp(650 + (_draft.InterfaceScalePercent - 100) * 1.4, 600, 760);
        MinWidth = 780;
        MinHeight = 560;
        MaxWidth = 1080;
        MaxHeight = 820;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = _palette.Canvas;
        _pages = new Grid();
        _status = UiElements.Text(T("所有更改已保存", "All changes saved"), 11, FontWeight.SemiBold, _palette.TextMuted);
        Content = BuildShell();
        _building = false;
        SelectPage(0);
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            RequestCancel();
        };
    }

    public void MarkSaved(AppSettings settings)
    {
        _persisted = _draft = settings.Normalize();
        _status.Text = T("更改已保存 · 可继续修改", "Changes saved · continue editing");
        _status.Foreground = _palette.Mint;
    }

    public void ClosePermanently() { _allowClose = true; Close(); }

    private Control BuildShell()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = _palette.Canvas };
        var header = new Border
        {
            Background = _palette.Header,
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 16),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("6,16,*,Auto"),
                Children =
                {
                    new Border { Background = _palette.Mint, CornerRadius = new CornerRadius(3) },
                    HeaderCopy(),
                    UiElements.Text("CODEX · SETTINGS", 10.5, FontWeight.Bold, _palette.Mint)
                }
            }
        };
        var headerGrid = (Grid)header.Child;
        Grid.SetColumn(headerGrid.Children[1], 2);
        Grid.SetColumn(headerGrid.Children[2], 3);
        root.Children.Add(header);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("166,*") };
        var nav = new StackPanel { Spacing = 6, Margin = new Thickness(12, 16) };
        var labels = _draft.Language == AppLanguage.SimplifiedChinese
            ? new[] { "常规", "外观", "交互", "通知", "数据与关于" }
            : new[] { "General", "Appearance", "Interaction", "Notifications", "Data & About" };
        for (var i = 0; i < labels.Length; i++)
        {
            var index = i;
            var button = UiElements.Button(labels[i], _palette);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Click += (_, _) => SelectPage(index);
            _navButtons.Add(button);
            nav.Children.Add(button);
        }
        body.Children.Add(new Border { Background = _palette.Sidebar, BorderBrush = _palette.Border,
            BorderThickness = new Thickness(0, 0, 1, 0), Child = nav });

        _pageControls.Add(BuildGeneralPage());
        _pageControls.Add(BuildAppearancePage());
        _pageControls.Add(BuildInteractionPage());
        _pageControls.Add(BuildNotificationsPage());
        _pageControls.Add(BuildDataPage());
        foreach (var page in _pageControls) _pages.Children.Add(page);
        var pageHost = new Border { Padding = new Thickness(22, 16, 18, 10), Child = _pages };
        Grid.SetColumn(pageHost, 1);
        body.Children.Add(pageHost);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(18, 10, 18, 14) };
        footer.Children.Add(_status);
        var cancel = UiElements.Button(T("取消", "Cancel"), _palette);
        cancel.Click += (_, _) => RequestCancel();
        Grid.SetColumn(cancel, 1);
        footer.Children.Add(cancel);
        var save = UiElements.Button(T("保存并应用", "Save & apply"), _palette, true);
        save.Margin = new Thickness(10, 0, 0, 0);
        save.Click += (_, _) => SaveRequested?.Invoke(_draft.Normalize());
        Grid.SetColumn(save, 2);
        footer.Children.Add(save);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private Control HeaderCopy() => new StackPanel { Spacing = 1, Children =
    {
        UiElements.Text("Codex / " + T("额度面板", "Quota Panel"), 23, FontWeight.Bold, _palette.TextPrimary),
        UiElements.Text(T("紧凑、即时且可撤销的设置", "Compact, immediate and reversible settings"), 10.5, FontWeight.Normal, _palette.TextMuted)
    }};

    private void Change(Func<AppSettings, AppSettings> change)
    {
        var previous = _draft;
        _draft = change(_draft).Normalize();
        UiElements.ScaleFactor = _draft.InterfaceScalePercent / 100d;
        if (_draft.Theme != previous.Theme || _draft.Language != previous.Language)
            RebuildShell();
        else if (_draft.InterfaceScalePercent != _appliedInterfaceScale)
            ApplyInterfaceScale(_draft.InterfaceScalePercent);
        ApplyDraftToOrbPreview();
        _status.Text = T("即时预览 · 尚未保存", "Live preview · not saved");
        _status.Foreground = _palette.Amber;
        PreviewChanged?.Invoke(_draft);
    }

    private void SelectPage(int index)
    {
        _selectedPage = index;
        for (var i = 0; i < _pageControls.Count; i++)
        {
            _pageControls[i].IsVisible = i == index;
            _navButtons[i].Background = i == index ? _palette.Active : Brushes.Transparent;
            _navButtons[i].BorderBrush = i == index ? _palette.Mint : Brushes.Transparent;
        }
    }

    private void RebuildShell()
    {
        _building = true;
        _palette = UiPalette.For(_draft.Theme, _systemDark);
        _navButtons.Clear();
        _pageControls.Clear();
        _pages = new Grid();
        _status = UiElements.Text(T("即时预览 · 尚未保存", "Live preview · not saved"), 11,
            FontWeight.SemiBold, _palette.Amber);
        Content = BuildShell();
        Background = _palette.Canvas;
        Width = Math.Clamp(900 + (_draft.InterfaceScalePercent - 100) * 3.2, 820, 1060);
        Height = Math.Clamp(650 + (_draft.InterfaceScalePercent - 100) * 1.4, 600, 760);
        _building = false;
        SelectPage(Math.Clamp(_selectedPage, 0, _pageControls.Count - 1));
        _appliedInterfaceScale = _draft.InterfaceScalePercent;
    }

    private void ApplyInterfaceScale(int percent)
    {
        if (Content is not Visual root || _appliedInterfaceScale <= 0) return;
        var ratio = percent / (double)_appliedInterfaceScale;
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (control is TextBlock text) text.FontSize *= ratio;
            else if (control is TemplatedControl templated &&
                     (control is ComboBox || control is NumericUpDown || control is TextBox || control is Button))
                templated.FontSize *= ratio;
        }
        Width = Math.Clamp(900 + (percent - 100) * 3.2, 820, 1060);
        Height = Math.Clamp(650 + (percent - 100) * 1.4, 600, 760);
        _appliedInterfaceScale = percent;
    }

    private void ApplyDraftToOrbPreview()
    {
        if (_orbPreview is null) return;
        _orbPreview.OrbBackground = Color.Parse(_draft.OrbBackground);
        _orbPreview.OrbBackgroundOpacity = _draft.OrbOpacityPercent / 100d;
        _orbPreview.OuterRingColor = Color.Parse(_draft.OuterRingColor);
        _orbPreview.InnerRingColor = Color.Parse(_draft.InnerRingColor);
        _orbPreview.FeedbackEnabled = _draft.ConsumptionFeedbackEnabled;
        _orbPreview.FeedbackStyle = _draft.ConsumptionFeedbackStyle;
        _orbPreview.AnimateFeedback = !_draft.ReducedMotion;
    }

    private void RequestCancel()
    {
        _draft = _persisted;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private string T(string zh, string en) => _draft.Language == AppLanguage.SimplifiedChinese ? zh : en;

    private static string AppVersion => Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.6.1";

    private sealed record WindowChoice(int Minutes, string Label)
    {
        public override string ToString() => Label;
    }
}
