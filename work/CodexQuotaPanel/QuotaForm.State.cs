namespace CodexQuotaPanel;

internal sealed partial class QuotaForm
{
    public void ApplySnapshot(QuotaSnapshot snapshot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplySnapshot(snapshot));
            return;
        }

        _snapshot = snapshot;
        var remaining = snapshot.RemainingPercent;
        var color = UiPalette.ForRemaining(remaining);
        _ring.Remaining = remaining;
        _planLabel.Text = $"{FormatPlan(snapshot.PlanType)} {L10n.PlanSuffix}";
        _planLabel.ForeColor = color;
        _heroValue.Text = snapshot.IsBlocked ? L10n.QuotaFull :
            remaining <= 20 ? L10n.NearlyUsed :
            remaining <= 45 ? L10n.WatchBalance : L10n.QuotaHealthy;
        _heroValue.ForeColor = color;

        var tightest = snapshot.Buckets.OrderBy(bucket => bucket.RemainingPercent).FirstOrDefault();
        _heroLabel.Text = tightest is null ? L10n.TightestWindow : L10n.Pick(
            $"最紧 · {LimitRowControl.FormatWindow(tightest.WindowMinutes)}",
            $"Tightest · {LimitRowControl.FormatWindow(tightest.WindowMinutes)}");
        UpdateRunwayInsight(force: true);

        _primaryRow.SetBucket(snapshot.Primary);
        _secondaryRow.SetBucket(snapshot.Secondary);
        ApplyAdaptiveQuotaWindowLayout();
        UpdateHistoryRows();
        UpdateCredits(snapshot.Credits);

        var rpc = string.Equals(snapshot.Source, "App Server", StringComparison.Ordinal);
        _orb.SetSnapshot(snapshot, live: true);
        _sourcePill.Text = rpc ? L10n.LiveRpc : L10n.LocalLive;
        _sourcePill.PillColor = rpc ? UiPalette.Mint : UiPalette.Amber;
        _statusLabel.Text = rpc
            ? L10n.Pick("● 实时同步 · 每 60 秒校准", "● Live sync · Calibrates every 60s")
            : L10n.Pick("● 本地监听 · Codex 活动后更新", "● Local watch · Updates after Codex activity");
        _statusLabel.ForeColor = rpc ? UiPalette.Mint : UiPalette.Amber;
        if (snapshot.AdditionalLimitCount > 0)
            _statusLabel.Text += L10n.Pick($" · +{snapshot.AdditionalLimitCount} 组",
                $" · +{snapshot.AdditionalLimitCount} {(snapshot.AdditionalLimitCount == 1 ? "group" : "groups")}");
        if (rpc && !_applyingLanguage)
        {
            _lastStatus = null;
        }
        if (_lastStatus is not null && L10n.IsDisconnectedStatus(_lastStatus))
        {
            _statusLabel.Text = L10n.TranslateStatus(_lastStatus);
            _statusLabel.ForeColor = UiPalette.Amber;
            _orb.SetConnectionState(false);
        }
        if (_hoverPeek.Visible) _hoverPeek.SetData(snapshot, _ringConfiguration);
        TickDisplay();
        MarkTransitionPreviewCacheDirty();
    }

    public void SetStatus(string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(status));
            return;
        }
        _lastStatus = status;
        var disconnected = L10n.IsDisconnectedStatus(status);
        if (disconnected) _orb.SetConnectionState(false);
        if (_snapshot is null || disconnected)
        {
            _statusLabel.Text = L10n.TranslateStatus(status);
            _statusLabel.ForeColor = disconnected ? UiPalette.Amber : UiPalette.Muted;
        }
    }

    public void SetTopMostPreference(bool value)
    {
        _alwaysOnTopPreference = value;
        ReassertTopMostPreference();
    }

    public void ApplyLanguage()
    {
        Text = L10n.AppTitle;
        AccessibleName = L10n.AppAccessible;
        _heroValue.Font = UiPalette.Display(L10n.IsChinese ? 22.5f : 18f, FontStyle.Bold);
        _brandLabel.Text = L10n.Brand;
        _subtitleLabel.Text = L10n.LiveRateLimits;
        _closeButton.AccessibleName = L10n.CollapseOrb;
        _pinButton.AccessibleName = L10n.AlwaysOnTop;
        _toolTip.SetToolTip(_closeButton, L10n.CollapseOrb);
        _toolTip.SetToolTip(_pinButton, L10n.AlwaysOnTop);
        _refreshButton.Text = L10n.Refresh;
        _refreshButton.AccessibleName = L10n.RefreshNow;
        _toolTip.SetToolTip(_refreshButton, L10n.RefreshNow);
        _hideButton.Text = L10n.CollapseOrb;
        _hideButton.AccessibleName = L10n.CollapseOrb;
        _sectionTitle.Text = L10n.WindowSection;
        if (_snapshot is null)
        {
            _heroLabel.Text = L10n.TightestWindow;
            _heroValue.Text = L10n.WaitingData;
            _nextResetLabel.Text = L10n.WaitingQuotaEvent;
            _planLabel.Text = $"— {L10n.PlanSuffix}";
            _sourcePill.Text = L10n.ConnectingBadge;
            _statusLabel.Text = _lastStatus is null ? L10n.Connecting : L10n.TranslateStatus(_lastStatus);
            _statusLabel.ForeColor = _lastStatus is not null && L10n.IsDisconnectedStatus(_lastStatus)
                ? UiPalette.Amber
                : UiPalette.Muted;
            _freshnessLabel.Text = L10n.NoSnapshot;
        }
        else
        {
            _applyingLanguage = true;
            try { ApplySnapshot(_snapshot); }
            finally { _applyingLanguage = false; }
        }
        _primaryRow.SetBucket(_snapshot?.Primary);
        _secondaryRow.SetBucket(_snapshot?.Secondary);
        _dailyTokenUsage.ApplyLanguage();
        _tokenUsageDetails?.ApplyLanguage();
        ApplyAdaptiveQuotaWindowLayout();
        _ring.ApplyLanguage();
        _orb.ConfigureRings(_ringConfiguration);
        _hoverPeek.ApplyLanguage();
        UiPalette.ApplyTypography(_hoverPeek);
        if (_snapshot is not null && _hoverPeek.Visible) _hoverPeek.SetData(_snapshot, _ringConfiguration);
        UiPalette.ApplyTypography(this);
        Invalidate(true);
        MarkTransitionPreviewCacheDirty();
    }

    public void SetOrbOpacityPercent(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbOpacityPercent(value));
            return;
        }

        _orbOpacityPercent = PanelPreferenceManager.NormalizeOpacity(value);
        ApplyOrbPresentation();
    }

    public void SetOrbBackgroundColor(int? argb)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbBackgroundColor(argb));
            return;
        }

        _orb.SetBackgroundColor(argb);
        ApplyOrbPresentation();
        Invalidate(true);
        MarkTransitionPreviewCacheDirty();
    }

    public void ApplyTheme(UiPalette.Colors previousColors)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyTheme(previousColors));
            return;
        }

        UiPalette.ApplyTheme(this, previousColors);
        UiPalette.ApplyTheme(_hoverPeek, previousColors);
        _tokenUsageDetails?.ApplyTheme(previousColors);
        if (_snapshot is not null)
            ApplySnapshot(_snapshot);
        else
        {
            BackColor = UiPalette.Canvas;
            ForeColor = UiPalette.Text;
        }
        ApplyOrbPresentation();
        UpdateRegion();
        Invalidate(true);
        MarkTransitionPreviewCacheDirty();
    }

    public void SetOrbSize(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbSize(value));
            return;
        }

        var normalized = NormalizeOrbLogicalSize(value);
        var previewWasRunning = _orbResizePreview.Enabled;
        _orbResizePreview.Stop();
        if (_orbLogicalSize == normalized && !previewWasRunning) return;

        var previousOrbBounds = _collapsed && !_animating
            ? Bounds
            : _collapsedBounds.IsEmpty
                ? new Rectangle(Location, ScaledOrbSize())
                : _collapsedBounds;
        var center = new Point(
            previousOrbBounds.Left + previousOrbBounds.Width / 2,
            previousOrbBounds.Top + previousOrbBounds.Height / 2);

        _orbLogicalSize = normalized;
        var targetScreen = DisplayPlacement.SelectScreen(previousOrbBounds);
        var targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
        var orbSide = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var orbSize = new Size(orbSide, orbSide);
        var nextLocation = ClampOrbLocation(new Point(
            center.X - orbSize.Width / 2,
            center.Y - orbSize.Height / 2));
        if (_snapToEdge) nextLocation = SnapOrbLocationToNearbyEdge(nextLocation);

        if (_collapsed && !_animating)
        {
            var locationChanged = Location != nextLocation;
            NormalizeCollapsedGeometry(nextLocation);
            ApplyOrbPresentation();
            UpdateRegion();
            Invalidate(true);
            if (locationChanged) OrbPositionChanged?.Invoke(Location);
        }
        else
        {
            _collapsedBounds = new Rectangle(nextLocation, orbSize);
            _orbReturnLocation = nextLocation;
            _orb.Size = orbSize;
            _orb.Location = new Point(
                Math.Max(0, ClientSize.Width - orbSize.Width),
                Math.Max(0, ClientSize.Height - orbSize.Height));
        }
    }

    public void PreviewOrbSize(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => PreviewOrbSize(value));
            return;
        }

        var normalized = NormalizeOrbLogicalSize(value);
        if (_orbLogicalSize == normalized) return;
        if (!_collapsed || _animating || !Visible)
        {
            SetOrbSize(normalized);
            return;
        }

        var currentBounds = Bounds;
        var center = new Point(
            currentBounds.Left + currentBounds.Width / 2,
            currentBounds.Top + currentBounds.Height / 2);
        _orbLogicalSize = normalized;
        var targetScreen = DisplayPlacement.SelectScreen(currentBounds);
        var targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
        var orbSide = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var orbSize = new Size(orbSide, orbSide);
        var nextLocation = ClampOrbLocation(new Point(
            center.X - orbSize.Width / 2,
            center.Y - orbSize.Height / 2));
        if (_snapToEdge) nextLocation = SnapOrbLocationToNearbyEdge(nextLocation);

        _orbResizePreviewFrom = currentBounds;
        _orbResizePreviewTo = new Rectangle(nextLocation, orbSize);
        _orbResizePreviewStartedAt = Environment.TickCount64;
        _orbResizePreview.Start();
    }

    private void AnimateOrbResizePreview(object? sender, EventArgs e)
    {
        var elapsed = Environment.TickCount64 - _orbResizePreviewStartedAt;
        var progress = Math.Clamp(elapsed / (double)OrbResizePreviewDurationMs, 0d, 1d);
        var eased = 1d - Math.Pow(1d - progress, 3d);
        Bounds = Interpolate(_orbResizePreviewFrom, _orbResizePreviewTo, eased);
        if (progress < 1d) return;

        _orbResizePreview.Stop();
        var previousLocation = _collapsedBounds.Location;
        NormalizeCollapsedGeometry(_orbResizePreviewTo.Location);
        ApplyOrbPresentation();
        UpdateRegion();
        Invalidate(true);
        if (previousLocation != Location) OrbPositionChanged?.Invoke(Location);
    }

    public void SetPositionLocked(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetPositionLocked(value));
            return;
        }

        _positionLocked = value;
    }

    public void SetSnapToEdge(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetSnapToEdge(value));
            return;
        }

        _snapToEdge = value;
    }

    public void SetOrbClickThroughPreference(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbClickThroughPreference(value));
            return;
        }

        _orbClickThrough = value;
        if (value) HideHoverPreview();
        ApplyOrbPresentation();
    }

    public void SetHoverPreviewEnabled(bool value)
    {
        _hoverPreviewEnabled = value;
        if (!value) HideHoverPreview();
    }

    public void SetConsumptionFlameEnabled(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetConsumptionFlameEnabled(value));
            return;
        }

        _consumptionFlameEnabled = value;
        _orb.SetFlameAnimationEnabled(value);
        _orb.SetConsumptionIntensity(value ? ConsumptionIntensity : 0d);
    }

    public void SetConsumptionFlameStyle(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetConsumptionFlameStyle(value));
            return;
        }

        _orb.SetFlameStyle(value);
    }

    public void ConfigureRings(RingDisplayConfiguration configuration)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ConfigureRings(configuration));
            return;
        }
        _ringConfiguration = configuration;
        _orb.ConfigureRings(configuration);
        UpdateHistoryRows();
        if (_snapshot is not null && _hoverPeek.Visible) _hoverPeek.SetData(_snapshot, configuration);
        MarkTransitionPreviewCacheDirty();
    }

    public void SetHistory(IReadOnlyList<QuotaHistoryPoint> history)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetHistory(history));
            return;
        }
        _history = history;
        ConsumptionIntensity = CalculateConsumptionIntensity(history);
        _orb.SetConsumptionIntensity(_consumptionFlameEnabled ? ConsumptionIntensity : 0d);
        UpdateHistoryRows();
        UpdateRunwayInsight(force: true);
        MarkTransitionPreviewCacheDirty();
    }

    internal static double CalculateConsumptionIntensity(
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset? now = null) => QuotaConsumptionRate.Evaluate(history, now).Intensity;
}
