namespace CodexQuotaPanel;

internal sealed partial class QuotaForm
{
    internal int VisibleQuotaRowCount => _snapshot is null
        ? 1
        : (_snapshot.Primary is null ? 0 : 1) + (_snapshot.Secondary is null ? 0 : 1);
    internal Size ExpandedLogicalSize => ExpandedPanelSize;
    internal DailyTokenUsageControl DailyTokenUsage => _dailyTokenUsage;

    public void SetTokenCycleUsage(TokenCycleUsage? usage)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetTokenCycleUsage(usage));
            return;
        }
        _dailyTokenUsage.SetUsage(usage);
        MarkTransitionPreviewCacheDirty();
    }

    private void ApplyAdaptiveQuotaWindowLayout()
    {
        var primaryAvailable = _snapshot?.Primary is not null;
        var secondaryAvailable = _snapshot?.Secondary is not null;
        var actualCount = (primaryAvailable ? 1 : 0) + (secondaryAvailable ? 1 : 0);
        _availableWindowCount = actualCount;

        if (_detailLogicalBounds.Count == 0)
        {
            ApplyAdaptiveQuotaRowVisibility(!_collapsed && !_animating);
            return;
        }

        var dual = actualCount >= 2;
        SetLogicalBounds(_primaryRow, new Rectangle(18, 224, 332, 70));
        SetLogicalBounds(_secondaryRow, new Rectangle(18, dual ? 302 : 224, 332, 70));
        var tokenY = dual ? 380 : 302;
        SetLogicalBounds(_dailyTokenUsage, new Rectangle(18, tokenY, 332, 96));
        SetLogicalBounds(_creditsLabel, new Rectangle(19, tokenY + 104, 331, 19));
        SetLogicalBounds(_statusLabel, new Rectangle(19, tokenY + 128, 331, 18));
        SetLogicalBounds(_freshnessLabel, new Rectangle(19, tokenY + 150, 331, 17));
        SetLogicalBounds(_refreshButton, new Rectangle(18, tokenY + 178, 84, 28));
        SetLogicalBounds(_hideButton, new Rectangle(108, tokenY + 178, 242, 28));
        _detailLayoutDpi = 0;
        ApplyDetailLayoutForCurrentDpi(force: true);
        ApplyAdaptiveQuotaRowVisibility(!_collapsed && !_animating);

        var desiredSize = ScaledSize(ExpandedPanelSize);
        if (!_animating)
        {
            if (_collapsed)
            {
                var anchor = Bounds.BottomRight();
                _expandedBounds = ClampToWorkingArea(new Rectangle(
                    anchor.X - desiredSize.Width,
                    anchor.Y - desiredSize.Height,
                    desiredSize.Width,
                    desiredSize.Height));
            }
            else if (ClientSize != desiredSize)
            {
                var anchor = Bounds.BottomRight();
                Bounds = ClampToWorkingArea(new Rectangle(
                    anchor.X - desiredSize.Width,
                    anchor.Y - desiredSize.Height,
                    desiredSize.Width,
                    desiredSize.Height));
                _expandedBounds = Bounds;
                UpdateRegion();
            }
        }

        if (_dailyTokenUsage.Usage is { } usage)
        {
            var selected = _snapshot is null ? null : TokenCycleSelector.Select(_snapshot);
            if (selected is null || selected.Value.ResetsAt != usage.ResetsAt ||
                selected.Value.WindowMinutes != usage.WindowMinutes)
                _dailyTokenUsage.SetUsage(null);
        }
        MarkTransitionPreviewCacheDirty();
    }

    private void ApplyAdaptiveQuotaRowVisibility(bool detailVisible)
    {
        var showPrimary = _snapshot?.Primary is not null || _snapshot is null ||
                          (_snapshot.Primary is null && _snapshot.Secondary is null);
        var showSecondary = _snapshot?.Secondary is not null;
        _primaryRow.Visible = detailVisible && showPrimary;
        _secondaryRow.Visible = detailVisible && showSecondary;
        _dailyTokenUsage.Visible = detailVisible;
    }

    private void SetLogicalBounds(Control control, Rectangle bounds)
    {
        _detailLogicalBounds[control] = bounds;
    }
}

internal static class RectangleExtensions
{
    public static Point BottomRight(this Rectangle rectangle) =>
        new(rectangle.Right, rectangle.Bottom);
}
