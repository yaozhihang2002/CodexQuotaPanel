using Avalonia;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace CodexQuota.UI.Avalonia;

internal sealed record OrbPlacementAnchor(PixelPoint Position, PixelRect Area, int Size, string Display)
{
    public PixelPoint Project(PixelRect area, int size)
    {
        static int Axis(int value, int oldStart, int oldLength, int oldSize, int start, int length, int newSize)
        {
            var fraction = Math.Clamp((value - oldStart) / (double)Math.Max(1, oldLength - oldSize), 0, 1);
            return start + (int)Math.Round(fraction * Math.Max(0, length - newSize));
        }
        return new(Axis(Position.X, Area.X, Area.Width, Size, area.X, area.Width, size),
            Axis(Position.Y, Area.Y, Area.Height, Size, area.Y, area.Height, size));
    }
}

public sealed partial class OrbWindow
{
    private readonly Dictionary<string, OrbPlacementAnchor> _placements = [];
    private readonly Dictionary<string, OrbPlacementAnchor> _contextAnchors = [];
    private OrbPlacementAnchor? _placementAnchor;
    private string _placementContext = "local";
    private bool _placementPending;
    private string? _settledPlacementKey;
    private string? _preparedPlacementKey;
    private long _placementStarted;
    private Task? _placementFrame;
    private long _placementFrameDeadline;
    internal bool IsPlacementConcealed => !_orb.IsVisible && !_edge.IsVisible;
    internal bool IsPlacementPending => _placementPending;
    private readonly DispatcherTimer _placementTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };

    private string PlacementKey => _placementContext + ":" + string.Join(";", Screens.All
        .Select(s => $"{s.Bounds}/{s.WorkingArea}/{s.Scaling:R}").OrderBy(s => s, StringComparer.Ordinal));

    public void RestoreRememberedPosition(double? x, double? y, string? displayId = null)
    {
        if (_placementPending && _placementAnchor is not null) return;
        if (!RestorePlacementAnchor()) RestorePosition(x, y, displayId);
    }

    public void SetPlacementContext(bool remote, bool displayChanged = false)
    {
        var context = remote ? "remote" : "local";
        if (_placementContext == context && !displayChanged) return;
        var contextChanged = _placementContext != context;
        _placementContext = context;
        QueuePlacementRestore(contextChanged);
    }

    private void InitializePlacement()
    {
        Opened += (_, _) => { if (_placementAnchor is null) RememberPlacement(); };
        _placementTimer.Tick += (_, _) =>
        {
            if (_pointerPressed) return;
            if (_closing) { _placementTimer.Stop(); return; }
            // RDP sends several broadcasts over multiple frames. Keep one
            // concealed episode instead of revealing between those broadcasts.
            var remaining = 2000 - (Environment.TickCount64 - _placementStarted);
            if (remaining > 0)
            {
                _placementTimer.Interval = TimeSpan.FromMilliseconds(remaining);
                return;
            }
            if (_placementFrame is null)
            {
                _preparedPlacementKey = PlacementKey;
                RestorePlacementAnchor();
                UpdateLayout();
                InvalidateVisual();
                _placementFrame = ElementComposition.GetElementVisual(this)?.Compositor
                    .RequestCompositionBatchCommitAsync().Rendered ?? Task.CompletedTask;
                _placementFrameDeadline = Environment.TickCount64 + 500;
                _placementTimer.Interval = TimeSpan.FromMilliseconds(16);
                return;
            }
            if (!_placementFrame.IsCompleted && Environment.TickCount64 < _placementFrameDeadline) return;
            if (_preparedPlacementKey != PlacementKey) { QueuePlacementRestore(); return; }
            _placementTimer.Stop();
            _placementFrame = null;
            _placementPending = false;
            _settledPlacementKey = _preparedPlacementKey;
            // Reveal only the content, never Show() the window: a user-hidden
            // orb must stay hidden if a display change finishes in the meantime.
            _orb.IsVisible = _edge.IsVisible = true;
        };
    }

    private void QueuePlacementRestore(bool contextChanged = false)
    {
        if (_closing) return;
        // Session, screen and polling monitors can report the same final layout
        // after it is already visible. These are not new placement changes.
        if (!_placementPending && _settledPlacementKey == PlacementKey) return;
        // Never read Position here: Windows may already have clamped it to a
        // temporary RDP surface. The last intentional anchor is authoritative.
        if (!_placementPending)
        {
            _placementStarted = Environment.TickCount64;
            _placementPending = true;
            // Layout keys may change while this window's actual footprint does
            // not. Do not blink for those unrelated monitor notifications.
            if (contextChanged || PlacementGeometryDiffers() || IsEdgeAnimating)
                _orb.IsVisible = _edge.IsVisible = false;
            FinishEdgeMotion();
        }
        _placementFrame = null;
        _lastInteraction = Environment.TickCount64;
        _placementTimer.Stop();
        _placementTimer.Interval = TimeSpan.FromMilliseconds(750);
        _placementTimer.Start();
    }

    private void RememberPlacement()
    {
        var position = ExpandedPosition;
        var screen = Screens.ScreenFromPoint(position) ?? Screens.Primary;
        if (screen is null) return;
        _placementAnchor = new(position, screen.WorkingArea,
            (int)Math.Ceiling(_settings.OrbSize * screen.Scaling), DisplayId(screen));
        _placements[PlacementKey] = _placementAnchor;
        _contextAnchors[_placementContext] = _placementAnchor;
        if (!_placementPending) _settledPlacementKey = PlacementKey;
    }

    private (PixelPoint Anchor, PixelPoint Position, double Width, double Height)? PlacementTarget()
    {
        var anchor = _placements.GetValueOrDefault(PlacementKey) ??
            _contextAnchors.GetValueOrDefault(_placementContext) ?? _placementAnchor;
        if (anchor is null) return null;
        var screen = Screens.All.FirstOrDefault(s => DisplayId(s) == anchor.Display) ?? Screens.Primary;
        if (screen is null) return null;
        var position = anchor.Project(screen.WorkingArea, (int)Math.Ceiling(_settings.OrbSize * screen.Scaling));
        if (IsEdgeCollapsed)
        {
            var compact = CollapsedGeometry(position, _edge.Edge, screen.WorkingArea, screen.Scaling, _settings.OrbSize);
            return (position, compact.Position, compact.Width, compact.Height);
        }
        return (position, position, _settings.OrbSize, _settings.OrbSize);
    }

    private bool PlacementGeometryDiffers() => PlacementTarget() is { } target &&
        (Position != target.Position || Width != target.Width || Height != target.Height);

    private bool RestorePlacementAnchor()
    {
        if (PlacementTarget() is not { } target) return false;
        // Placement is not a user request to expand. Preserve the compact
        // surface, edge and expanded anchor without invoking hover/animation.
        _edgeTransition = true;
        try
        {
            if (IsEdgeCollapsed) _expandedPosition = target.Anchor;
            if (Width != target.Width || Height != target.Height) SetSurfaceSize(target.Width, target.Height);
            if (Position != target.Position) Position = target.Position;
        }
        finally { _edgeTransition = false; }
        _lastInteraction = Environment.TickCount64;
        // No cache/settings writes: projected positions are temporary.
        return true;
    }
}
