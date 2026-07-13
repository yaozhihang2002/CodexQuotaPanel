using System.Globalization;

namespace CodexQuotaPanel;

internal enum AppLanguage
{
    SimplifiedChinese = 0,
    English = 1
}

internal static class L10n
{
    public static AppLanguage Current { get; private set; } = AppLanguage.SimplifiedChinese;
    public static bool IsChinese => Current == AppLanguage.SimplifiedChinese;
    public static CultureInfo Culture => IsChinese ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.GetCultureInfo("en-US");

    public static void SetLanguage(AppLanguage language) => Current = language;
    public static string Pick(string chinese, string english) => IsChinese ? chinese : english;

    public static string AppTitle => Pick("Codex 额度信号", "Codex Quota Signal");
    public static string AppAccessible => Pick("Codex 额度实时面板", "Codex live quota panel");
    public static string Brand => Pick("CODEX · 额度", "CODEX · QUOTA");
    public static string LiveRateLimits => Pick("实时额度限制", "LIVE RATE LIMITS");
    public static string ConnectingBadge => Pick("连接中", "CONNECTING");
    public static string PlanSuffix => Pick("方案", "PLAN");
    public static string WindowSection => Pick("额度窗口", "WINDOWS");
    public static string Credits => Pick("点数", "CREDITS");
    public static string LiveRpc => Pick("实时 RPC", "LIVE RPC");
    public static string LocalLive => Pick("本地实时", "LOCAL LIVE");
    public static string LocalShort => Pick("本地", "LOCAL");
    public static string Trend24Hours => Pick("24小时趋势", "24H TREND");
    public static string RingSettingsEyebrow => Pick("窗口与颜色", "WINDOW + COLOR");
    public static string AlertSettingsEyebrow => Pick("阈值与免打扰", "THRESHOLDS + QUIET HOURS");
    public static string OpacitySettingsEyebrow => Pick("滑动或输入 · 30–100%", "SLIDE OR TYPE · 30–100%");
    public static string ExpandDetails => Pick("展开额度详情", "Open quota details");
    public static string CollapseOrb => Pick("收起为悬浮球", "Collapse to quota orb");
    public static string Refresh => Pick("刷新", "Refresh");
    public static string RefreshNow => Pick("立即刷新", "Refresh now");
    public static string OfficialHelp => Pick("官方额度说明", "Official quota guide");
    public static string Exit => Pick("退出", "Exit");
    public static string AlwaysOnTop => Pick("悬浮窗始终置顶", "Always on top");
    public static string Startup => Pick("开机自动启动", "Start with Windows");
    public static string ClickThrough => Pick("悬浮球鼠标穿透", "Orb mouse click-through");
    public static string ClickThroughHint => Pick("开启后悬浮球不响应鼠标；可从托盘菜单关闭", "The orb ignores the mouse; disable this from the tray menu");
    public static string HoverPreview => Pick("悬停速览", "Hover preview");
    public static string HoverPreviewHint => Pick("鼠标停留在悬浮球上时显示重置倒计时", "Hover over the orb to see reset countdowns");
    public static string Language => Pick("语言 / Language", "Language / 语言");
    public static string Chinese => "简体中文";
    public static string English => "English";

    public static string Settings => Pick("设置", "Settings");
    public static string SettingsTitle => Pick("Codex 额度面板设置", "Codex Quota Panel settings");
    public static string SettingsSubtitle => Pick("让悬浮球更安静、更顺手，也更容易找回", "Tune the orb, interactions, alerts, and local data");
    public static string SettingsGeneral => Pick("常规", "General");
    public static string SettingsAppearance => Pick("外观", "Appearance");
    public static string SettingsInteraction => Pick("交互", "Interaction");
    public static string SettingsNotifications => Pick("通知", "Notifications");
    public static string SettingsDataAbout => Pick("数据与关于", "Data & About");
    public static string Save => Pick("保存并应用", "Save & apply");
    public static string Cancel => Pick("取消", "Cancel");
    public static string Edit => Pick("编辑…", "Edit…");
    public static string SettingsUnsavedState => Pick("即时预览 · 尚未保存", "Live preview · Not saved yet");
    public static string SettingsSavedState => Pick("所有更改已保存", "All changes saved");
    public static string GeneralIntro => Pick("启动方式和界面语言", "Startup behavior and display language");
    public static string StartWithWindows => Pick("随 Windows 启动", "Start with Windows");
    public static string StartWithWindowsHint => Pick("登录后在后台启动额度面板", "Launch the quota panel after you sign in");
    public static string StartupBehavior => Pick("启动后显示", "Show after startup");
    public static string StartupRestore => Pick("恢复上次状态", "Restore last state");
    public static string StartupOrb => Pick("悬浮球", "Quota orb");
    public static string StartupDetails => Pick("详情面板", "Details panel");
    public static string StartupTray => Pick("仅托盘", "Tray only");
    public static string InterfaceLanguage => Pick("界面语言", "Display language");
    public static string LanguageRestartHint => Pick("立即预览，保存并应用后保持", "Preview immediately; keep it after Save & apply");
    public static string InterfaceTheme => Pick("界面主题", "Interface theme");
    public static string ThemeHint => Pick("跟随 Windows 应用颜色，或固定使用深色/浅色", "Follow the Windows app theme, or keep dark/light mode");
    public static string ThemeSystem => Pick("跟随系统", "Follow system");
    public static string ThemeDark => Pick("深色", "Dark");
    public static string ThemeLight => Pick("浅色", "Light");
    public static string AppearanceIntro => Pick("尺寸、透明度和双环样式", "Size, opacity, and dual-ring styling");
    public static string OrbSize => Pick("悬浮球尺寸", "Orb size");
    public static string OrbSizeSmall => Pick("小 · 72 px", "Small · 72 px");
    public static string OrbSizeStandard => Pick("标准 · 88 px", "Standard · 88 px");
    public static string OrbSizeLarge => Pick("大 · 104 px", "Large · 104 px");
    public static string OrbSizePreciseHint => Pick("拖动滑块或输入 56–192 px；每次调整都会即时预览", "Drag the slider or enter 56–192 px; every change previews immediately");
    public static string OrbSizePresetHint => Pick("迷你 64 · 标准 88 · 大 128 · 超大 192", "Mini 64 · Standard 88 · Large 128 · XL 192");
    public static string SettingsFontSize => Pick("设置与弹窗字体大小", "Settings & dialog font size");
    public static string SettingsFontSizeHint => Pick("拖动滑块或输入 80–150%；立即预览，保存后保持", "Drag the slider or enter 80–150%; previews immediately and persists after saving");
    public static string SettingsFontSizePresetHint => Pick("紧凑 80% · 默认 100% · 大字 125% · 超大 150%", "Compact 80% · Default 100% · Large 125% · XL 150%");
    public static string OrbOpacity => Pick("悬浮球不透明度", "Orb opacity");
    public static string DualRingDisplay => Pick("双环显示", "Dual-ring display");
    public static string LiveOrbPreview => Pick("悬浮球即时预览", "Live orb preview");
    public static string LiveOrbPreviewHint => Pick("尺寸与双环颜色会立即更新；透明度会应用到桌面悬浮球", "Size and ring colors update here immediately; opacity is applied to the desktop orb");
    public static string ConsumptionFlame => Pick("额度消耗火焰", "Consumption flame");
    public static string ConsumptionFlameHint => Pick("根据近期消耗速度改变火焰颜色与活跃度；可关闭以减少动态效果", "Changes flame color and activity based on recent usage; turn it off to reduce motion");
    public static string FlameStyle => Pick("火焰风格", "Flame style");
    public static string FlameStyleHint => Pick("选择克制余烬、流体火焰或趣味像素效果", "Choose a restrained ember, fluid flame, or playful pixel effect");
    public static string FlameStyleEmber => Pick("简约余烬", "Minimal ember");
    public static string FlameStyleFluid => Pick("流体火焰", "Fluid flame");
    public static string FlameStylePixel => Pick("像素火焰", "Pixel flame");
    public static string InteractionIntro => Pick("拖动、穿透和快速找回", "Dragging, click-through, and quick recovery");
    public static string PositionLock => Pick("锁定悬浮球位置", "Lock orb position");
    public static string PositionLockHint => Pick("防止误拖动，但仍可正常点击", "Prevents accidental dragging while keeping clicks enabled");
    public static string SnapToEdge => Pick("拖动后吸附屏幕边缘", "Snap to screen edge after dragging");
    public static string SnapToEdgeHint => Pick("仅在释放时靠近屏幕边缘吸附；拖动时按住 Shift 可临时跳过", "Snaps only when released near a screen edge; hold Shift while dragging to skip it temporarily");
    public static string GlobalHotKey => Pick("启用全局显示/隐藏快捷键", "Enable global show/hide shortcut");
    public static string GlobalHotKeyHint => Pick("即使开启鼠标穿透，也能从键盘找回悬浮球", "Recover the orb from the keyboard, even with click-through enabled");
    public static string MoveToCurrentDisplay => Pick("移到当前显示器", "Move to current display");
    public static string MoveToCurrentDisplayHint => Pick("把悬浮球移回鼠标所在屏幕", "Move the orb to the display containing the pointer");
    public static string NotificationIntro => Pick("额度提醒、免打扰和提示音", "Quota thresholds, quiet hours, and sound");
    public static string AlertSound => Pick("额外播放提示音", "Play an extra alert sound");
    public static string AlertSoundHint => Pick("显示额度通知时额外播放一次系统提示音", "Play one extra system sound when a quota notification is shown");
    public static string DataIntro => Pick("本地趋势、恢复工具和版本信息", "Local trends, recovery tools, and version details");
    public static string TrendRecording => Pick("记录本地 24 小时趋势", "Record local 24-hour trends");
    public static string TrendRecordingHint => Pick("数据只保存在本机，可随时清除", "Stored only on this device and can be cleared anytime");
    public static string ClearHistory => Pick("清除趋势历史", "Clear trend history");
    public static string ClearHistoryConfirm => Pick("确定清除本机保存的全部趋势历史吗？此操作无法撤销。", "Clear all locally stored trend history? This cannot be undone.");
    public static string RestoreDefaults => Pick("恢复默认设置", "Restore defaults");
    public static string RestoreDefaultsConfirm => Pick("确定将面板设置恢复为默认值吗？你仍可在保存前取消。", "Restore the panel settings to defaults? You can still cancel before saving.");
    public static string AboutThisApp => Pick("关于此应用", "About this app");
    public static string LocalPrivacyNote => Pick("本地读取额度数据 · 不上传会话内容 · MIT 开源", "Reads quota data locally · Does not upload session content · MIT licensed");
    public static string VersionLabel => Pick("版本", "Version");
    public static string ReleaseNotesTitle => Pick("更新说明", "Release notes");
    public static string PreReleaseLabel => Pick("预发布", "PRE-RELEASE");
    public static string ReleaseNotesSummary => Pick(
        "首个公开预览版：修复安装快捷方式 Logo，完善中英安装、提醒排版、悬浮球动画与显示稳定性。",
        "First public preview: fixes the installer shortcut logo and refines bilingual setup, alert layout, orb motion, and rendering stability.");
    public static string GitHubProject => Pick("GitHub 项目", "GitHub project");
    public static string OpenLinkFailed => Pick("无法打开链接，请复制后在浏览器中访问。", "Could not open the link. Copy it and open it in your browser.");
    public static string QuotaAlerts => Pick("额度提醒", "Quota alerts");
    public static string AlertsOff => Pick("已关闭", "Off");
    public static string AlertsSummary(int warning, int critical) => Pick($"警告 {warning}% · 严重 {critical}%", $"Warning {warning}% · Critical {critical}%");

    public static string OpacityTitle(int percent) => Pick($"悬浮球不透明度 · {percent}%", $"Orb opacity · {percent}%");
    public static string PreciseSettings => Pick("精确设置…", "Precise settings…");
    public static string PreciseSettingsHint => Pick("使用滑块或直接输入 30%–100%", "Use a slider or type 30%–100%");
    public static string OpacityPreset(int percent) => percent switch
    {
        100 => Pick("100% · 清晰", "100% · Solid"),
        85 => Pick("85% · 平衡", "85% · Balanced"),
        70 => Pick("70% · 轻透", "70% · Light"),
        55 => Pick("55% · 低干扰", "55% · Subtle"),
        _ => $"{percent}%"
    };

    public static string RingMenu(int outerMinutes, int innerMinutes) =>
        Pick($"环形显示 · {RingWindowCatalog.FormatShort(outerMinutes)} / {RingWindowCatalog.FormatShort(innerMinutes)}",
            $"Ring display · {RingWindowCatalog.FormatShort(outerMinutes)} / {RingWindowCatalog.FormatShort(innerMinutes)}");
    public static string AlertMenu(PanelPreferences preferences) => !preferences.AlertsEnabled
        ? Pick("额度提醒 · 已关闭", "Quota alerts · Off")
        : Pick($"额度提醒 · {preferences.WarningThreshold}% / {preferences.CriticalThreshold}%",
            $"Quota alerts · {preferences.WarningThreshold}% / {preferences.CriticalThreshold}%");

    public static string TightestWindow => Pick("最紧额度窗口", "Tightest quota window");
    public static string WaitingData => Pick("等待数据", "Waiting for data");
    public static string QuotaFull => Pick("额度已满", "Quota exhausted");
    public static string NearlyUsed => Pick("即将用尽", "Nearly exhausted");
    public static string WatchBalance => Pick("注意余量", "Watch remaining");
    public static string QuotaHealthy => Pick("额度充足", "Quota healthy");
    public static string ResetUnavailable => Pick("重置时间暂不可用", "Reset time unavailable");
    public static string ResetCreditExpiryUnavailable => Pick("重置卡到期时间暂不可用", "Reset-credit expiry unavailable");
    public static string WaitingQuotaEvent => Pick("等待 Codex 额度事件", "Waiting for Codex quota events");
    public static string Connecting => Pick("正在连接数据源", "Connecting to quota source");
    public static string NoSnapshot => Pick("尚无快照", "No snapshot yet");
    public static string NoSnapshotLong => Pick("尚无额度快照 · 正在等待 Codex 活动", "No quota snapshot · Waiting for Codex activity");
    public static string Remaining => Pick("剩余", "remaining");
    public static string WaitingSnapshot => Pick("等待快照", "Waiting for snapshot");
    public static string ResetUnknown => Pick("重置时间未知", "Reset time unknown");
    public static string WaitingRefresh => Pick("正在等待额度刷新", "Waiting for quota refresh");
    public static string TrendAccumulating => Pick("24H · 积累中", "24H · Collecting");
    public static string TemporarilyUnavailable => Pick("暂不可用", "Unavailable");
    public static string AvailableAgainHint => Pick(
        "窗口不可用时保留选择。\n灰色轨道表示暂无数据。",
        "Unavailable windows stay selected.\nGray track means no data.");

    public static string FormatWindow(int? minutes)
    {
        if (minutes is null or <= 0) return Pick("额度窗口", "Quota window");
        var value = minutes.Value;
        if (Math.Abs(value - 300) <= 5) return Pick("5 小时窗口", "5-hour window");
        if (Math.Abs(value - 10080) <= 60) return Pick("7 天窗口", "7-day window");
        if (value >= 525600 && value % 525600 == 0)
        {
            var count = value / 525600;
            return IsChinese ? (count <= 1 ? "年度窗口" : $"{count} 年窗口") : $"{count}-year window";
        }
        if (value >= 43200 && value % 43200 == 0)
        {
            var count = value / 43200;
            return IsChinese ? (count <= 1 ? "月度窗口" : $"{count} 个月窗口") : $"{count}-month window";
        }
        if (value >= 10080 && value % 10080 == 0)
        {
            var count = value / 10080;
            return IsChinese ? $"{count} 周窗口" : $"{count}-week window";
        }
        if (value >= 1440 && value % 1440 == 0)
        {
            var count = value / 1440;
            return IsChinese ? $"{count} 天窗口" : $"{count}-day window";
        }
        if (value >= 60 && value % 60 == 0)
        {
            var count = value / 60;
            return IsChinese ? $"{count} 小时窗口" : $"{count}-hour window";
        }
        return IsChinese ? $"{value} 分钟窗口" : $"{value}-minute window";
    }

    public static string FormatAge(DateTimeOffset observedAt)
    {
        var age = DateTimeOffset.Now - observedAt;
        if (IsChinese)
            return age.TotalSeconds < 10 ? "刚刚" : age.TotalMinutes < 1 ? $"{(int)age.TotalSeconds} 秒前" :
                age.TotalHours < 1 ? $"{(int)age.TotalMinutes} 分钟前" : age.TotalDays < 1 ? $"{(int)age.TotalHours} 小时前" : $"{(int)age.TotalDays} 天前";
        return age.TotalSeconds < 10 ? "just now" : age.TotalMinutes < 1 ? $"{(int)age.TotalSeconds}s ago" :
            age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m ago" : age.TotalDays < 1 ? $"{(int)age.TotalHours}h ago" : $"{(int)age.TotalDays}d ago";
    }

    public static string FormatLocalDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString(IsChinese ? "M月d日 HH:mm" : "MMM d HH:mm", Culture);

    public static string SourceName(string source) => string.Equals(source, "App Server", StringComparison.Ordinal)
        ? "App Server"
        : Pick("本地会话", "Local session");

    public static bool IsDisconnectedStatus(string status) => status.Contains("失败") || status.Contains("不可用") ||
        status.Contains("断开") || status.Contains("未找到") || status.Contains("暂不可读");

    public static string TranslateStatus(string status)
    {
        if (IsChinese) return status;
        return status switch
        {
            "正在连接 Codex App Server" => "Connecting to Codex App Server",
            "App Server 实时连接" => "App Server live connection",
            "App Server 不可用，使用本地快照" => "App Server unavailable · Using local snapshots",
            "App Server 刷新失败，保留本地快照" => "App Server refresh failed · Keeping local snapshot",
            "App Server 已断开，使用本地快照" => "App Server disconnected · Using local snapshots",
            "未找到 Codex 会话目录" => "Codex session folder not found",
            "正在读取本地额度快照" => "Reading local quota snapshot",
            "本地会话监听中" => "Watching local Codex sessions",
            "等待 Codex 写入额度快照" => "Waiting for Codex to write a quota snapshot",
            "本地快照暂不可读" => "Local quota snapshot is temporarily unavailable",
            "正在刷新额度" => "Refreshing quota",
            _ => status
        };
    }
}
