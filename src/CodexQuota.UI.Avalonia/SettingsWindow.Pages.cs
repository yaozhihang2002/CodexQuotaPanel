using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public sealed partial class SettingsWindow
{
    private Control BuildGeneralPage()
    {
        var panel = Page(T("常规", "General"), T("启动方式、语言与主题", "Startup, language and theme"));
        panel.Children.Add(Row(T("随系统启动", "Start with system"), T("登录后在后台启动额度面板", "Launch after sign-in"),
            Check(_draft.StartWithSystem, value => Change(s => s with { StartWithSystem = value }))));
        panel.Children.Add(Row(T("启动后显示", "Startup view"), T("恢复状态、悬浮球、详情或仅托盘", "Restore, orb, details or tray only"),
            Combo(Enum.GetValues<StartupViewMode>(), _draft.StartupView, value => Change(s => s with { StartupView = value }))));
        panel.Children.Add(Row(T("界面语言", "Language"), T("保存后用于所有窗口和托盘", "Used by all windows and tray after save"),
            Combo(Enum.GetValues<AppLanguage>(), _draft.Language, value => Change(s => s with { Language = value }))));
        panel.Children.Add(Row(T("界面主题", "Theme"), T("跟随系统、深色或浅色", "System, dark or light"),
            Combo(Enum.GetValues<AppTheme>(), _draft.Theme, value => Change(s => s with { Theme = value }))));
        return Scroll(panel);
    }

    private Control BuildAppearancePage()
    {
        var panel = Page(T("外观", "Appearance"), T("悬浮球即时预览与视觉反馈", "Live orb preview and visual feedback"));
        _orbPreview = new OrbControl { Width = 116, Height = 116, RemainingPercent = 68, SecondaryRemainingPercent = 29,
            OrbBackground = Color.Parse(_draft.OrbBackground), OrbBackgroundOpacity = _draft.OrbOpacityPercent / 100d,
            OuterRingColor = Color.Parse(_draft.OuterRingColor),
            InnerRingColor = Color.Parse(_draft.InnerRingColor), FeedbackEnabled = _draft.ConsumptionFeedbackEnabled,
            FeedbackStyle = _draft.ConsumptionFeedbackStyle, FeedbackIntensity = .55,
            AnimateFeedback = !_draft.ReducedMotion };
        panel.Children.Add(Row(T("悬浮球即时预览", "Live orb preview"), T("默认保持黑底，避免浅色主题出现白边", "Black by default, without light-theme halo"), _orbPreview));
        panel.Children.Add(Row(T("悬浮球大小", "Orb size"), "56–192 px", NumberSlider(56, 192, _draft.OrbSize,
            value => Change(s => s with { OrbSize = value }))));
        panel.Children.Add(Row(T("设置与弹窗字体", "Interface scale"), "80–150%", NumberSlider(80, 150, _draft.InterfaceScalePercent,
            value => Change(s => s with { InterfaceScalePercent = value }))));
        panel.Children.Add(Row(T("背景不透明度", "Background opacity"), "30–100%", NumberSlider(30, 100, _draft.OrbOpacityPercent,
            value => Change(s => s with { OrbOpacityPercent = value }))));
        panel.Children.Add(Row(T("额度环颜色", "Quota colors"), T("外环 / 内环", "Outer / inner"),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                ColorBox(_draft.OuterRingColor, value => Change(s => s with { OuterRingColor = value })),
                ColorBox(_draft.InnerRingColor, value => Change(s => s with { InnerRingColor = value }))
            }}));
        panel.Children.Add(Row(T("消耗反馈", "Usage feedback"), T("选择风格，或关闭动态反馈", "Choose a style or turn feedback off"),
            Combo(Enum.GetValues<FeedbackOption>(), _draft.ConsumptionFeedbackEnabled
                ? (FeedbackOption)((int)_draft.ConsumptionFeedbackStyle + 1) : FeedbackOption.Off,
                value => Change(s => s with { ConsumptionFeedbackEnabled = value != FeedbackOption.Off,
                    ConsumptionFeedbackStyle = value == FeedbackOption.Off ? s.ConsumptionFeedbackStyle : (ConsumptionFeedbackStyle)((int)value - 1) }))));
        return Scroll(panel);
    }

    private Control BuildInteractionPage()
    {
        var panel = Page(T("交互", "Interaction"), T("置顶、穿透、拖动与恢复入口", "Topmost, click-through, dragging and recovery"));
        panel.Children.Add(UiElements.Card(new StackPanel { Spacing = 4, Children =
        {
            UiElements.Text(T("移动提示", "MOVING THE ORB"), 12.5, FontWeight.Bold, _palette.Mint),
            UiElements.Text(T("鼠标穿透或锁定位置会阻止直接拖动。需要移动时，可从托盘选择“移动悬浮球…”，原设置会在拖动完成后自动恢复。",
                    "Click-through or position lock blocks direct dragging. Choose “Move orb…” from the tray; original settings return after the drag."),
                10.5, FontWeight.Normal, _palette.TextSecondary)
        }}, _palette));
        panel.Children.Add(ToggleRow(T("始终置顶", "Always on top"), T("定期校正，避免被其他窗口覆盖", "Reasserted periodically"),
            _draft.AlwaysOnTop, value => Change(s => s with { AlwaysOnTop = value })));
        panel.Children.Add(ToggleRow(T("鼠标穿透", "Click-through"), T("启用后用托盘或恢复快捷键关闭", "Use tray or recovery shortcut to disable"),
            _draft.ClickThrough, value => Change(s => s with { ClickThrough = value })));
        panel.Children.Add(ToggleRow(T("锁定位置", "Lock position"), T("防止误拖动", "Prevent accidental dragging"),
            _draft.PositionLocked, value => Change(s => s with { PositionLocked = value })));
        panel.Children.Add(ToggleRow(T("鼠标悬停信息", "Hover details"), T("显示额度、重置时间与数据源", "Quota, reset and source"),
            _draft.HoverPreviewEnabled, value => Change(s => s with { HoverPreviewEnabled = value })));
        panel.Children.Add(ToggleRow(T("贴边收起", "Edge collapse"), T("靠近四边自动吸附，闲置后收起；悬停展开", "Snap near any edge, collapse when idle; hover to expand"),
            _draft.EdgeAutoHide, value => Change(s => s with { EdgeAutoHide = value })));
        panel.Children.Add(ToggleRow(T("全局恢复快捷键", "Global recovery shortcut"), "Ctrl+Alt+Shift+Q",
            _draft.GlobalRecoveryShortcutEnabled, value => Change(s => s with { GlobalRecoveryShortcutEnabled = value })));
        panel.Children.Add(ToggleRow(T("减少动态效果", "Reduce motion"), T("保留快速淡入淡出", "Keep short fades only"),
            _draft.ReducedMotion, value => Change(s => s with { ReducedMotion = value })));
        return Scroll(panel);
    }

    private Control BuildNotificationsPage()
    {
        var panel = Page(T("通知", "Notifications"), T("提醒只在当前重置周期触发一次", "Alerts fire once per reset cycle"));
        panel.Children.Add(Row(T("额度恢复", "Quota reset"), T("确认额度恢复后，遵守免打扰设置", "After a confirmed reset; respects quiet hours"),
            Combo(Enum.GetValues<ResetOption>(), _draft.ResetNotificationEnabled ? ResetOption.Notify :
                _draft.ResetCelebrationEnabled ? ResetOption.Glow : ResetOption.Off,
                value => Change(s => s with { ResetCelebrationEnabled = value != ResetOption.Off,
                    ResetNotificationEnabled = value == ResetOption.Notify }))));
        panel.Children.Add(ToggleRow(T("启用额度提醒", "Enable alerts"), T("警告与严重阈值", "Warning and critical thresholds"),
            _draft.AlertsEnabled, value => Change(s => s with { AlertsEnabled = value })));
        panel.Children.Add(Row(T("警告阈值", "Warning threshold"), "2–100%", NumberSlider(2, 100, _draft.WarningThreshold,
            value => Change(s => s with { WarningThreshold = value }))));
        panel.Children.Add(Row(T("严重阈值", "Critical threshold"), "1–99%", NumberSlider(1, 99, _draft.CriticalThreshold,
            value => Change(s => s with { CriticalThreshold = value }))));
        panel.Children.Add(ToggleRow(T("免打扰时段", "Quiet hours"), T("默认 23:00–08:00", "Default 23:00–08:00"),
            _draft.QuietHoursEnabled, value => Change(s => s with { QuietHoursEnabled = value })));
        panel.Children.Add(Row(T("免打扰时间", "Quiet-hour range"), T("跨午夜时会自动识别", "Overnight ranges are supported"),
            TimeRange(_draft.QuietStartMinutes, _draft.QuietEndMinutes,
                start => Change(s => s with { QuietStartMinutes = start }),
                end => Change(s => s with { QuietEndMinutes = end }))));
        panel.Children.Add(ToggleRow(T("提醒声音", "Alert sound"), T("遵守免打扰设置", "Respects quiet hours"),
            _draft.AlertSoundEnabled, value => Change(s => s with { AlertSoundEnabled = value })));
        return Scroll(panel);
    }

    private Control BuildDataPage()
    {
        var panel = Page(T("数据与关于", "Data & About"), T("本地历史、迁移、更新与诊断", "Local history, migration, updates and diagnostics"));
        panel.Children.Add(BuildPricingStandardCard());
        panel.Children.Add(ToggleRow(T("自动同步费率", "Auto-sync rates"), T("每天从 GitHub 静默同步，不发送通知", "Sync from GitHub daily in the background, without notifications"),
            _draft.AutoUpdatePricing, value => Change(s => s with { AutoUpdatePricing = value })));
        panel.Children.Add(ToggleRow(T("记录本地 24 小时趋势", "Record 24-hour trend"), T("只保存在本机", "Stored locally only"),
            _draft.TrendRecordingEnabled, value => Change(s => s with { TrendRecordingEnabled = value })));
        panel.Children.Add(ToggleRow(T("启动时检查更新", "Check updates on startup"), T("最多每 24 小时一次", "At most once every 24 hours"),
            _draft.CheckForUpdatesOnStartup, value => Change(s => s with { CheckForUpdatesOnStartup = value })));
        panel.Children.Add(ActionRow(T("更新检查", "Updates"), "GitHub · yaozhihang2002/CodexQuotaPanel",
            (T("项目主页", "Project page"), () => OpenProjectRequested?.Invoke(this, EventArgs.Empty)),
            (T("立即检查", "Check now"), () => UpdateCheckRequested?.Invoke(this, EventArgs.Empty))));
        var maintenance = new StackPanel { Spacing = 10 };
        maintenance.Children.Add(ActionRow(T("设置导入与导出", "Import and export"), T("不包含位置、账户或历史数据", "Excludes position, account and history"),
            (T("导入…", "Import…"), () => ImportRequested?.Invoke(this, EventArgs.Empty)),
            (T("导出…", "Export…"), () => ExportRequested?.Invoke(this, EventArgs.Empty))));
        maintenance.Children.Add(ActionRow(T("维护工具", "Maintenance"), T("清除本地趋势与 Token 历史", "Clear local trends and token history"),
            (T("清除历史", "Clear history"), () => ClearHistoryRequested?.Invoke(this, EventArgs.Empty)),
            (T("复制诊断", "Copy diagnostics"), () => CopyDiagnosticsRequested?.Invoke(this, EventArgs.Empty)),
            (T("恢复默认", "Reset"), () => RestoreDefaultsRequested?.Invoke(this, EventArgs.Empty))));
        panel.Children.Add(new Expander { Header = T("维护与诊断", "Maintenance & diagnostics"), Content = maintenance,
            HorizontalAlignment = HorizontalAlignment.Stretch });
        panel.Children.Add(UiElements.Card(new StackPanel { Spacing = 5, Children =
        {
            UiElements.Text(T("关于此应用", "About this app"), 15, FontWeight.Bold, _palette.TextPrimary),
            UiElements.Text(T("本地读取额度数据 · 不上传会话内容 · MIT 开源", "Reads quota locally · no conversation upload · MIT licensed"), 11.5, FontWeight.Normal, _palette.TextSecondary),
            UiElements.Text("github.com/yaozhihang2002/CodexQuotaPanel", 11, FontWeight.SemiBold, _palette.TextMuted)
        }}, _palette));
        return Scroll(panel);
    }

    private Control BuildPricingStandardCard()
    {
        _pricingStatus = UiElements.Text($"{T("费率核对", "Rates checked")} · {ApiCostEstimator.CurrentBasisDate}",
            10.5, FontWeight.SemiBold, _palette.Mint);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        header.Children.Add(new StackPanel { Spacing = 2, Children =
        {
            UiElements.Text(T("API 等价估算", "API-equivalent estimate"), 15,
                FontWeight.Bold, _palette.TextPrimary),
            _pricingStatus
        }});
        var official = UiElements.Button(T("查看官方价格", "Official pricing"), _palette);
        official.MinHeight = 34;
        official.Padding = new Thickness(12, 6);
        official.Click += (_, _) => OpenPricingRequested?.Invoke(this, EventArgs.Empty);
        _pricingUpdate = UiElements.Button(T("同步费率", "Sync rates"), _palette);
        _pricingUpdate.Click += (_, _) => PricingUpdateRequested?.Invoke(this, EventArgs.Empty);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { _pricingUpdate, official } };
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var explanation = new StackPanel { Spacing = 5, Children =
        {
            UiElements.Text(T(
                    "按 OpenAI 公开 API 价格换算本机 Token，仅供参考；不是订阅账单或实际扣费。",
                    "Local tokens are converted using public OpenAI API prices for reference, not as a subscription bill or actual charge."),
                10.5, FontWeight.Normal, _palette.TextSecondary),
            UiElements.Text(T(
                    "Auto-review 沿用历史模型映射；未知模型只保留 Token，不计入美元合计。",
                    "Auto-review retains its historical model mapping; unknown models keep tokens but are excluded from the USD total."),
                10.5, FontWeight.Normal, _palette.TextSecondary)
        }};
        return UiElements.Card(new StackPanel { Spacing = 10, Children = { header, explanation } },
            _palette, new Thickness(16, 13));
    }

}
