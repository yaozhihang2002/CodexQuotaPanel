using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

internal enum DockEdge { None, Left, Right, Top, Bottom }

public sealed partial class OrbWindow
{
    // Win32/Avalonia imposes a 32-DIP minimum client width. Render into that
    // exact footprint instead of leaving an invisible native margin.
    internal const double EdgeWidth = 32;
    // Win32 also enforces a taller minimum client height for horizontal windows.
    internal const double EdgeHorizontalHeight = 38;
    private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly EdgeQuotaControl _edge = new() { Cursor = new Cursor(StandardCursorType.Hand) };
    private PixelPoint? _expandedPosition;
    private long _lastInteraction = Environment.TickCount64;
    private long _recoveryStarted;
    private bool _edgeTransition;
    internal bool IsEdgeCollapsed => _expandedPosition is not null;
    internal DockEdge CollapsedEdge => IsEdgeCollapsed ? _edge.Edge : DockEdge.None;
    public PixelPoint ExpandedPosition => _expandedPosition ?? Position;

    private void InitializeEdgeBehavior()
    {
        PointerEntered += (_, _) => { if (!_edgeTransition) ExpandFromEdge(animate: true); };
        PointerExited += (_, _) => _lastInteraction = Environment.TickCount64;
        Opened += (_, _) => { _lastInteraction = Environment.TickCount64; _edgeTimer.Start(); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (IsVisible && !_closing) { _lastInteraction = Environment.TickCount64; _edgeTimer.Start(); }
            else { _edgeTimer.Stop(); FinishEdgeMotion(); }
        };
        Screens.Changed += OnEdgeScreensChanged;
        _edgeTimer.Tick += (_, _) =>
        {
            if (_closing || !IsVisible) { _edgeTimer.Stop(); return; }
            var elapsed = Environment.TickCount64 - _recoveryStarted;
            _orb.RecoveryHighlight = _recoveryStarted > 0 && elapsed < 2400 && !_settings.ReducedMotion
                ? Math.Sin(Math.PI * elapsed / 2400d) : 0;
            _orb.RecoveryLabel = _recoveryStarted > 0 && elapsed < 3500
                ? (_settings.Language == AppLanguage.SimplifiedChinese ? "额度已恢复" : "RESET") : "";
            if (Math.Abs(_edge.RecoveryHighlight - _orb.RecoveryHighlight) > .001)
            {
                _edge.RecoveryHighlight = _orb.RecoveryHighlight;
                _edge.InvalidateVisual();
            }
            if (Environment.TickCount64 - _lastInteraction >= 5000) TryCollapseToEdge();
        };
    }

    private void OnEdgeScreensChanged(object? sender, EventArgs e)
    {
        if (_closing) return;
        ExpandFromEdge();
        RestorePosition(Position.X, Position.Y);
    }

    internal bool TryCollapseToEdge()
    {
        if (_closing || !IsVisible || IsEdgeCollapsed || IsEdgeAnimating || !_settings.EdgeAutoHide ||
            _settings.ClickThrough || _moveMode || _pointerPressed || IsPointerOver) return false;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return false;
        var area = screen.WorkingArea;
        var size = (int)Math.Ceiling(_settings.OrbSize * screen.Scaling);
        var edge = SelectEdge(Position, size, area, 18 * screen.Scaling);
        if (edge == DockEdge.None) return false;
        _expandedPosition = Position;
        _edge.Edge = edge;
        var horizontal = edge is DockEdge.Top or DockEdge.Bottom;
        var targetWidth = horizontal ? 76 : EdgeWidth;
        var targetHeight = horizontal ? EdgeHorizontalHeight : 76;
        var width = (int)Math.Ceiling(targetWidth * screen.Scaling);
        var height = (int)Math.Ceiling(targetHeight * screen.Scaling);
        var x = Math.Clamp(_expandedPosition.Value.X + (int)((_settings.OrbSize - targetWidth) * screen.Scaling / 2),
                area.X, Math.Max(area.X, area.Right - width));
        var y = Math.Clamp(_expandedPosition.Value.Y + (int)((_settings.OrbSize - targetHeight) * screen.Scaling / 2),
                area.Y, Math.Max(area.Y, area.Bottom - height));
        var targetPosition = new PixelPoint(edge == DockEdge.Left ? area.X : edge == DockEdge.Right ? area.Right - width : x,
            edge == DockEdge.Top ? area.Y : edge == DockEdge.Bottom ? area.Bottom - height : y);
        StartEdgeMotion(targetPosition, targetWidth, targetHeight, collapse: true, animate: !_settings.ReducedMotion);
        return true;
    }

    internal static DockEdge SelectEdge(PixelPoint position, int size, PixelRect area, double threshold)
    {
        var left = Math.Abs(position.X - area.X);
        var right = Math.Abs(area.Right - position.X - size);
        // Side edges win at corners; top/bottom remain available elsewhere.
        if (Math.Min(left, right) <= threshold) return left <= right ? DockEdge.Left : DockEdge.Right;
        var top = Math.Abs(position.Y - area.Y);
        var bottom = Math.Abs(area.Bottom - position.Y - size);
        return Math.Min(top, bottom) <= threshold ? top <= bottom ? DockEdge.Top : DockEdge.Bottom : DockEdge.None;
    }

    public void ExpandFromEdge(bool animate = false)
    {
        _lastInteraction = Environment.TickCount64;
        if (_expandedPosition is not { } anchor) return;
        if (animate && _edgeMotion is { Collapse: false }) return;
        StartEdgeMotion(anchor, _settings.OrbSize, _settings.OrbSize, collapse: false,
            animate: animate && !_settings.ReducedMotion);
    }

    private void SetSurfaceSize(double width, double height)
    {
        MinWidth = MinHeight = 0;
        MaxWidth = MaxHeight = double.PositiveInfinity;
        Width = width; Height = height;
        MinWidth = MaxWidth = width; MinHeight = MaxHeight = height;
    }

    private void UpdateEdgePresentation(QuotaPresentation presentation)
    {
        _edge.Primary = _orb.RemainingPercent;
        _edge.Secondary = _orb.SecondaryRemainingPercent;
        _edge.PrimaryColor = _orb.OuterRingColor;
        _edge.SecondaryColor = _orb.InnerRingColor;
        _edge.PrimaryLabel = _orb.PrimaryLabel;
        _edge.SecondaryLabel = _orb.SecondaryLabel;
        ToolTip.SetTip(_edge, _settings.HoverPreviewEnabled ? BuildToolTip(presentation) : null);
        _edge.InvalidateVisual();
    }

    public void CelebrateReset()
    {
        if (_closing || !IsVisible) return;
        // Preserve the user's compact mode; no unsolicited expansion or focus.
        _recoveryStarted = Environment.TickCount64;
        ToolTip.SetTip(_edge, _settings.Language == AppLanguage.SimplifiedChinese ? "额度已恢复" : "Quota reset");
    }

    public void StopEffects()
    {
        _closing = true;
        _edgeTimer.Stop();
        _edgeMotionTimer.Stop();
        _edgeMotion = null;
        Screens.Changed -= OnEdgeScreensChanged;
    }
}
