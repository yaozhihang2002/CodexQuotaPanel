using System.Diagnostics;

namespace CodexQuotaPanel;

internal sealed partial class SettingsForm
{
    private Panel BuildHeader()
    {
        var header = new SettingsHeaderPanel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(18, 9, 17, 8),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 3f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Mint,
            Margin = Padding.Empty
        }, 0, 0);

        var title = new SettingsBrandTitle
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Font = UiPalette.Display(14.5f, FontStyle.Bold),
            Margin = new Padding(11, 0, 8, 0),
            AccessibleName = L10n.SettingsTitle
        };
        layout.Controls.Add(title, 1, 0);

        var badge = MakeDockLabel("CODEX · SETTINGS", UiPalette.Mono(6.5f, FontStyle.Bold), UiPalette.Mint);
        badge.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(badge, 2, 0);
        header.Controls.Add(layout);
        return header;
    }

    private Panel BuildNavigation()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Surface,
            Padding = new Padding(8, 10, 8, 8)
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Surface,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        host.Controls.Add(flow);
        host.Controls.Add(new Panel
        {
            Dock = DockStyle.Right,
            Width = 1,
            BackColor = UiPalette.Border,
            Margin = Padding.Empty
        });

        AddNav(flow, L10n.SettingsGeneral, 0);
        AddNav(flow, L10n.SettingsAppearance, 1);
        AddNav(flow, L10n.SettingsInteraction, 2);
        AddNav(flow, L10n.SettingsNotifications, 3);
        AddNav(flow, L10n.SettingsDataAbout, 4);
        return host;
    }

    private void AddNav(FlowLayoutPanel flow, string text, int pageIndex)
    {
        var button = new SettingsNavButton
        {
            Text = text,
            Size = new Size(148, 38),
            Margin = new Padding(0, 0, 0, 4),
            AccessibleName = text
        };
        button.Click += (_, _) => SelectPage(pageIndex);
        _navigation.Add(button);
        flow.Controls.Add(button);
    }

    private static ResponsiveSettingsPage MakePage() => new();

    private Control BuildGeneralPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsGeneral, L10n.GeneralIntro));
        page.AddItem(MakeToggleRow(L10n.StartWithWindows, L10n.StartWithWindowsHint, _startupToggle));
        page.AddItem(MakeControlRow(L10n.StartupBehavior,
            L10n.Pick("选择悬浮球、详情、仅托盘或恢复上次状态", "Choose the orb, details, tray only, or restore last state"),
            BuildChoiceSelector(_startupViewCombo, columns: 2), rightColumnWidth: 334));
        page.AddItem(MakeControlRow(L10n.InterfaceLanguage, L10n.LanguageRestartHint,
            BuildChoiceSelector(_languageCombo, columns: 2), rightColumnWidth: 334));
        page.AddItem(MakeControlRow(L10n.InterfaceTheme, L10n.ThemeHint,
            BuildChoiceSelector(_themeCombo, columns: 3), rightColumnWidth: 334));
        return page;
    }

    private Control BuildAppearancePage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsAppearance, L10n.AppearanceIntro));
        page.AddItem(BuildOrbPreviewCard());
        page.AddItem(MakeToggleRow(L10n.AlwaysOnTop,
            L10n.Pick("让悬浮球和详情面板保持在其他窗口上方", "Keep the orb and details above other windows"), _topMostToggle));
        page.AddItem(MakeControlRow(L10n.OrbSize,
            L10n.OrbSizePreciseHint, BuildOrbSizeEditor(), rightColumnWidth: 258));
        page.AddItem(MakeControlRow(L10n.SettingsFontSize,
            L10n.SettingsFontSizeHint, BuildFontScaleEditor(), rightColumnWidth: 258));
        page.AddItem(MakeEditorRow(L10n.OrbOpacity,
            L10n.Pick("可使用滑块或直接输入精确数值", "Use a slider or enter an exact value"), _opacitySummary, EditOpacity));
        page.AddItem(MakeControlRow(L10n.OrbBackground, L10n.OrbBackgroundHint,
            BuildOrbBackgroundEditor(), rightColumnWidth: 334));
        page.AddItem(MakeEditorRow(L10n.DualRingDisplay,
            L10n.Pick("选择额度窗口并分别设置环形颜色", "Choose quota windows and a color for each ring"), _ringSummary, EditRings));
        page.AddItem(MakeControlRow(L10n.FlameStyle,
            L10n.FlameStyleHint, BuildChoiceSelector(_flameStyleCombo, columns: 3), rightColumnWidth: 334));
        page.AddItem(MakeToggleRow(L10n.ConsumptionFlame,
            L10n.ConsumptionFlameHint, _consumptionFlameToggle));
        return page;
    }

    private Control BuildOrbSizeEditor()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(244, 58),
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        layout.Controls.Add(_orbSizeSlider, 0, 0);
        layout.Controls.Add(_orbSizeInput, 1, 0);
        var presets = MakeDockLabel(L10n.OrbSizePresetHint, UiPalette.Body(7f), UiPalette.Faint);
        presets.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(presets, 0, 1);
        layout.SetColumnSpan(presets, 2);
        return layout;
    }

    private Control BuildFontScaleEditor()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(244, 58),
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        layout.Controls.Add(_fontScaleSlider, 0, 0);
        layout.Controls.Add(_fontScaleInput, 1, 0);
        var presets = MakeDockLabel(L10n.SettingsFontSizePresetHint, UiPalette.Body(7f), UiPalette.Faint);
        presets.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(presets, 0, 1);
        layout.SetColumnSpan(presets, 2);
        return layout;
    }

    private Control BuildOrbBackgroundEditor()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(320, 40),
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _orbBackgroundSummary.Dock = DockStyle.Fill;
        _orbBackgroundSummary.TextAlign = ContentAlignment.MiddleCenter;
        _orbBackgroundSummary.Margin = Padding.Empty;
        layout.Controls.Add(_orbBackgroundSummary, 0, 0);
        _orbBackgroundColorButton.Dock = DockStyle.Fill;
        _orbBackgroundColorButton.Margin = new Padding(4, 3, 6, 3);
        layout.Controls.Add(_orbBackgroundColorButton, 1, 0);
        _orbBackgroundDefaultButton.Dock = DockStyle.Fill;
        _orbBackgroundDefaultButton.Margin = new Padding(4, 3, 0, 3);
        layout.Controls.Add(_orbBackgroundDefaultButton, 2, 0);
        return layout;
    }

    private Control BuildOrbPreviewCard()
    {
        var card = new SettingsCard
        {
            Size = new Size(570, 176),
            Margin = new Padding(0, 0, 0, 10)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18, 12, 18, 12),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(MakeTextBlock(
            L10n.LiveOrbPreview,
            L10n.LiveOrbPreviewHint,
            minimumHeight: 142), 0, 0);

        var previewHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.SurfaceRaised,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(6)
        };
        _orbPreview = new QuotaOrbControl
        {
            Cursor = Cursors.Default,
            TabStop = false,
            Anchor = AnchorStyles.None
        };
        previewHost.Controls.Add(_orbPreview);
        previewHost.Resize += (_, _) => CenterOrbPreview(previewHost);
        layout.Controls.Add(previewHost, 1, 0);
        card.Controls.Add(layout);
        return card;
    }

    private void CenterOrbPreview(Control host)
    {
        if (_orbPreview is null) return;
        var viewport = host.DisplayRectangle;
        _orbPreview.Location = new Point(
            viewport.Left + Math.Max(0, (viewport.Width - _orbPreview.Width) / 2),
            viewport.Top + Math.Max(0, (viewport.Height - _orbPreview.Height) / 2));
    }

    private Control BuildInteractionPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsInteraction, L10n.InteractionIntro));
        page.AddItem(MakeToggleRow(L10n.PositionLock, L10n.PositionLockHint, _positionLockedToggle));
        page.AddItem(MakeToggleRow(L10n.SnapToEdge, L10n.SnapToEdgeHint, _snapToEdgeToggle));
        page.AddItem(MakeToggleRow(L10n.ClickThrough, L10n.ClickThroughHint, _clickThroughToggle));
        page.AddItem(MakeToggleRow(L10n.ClickThroughReminder, L10n.ClickThroughReminderHint, _clickThroughReminderToggle));
        page.AddItem(MakeToggleRow(L10n.HoverPreview, L10n.HoverPreviewHint, _hoverPreviewToggle));
        page.AddItem(MakeToggleRow(L10n.GlobalHotKey, L10n.GlobalHotKeyHint, _globalHotKeyToggle));

        var moveButton = MakeActionButton(L10n.MoveToCurrentDisplay, 150, primary: false);
        moveButton.Click += (_, _) => MoveToCurrentDisplayRequested?.Invoke();
        page.AddItem(MakeControlRow(L10n.MoveToCurrentDisplay, L10n.MoveToCurrentDisplayHint, moveButton));
        return page;
    }

    private Control BuildNotificationsPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsNotifications, L10n.NotificationIntro));
        page.AddItem(MakeEditorRow(L10n.QuotaAlerts,
            L10n.Pick("设置警告、严重阈值和免打扰时段", "Set warning and critical thresholds plus quiet hours"), _alertSummary, EditAlerts));
        page.AddItem(MakeToggleRow(L10n.AlertSound, L10n.AlertSoundHint, _alertSoundToggle));
        return page;
    }

    private Control BuildDataPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsDataAbout, L10n.DataIntro));
        page.AddItem(BuildVersionUpdatesCard());
        page.AddItem(MakeControlRow(
            L10n.SettingsTransfer,
            L10n.SettingsTransferHint,
            BuildSettingsTransferControl(),
            318));
        page.AddItem(MakeToggleRow(L10n.TrendRecording, L10n.TrendRecordingHint, _trendRecordingToggle));

        var clearButton = MakeActionButton(L10n.ClearHistory, 138, primary: false);
        clearButton.Click += (_, _) => RequestClearHistory();
        page.AddItem(MakeControlRow(L10n.ClearHistory,
            L10n.Pick("删除本机保存的趋势点，不影响额度数据源", "Deletes saved trend points without affecting the quota source"), clearButton));

        var diagnosticsButton = MakeActionButton(L10n.Pick("复制诊断", "Copy diagnostics"), 138, primary: false);
        diagnosticsButton.Click += (_, _) => CopyDiagnostics();
        page.AddItem(MakeControlRow(
            L10n.Pick("脱敏诊断信息", "Sanitized diagnostics"),
            L10n.Pick("仅包含版本、系统、数据源和趋势状态", "Includes only version, system, source, and trend status"),
            diagnosticsButton));

        var resetButton = MakeActionButton(L10n.RestoreDefaults, 138, primary: false);
        resetButton.Click += (_, _) => StageReset();
        page.AddItem(MakeControlRow(L10n.RestoreDefaults,
            L10n.Pick("重置界面、交互、提醒和本地数据选项", "Reset appearance, interaction, alerts, and local-data options"), resetButton));

        var version = ProductVersionInfo.Current;
        var about = new SettingsCard { Size = new Size(570, 144), Margin = new Padding(0, 0, 0, 10) };
        var aboutLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 14),
            Margin = Padding.Empty
        };
        aboutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        aboutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        var aboutTitle = MakeDockLabel(L10n.AboutThisApp,
            UiPalette.Body(9f, FontStyle.Bold), UiPalette.Text);
        aboutTitle.Padding = new Padding(0, 0, 0, 4);
        aboutLayout.Controls.Add(aboutTitle, 0, 0);
        var sourceText = _snapshot is null
            ? L10n.Pick("数据源 · 等待连接", "Source · Waiting for connection")
            : L10n.Pick(
                $"数据源 · {L10n.SourceName(_snapshot.Source)} · 更新于 {_snapshot.ObservedAt.ToLocalTime():HH:mm:ss}",
                $"Source · {L10n.SourceName(_snapshot.Source)} · Updated {_snapshot.ObservedAt.ToLocalTime():HH:mm:ss}");
        var privacyLabel = MakeDockLabel(L10n.LocalPrivacyNote, UiPalette.Body(7.6f), UiPalette.Muted);
        privacyLabel.Padding = new Padding(0, 0, 0, 2);
        aboutLayout.Controls.Add(privacyLabel, 0, 1);
        aboutLayout.SetColumnSpan(privacyLabel, 2);
        var sourceLabel = MakeDockLabel(sourceText, UiPalette.Mono(6.7f, FontStyle.Bold), UiPalette.Faint);
        sourceLabel.Padding = new Padding(0, 0, 0, 3);
        aboutLayout.Controls.Add(sourceLabel, 0, 2);
        aboutLayout.SetColumnSpan(sourceLabel, 2);
        var versionLabel = MakeDockLabel($"{L10n.VersionLabel} {version}",
            UiPalette.Mono(7f, FontStyle.Bold), UiPalette.Mint);
        versionLabel.TextAlign = ContentAlignment.MiddleRight;
        versionLabel.Padding = new Padding(0, 0, 0, 4);
        aboutLayout.Controls.Add(versionLabel, 1, 0);
        about.Controls.Add(aboutLayout);
        page.AddItem(about);
        return page;
    }

    private Control BuildSettingsTransferControl()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(304, 38),
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var import = MakeActionButton(L10n.ImportSettings, 142, primary: false);
        import.Dock = DockStyle.Fill;
        import.Margin = new Padding(0, 3, 5, 3);
        import.Click += (_, _) => ImportSettings();
        layout.Controls.Add(import, 0, 0);

        var export = MakeActionButton(L10n.ExportSettings, 142, primary: false);
        export.Dock = DockStyle.Fill;
        export.Margin = new Padding(5, 3, 0, 3);
        export.Click += (_, _) => ExportSettings();
        layout.Controls.Add(export, 1, 0);
        return layout;
    }

    private Control BuildVersionUpdatesCard()
    {
        const string githubUrl = "https://github.com/yaozhihang2002/CodexQuotaPanel";

        var card = new SettingsCard
        {
            Size = new Size(570, 274),
            Margin = new Padding(0, 0, 0, 10),
            AccessibleName = L10n.VersionAndUpdates
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(18, 12, 18, 12),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 13f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        var version = ProductVersionInfo.Current;
        header.Controls.Add(MakeDockLabel($"{L10n.VersionAndUpdates} · v{version}",
            UiPalette.Body(9f, FontStyle.Bold), UiPalette.Text), 0, 0);
        var badge = new PillLabel
        {
            Text = L10n.PreReleaseLabel,
            PillColor = UiPalette.Mint,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 1, 0, 3)
        };
        header.Controls.Add(badge, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var summary = new ResponsiveTextLabel
        {
            Text = L10n.ReleaseNotesSummary,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UiPalette.Body(7.6f),
            ForeColor = UiPalette.Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 5, 0, 5),
            TextAlign = ContentAlignment.TopLeft,
            UseCompatibleTextRendering = false
        };
        layout.Controls.Add(summary, 0, 1);

        var github = BuildInfoLink(L10n.GitHubProject, "yaozhihang2002/CodexQuotaPanel", githubUrl);
        layout.Controls.Add(github, 0, 2);

        var separator = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Border,
            Margin = new Padding(0, 6, 0, 6),
            AccessibleRole = AccessibleRole.Separator
        };
        layout.Controls.Add(separator, 0, 3);

        var automaticUpdateHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        automaticUpdateHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        automaticUpdateHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
        automaticUpdateHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        automaticUpdateHeader.Controls.Add(MakeDockLabel(
            L10n.AutomaticUpdateChecks,
            UiPalette.Body(8.7f, FontStyle.Bold),
            UiPalette.Text), 0, 0);
        _checkUpdatesToggle.Anchor = AnchorStyles.None;
        _checkUpdatesToggle.Margin = Padding.Empty;
        automaticUpdateHeader.Controls.Add(_checkUpdatesToggle, 1, 0);
        layout.Controls.Add(automaticUpdateHeader, 0, 4);

        var updateHint = new ResponsiveTextLabel
        {
            Text = L10n.CheckUpdatesOnStartupHint,
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiPalette.Body(7.1f),
            ForeColor = UiPalette.Muted,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.TopLeft,
            UseCompatibleTextRendering = false
        };
        layout.Controls.Add(updateHint, 0, 5);

        var updateActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        updateActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        updateActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132f));
        updateActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _updateStatusLabel = MakeDockLabel(
            L10n.UpdateNotChecked,
            UiPalette.Body(7.1f, FontStyle.Bold),
            UiPalette.Faint);
        _updateStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _updateStatusLabel.Margin = new Padding(0, 0, 10, 0);
        updateActions.Controls.Add(_updateStatusLabel, 0, 0);
        _updateCheckButton = MakeActionButton(L10n.CheckNow, 124, primary: false);
        _updateCheckButton.Dock = DockStyle.Fill;
        _updateCheckButton.Margin = new Padding(0, 3, 0, 3);
        _updateCheckButton.Click += async (_, _) => await CheckForUpdatesAsync();
        updateActions.Controls.Add(_updateCheckButton, 1, 0);
        layout.Controls.Add(updateActions, 0, 6);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildInfoLink(string caption, string text, string target)
    {
        var block = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.SurfaceRaised,
            Padding = new Padding(10, 5, 10, 5)
        };
        var link = new BaselineSafeLinkLabel
        {
            Text = $"{caption}  \u00B7  {text}",
            PrefixText = $"{caption}  \u00B7  ",
            ProjectText = text,
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiPalette.Body(7.8f, FontStyle.Bold),
            ForeColor = UiPalette.Mint,
            LinkColor = UiPalette.Mint,
            ActiveLinkColor = UiPalette.Sky,
            VisitedLinkColor = UiPalette.Mint,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Cursor = Cursors.Hand,
            TabStop = true,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 2),
            UseMnemonic = false
        };
        link.LinkArea = new LinkArea(0, link.Text.Length);
        link.LinkClicked += (_, _) => OpenExternalLink(target);
        block.Controls.Add(link);
        return block;
    }

    private void OpenExternalLink(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, L10n.OpenLinkFailed, L10n.SettingsTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_checkingForUpdates || IsDisposed) return;
        var check = CheckForUpdatesRequested;
        if (check is null)
        {
            _updateStatusLabel.Text = L10n.UpdateUnavailable;
            _updateStatusLabel.ForeColor = UiPalette.Amber;
            return;
        }

        _checkingForUpdates = true;
        _updateCheckButton.Enabled = false;
        _updateStatusLabel.Text = L10n.UpdateChecking;
        _updateStatusLabel.ForeColor = UiPalette.Faint;
        try
        {
            var result = await check(_operationLifetime.Token);
            if (IsDisposed || _operationLifetime.IsCancellationRequested) return;
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when
                    result.ReleaseUri is not null && !string.IsNullOrWhiteSpace(result.LatestTag):
                    _updateStatusLabel.Text = L10n.UpdateAvailable(result.LatestTag);
                    _updateStatusLabel.ForeColor = UiPalette.Mint;
                    if (MessageBox.Show(
                            this,
                            L10n.OpenReleasePrompt(result.LatestTag),
                            L10n.CheckForUpdates,
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information,
                            MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                        OpenExternalLink(result.ReleaseUri.AbsoluteUri);
                    break;
                case UpdateCheckStatus.UpToDate:
                    _updateStatusLabel.Text = L10n.UpdateCurrent(result.CurrentVersion);
                    _updateStatusLabel.ForeColor = UiPalette.Mint;
                    break;
                default:
                    _updateStatusLabel.Text = L10n.UpdateUnavailable;
                    _updateStatusLabel.ForeColor = UiPalette.Amber;
                    MessageBox.Show(
                        this,
                        L10n.UpdateUnavailable,
                        L10n.CheckForUpdates,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
            }
        }
        catch (OperationCanceledException) when (_operationLifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _checkingForUpdates = false;
            if (!IsDisposed) _updateCheckButton.Enabled = true;
        }
    }

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog
        {
            Title = L10n.ImportSettings,
            Filter = L10n.SettingsFileFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!SettingsTransferService.TryImport(
                dialog.FileName,
                _savedPreferences,
                out var imported,
                out var failure))
        {
            ShowSettingsTransferFailure(failure);
            return;
        }

        var previous = _workingPreferences;
        var startupEnabled = StartupEnabled;
        _workingPreferences = imported;
        ApplyPreferencesToControls(_workingPreferences, startupEnabled);
        if (previous.ThemeMode != _workingPreferences.ThemeMode)
        {
            var previousColors = UiPalette.ResolveColors(previous.ThemeMode);
            UiPalette.SetTheme(_workingPreferences.ThemeMode);
            ApplyThemeToOpenForm(previousColors);
        }
        if (previous.Language != _workingPreferences.Language)
        {
            L10n.SetLanguage((AppLanguage)_workingPreferences.Language);
            ApplyLanguageToOpenForm();
        }
        RaisePreview();
        MessageBox.Show(
            this,
            L10n.ImportSettingsSuccess,
            L10n.SettingsTransfer,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ExportSettings()
    {
        UpdateFromDirectControls();
        using var dialog = new SaveFileDialog
        {
            Title = L10n.ExportSettings,
            Filter = L10n.SettingsFileFilter,
            AddExtension = true,
            DefaultExt = "json",
            FileName = $"CodexQuotaPanel-settings-{DateTime.Now:yyyyMMdd}.json",
            OverwritePrompt = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!SettingsTransferService.TryExport(dialog.FileName, SelectedPreferences, out var failure))
        {
            ShowSettingsTransferFailure(failure);
            return;
        }
        MessageBox.Show(
            this,
            L10n.ExportSettingsSuccess,
            L10n.SettingsTransfer,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowSettingsTransferFailure(SettingsTransferFailure failure)
    {
        var message = failure switch
        {
            SettingsTransferFailure.TooLarge => L10n.SettingsTransferTooLarge,
            SettingsTransferFailure.UnsupportedVersion => L10n.SettingsTransferUnsupported,
            SettingsTransferFailure.InvalidFormat => L10n.SettingsTransferInvalid,
            _ => L10n.SettingsTransferIoError
        };
        MessageBox.Show(
            this,
            message,
            L10n.SettingsTransfer,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
