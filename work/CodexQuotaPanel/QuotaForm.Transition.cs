using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace CodexQuotaPanel;

internal sealed partial class QuotaForm
{
    private void BeginTransition(Rectangle target, bool expanding)
    {
        _transition.Stop();
        DisposeTransitionOverlay();
        var preparationStartedAt = Environment.TickCount64;
        var previousPreview = _transitionPreview;
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        _transitionFrom = Bounds;
        _transitionTo = target;
        var fullBounds = expanding ? target : _transitionFrom;
        var orbBounds = expanding ? _transitionFrom : target;
        using (NativeRedrawScope.Suspend(this))
        {
            _transitionOrbPreview = CaptureOrbPreview();
            _transitionPreview = previousPreview is not null
                ? new Bitmap(previousPreview)
                : expanding
                    ? _cachedExpandedPreview is not null
                        ? new Bitmap(_cachedExpandedPreview)
                        : CaptureExpandedPreview(fullBounds)
                    : CaptureCurrentPreview();
        }
        if (_transitionPreview is not null && (!expanding || _cachedExpandedPreview is null))
        {
            ReplaceExpandedPreviewCache(new Bitmap(_transitionPreview));
            _transitionPreviewCacheDirty = false;
        }
        previousPreview?.Dispose();
        _transitionExpanding = expanding;
        _transitionAnchor = new PointF(
            orbBounds.Left + orbBounds.Width / 2f - fullBounds.Left,
            orbBounds.Top + orbBounds.Height / 2f - fullBounds.Top);
        UpdateTransitionVisualState(0d);

        try
        {
            _transitionOverlay = new LayeredTransitionOverlay(fullBounds, TopMost);
            _transitionOverlay.Present(
                _transitionPreview!, _transitionOrbPreview!, _transitionAnchor,
                _transitionShapeProgress, _transitionOrbScale);
            _transitionOverlay.Show();
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        {
            DisposeTransitionOverlay();
        }

        _animating = true;
        _collapsed = false;
        if (_transitionOverlay is not null)
        {
            base.Hide();
            Bounds = fullBounds;
            ApplyOrbPresentation();
            SetDetailControlsVisible(false);
            _orb.Visible = false;
            UpdateRegion();
        }
        else
        {
            using var redraw = NativeRedrawScope.Suspend(this);
            Bounds = fullBounds;
            ApplyOrbPresentation();
            SetDetailControlsVisible(false);
            _orb.Visible = false;
            UpdateRegion();
        }

        _transitionPreparationMs = Environment.TickCount64 - preparationStartedAt;
        _transitionPaintFrames = 0;
        _transitionMaxPaintGapMs = 0;
        _transitionLastPaintAt = 0;
        _transitionStartedAt = Environment.TickCount64;
        _transitionMetricsActive = true;
        if (_transitionOverlay is not null) RecordTransitionFrame();
        BeginHighResolutionAnimationClock();
        _transition.Start();
    }

    private void AnimateTransition(object? sender, EventArgs e)
    {
        var elapsed = Environment.TickCount64 - _transitionStartedAt;
        var progress = Math.Clamp(elapsed / (double)TransitionDurationMs, 0d, 1d);
        UpdateTransitionVisualState(progress);
        if (_transitionOverlay is not null)
        {
            try
            {
                _transitionOverlay.Present(
                    _transitionPreview!, _transitionOrbPreview!, _transitionAnchor,
                    _transitionShapeProgress, _transitionOrbScale);
                RecordTransitionFrame();
            }
            catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
            {
                DisposeTransitionOverlay();
                if (!Visible) Show();
                UpdateRegion();
                Invalidate();
            }
        }
        else
        {
            UpdateRegion();
            Invalidate();
        }
        if (progress >= 1d) CompleteTransition();
    }

    internal void SetTransitionPreviewProgress(double progress)
    {
        if (!_animating) return;
        _transition.Stop();
        EndHighResolutionAnimationClock();
        progress = Math.Clamp(progress, 0d, 1d);
        UpdateTransitionVisualState(progress);
        UpdateRegion();
        if (_transitionOverlay is not null)
            _transitionOverlay.Present(
                _transitionPreview!, _transitionOrbPreview!, _transitionAnchor,
                _transitionShapeProgress, _transitionOrbScale);
        Invalidate(true);
        Update();
    }

    private void UpdateTransitionVisualState(double progress)
    {
        progress = Math.Clamp(progress, 0d, 1d);
        if (_transitionExpanding)
        {
            var orbProgress = Math.Clamp(progress / TransitionOrbPhase, 0d, 1d);
            _transitionOrbScale = 1d - SmoothStep(orbProgress);
            var genieProgress = Math.Clamp(
                (progress - TransitionOrbPhase) / (1d - TransitionOrbPhase), 0d, 1d);
            _transitionShapeProgress = SmoothStep(genieProgress);
        }
        else
        {
            var genieEnd = 1d - TransitionOrbPhase;
            var genieProgress = Math.Clamp(progress / genieEnd, 0d, 1d);
            _transitionShapeProgress = SmoothStep(1d - genieProgress);
            var orbProgress = Math.Clamp(
                (progress - genieEnd) / TransitionOrbPhase, 0d, 1d);
            _transitionOrbScale = SmoothStep(orbProgress);
        }
    }

    private void CompleteTransition()
    {
        _transition.Stop();
        EndHighResolutionAnimationClock();
        _lastTransitionDurationMs = Environment.TickCount64 - _transitionStartedAt;
        _transitionMetricsActive = false;
        using (NativeRedrawScope.Suspend(this))
        {
            _animating = false;
            _transitionShapeProgress = _transitionExpanding ? 1d : 0d;
            if (_transitionExpanding)
            {
                _collapsed = false;
                Bounds = _transitionTo;
                _expandedBounds = Bounds;
                ApplyDetailLayoutForCurrentDpi();
                _orb.Visible = false;
                ApplyAdaptiveQuotaWindowLayout();
                SetDetailControlsVisible(true);
            }
            else
            {
                _collapsed = true;
                NormalizeCollapsedGeometry(_transitionTo.Location);
                SetDetailControlsVisible(false);
                _orb.Visible = true;
                _orb.Bounds = ClientRectangle;
                OrbPositionChanged?.Invoke(Location);
            }
            ApplyOrbPresentation();
            UpdateRegion();
        }
        if (!IsHidden && !Visible) Show();
        if (Visible)
        {
            _transitionOverlay?.BringToFront();
            Invalidate(true);
            Update();
            DwmFlush();
        }
        if (_transitionExpanding)
        {
            Activate();
            BringToFront();
        }
        DisposeTransitionOverlay();
        ReassertTopMostPreference();
        _transitionPreview?.Dispose();
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        if (!IsHidden)
            SetViewState(_transitionExpanding ? DetailsViewState : OrbViewState);
        if (_collapsed) QueueTransitionPreviewCacheRefresh();
    }

    private void RecordTransitionFrame()
    {
        var now = Environment.TickCount64;
        if (_transitionLastPaintAt > 0)
            _transitionMaxPaintGapMs = Math.Max(_transitionMaxPaintGapMs, now - _transitionLastPaintAt);
        _transitionLastPaintAt = now;
        _transitionPaintFrames++;
    }

    private void BeginHighResolutionAnimationClock()
    {
        if (_highResolutionTimerActive) return;
        _highResolutionTimerActive = TimeBeginPeriod(1) == 0;
    }

    private void EndHighResolutionAnimationClock()
    {
        if (!_highResolutionTimerActive) return;
        TimeEndPeriod(1);
        _highResolutionTimerActive = false;
    }

    private void DisposeTransitionOverlay()
    {
        if (_transitionOverlay is null) return;
        _transitionOverlay.Close();
        _transitionOverlay.Dispose();
        _transitionOverlay = null;
    }

    private void SetCollapsedInstant(Rectangle? bounds = null)
    {
        var restoreVisible = _animating && !IsHidden;
        _transition?.Stop();
        EndHighResolutionAnimationClock();
        DisposeTransitionOverlay();
        _transitionPreview?.Dispose();
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        var location = bounds?.Location ?? Location;
        _animating = false;
        _collapsed = true;
        NormalizeCollapsedGeometry(location);
        ApplyAdaptiveQuotaWindowLayout();
        ApplyOrbPresentation();
        SetDetailControlsVisible(false);
        _orb.Visible = true;
        _orb.Bounds = ClientRectangle;
        UpdateRegion();
        Invalidate(true);
        if (restoreVisible && !Visible) Show();
        MarkTransitionPreviewCacheDirty();
    }

    private void SetExpandedInstant(Rectangle bounds)
    {
        var restoreVisible = _animating && !IsHidden;
        _transition.Stop();
        EndHighResolutionAnimationClock();
        DisposeTransitionOverlay();
        _transitionPreview?.Dispose();
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        Bounds = bounds;
        _animating = false;
        _collapsed = false;
        _expandedBounds = Bounds;
        ApplyDetailLayoutForCurrentDpi();
        ApplyAdaptiveQuotaWindowLayout();
        ApplyOrbPresentation();
        _orb.Visible = false;
        SetDetailControlsVisible(true);
        UpdateRegion();
        Invalidate(true);
        if (restoreVisible && !Visible) Show();
    }

    private void SetDetailControlsVisible(bool visible)
    {
        foreach (Control control in Controls)
            if (!ReferenceEquals(control, _orb)) control.Visible = visible;
        if (visible) ApplyAdaptiveQuotaRowVisibility(true);
    }

    private void NormalizeCollapsedGeometry(Point location)
    {
        var probeSide = ScaledOrbSize().Width;
        var targetScreen = DisplayPlacement.SelectScreen(
            new Rectangle(location, new Size(probeSide, probeSide)));
        var targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
        var side = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        SuspendLayout();
        ClientSize = new Size(side, side);
        Location = location;
        _orb.Bounds = ClientRectangle;
        _collapsedBounds = Bounds;
        ResumeLayout(performLayout: false);
        Debug.Assert(ClientSize.Width == ClientSize.Height);
        Debug.Assert(_orb.Bounds == ClientRectangle);
    }

    private void ApplyOrbPresentation()
    {
        var desiredBackground = _collapsed && !_animating
            ? _orb.WindowBackdropColor
            : UiPalette.Canvas;
        if (BackColor.ToArgb() != desiredBackground.ToArgb())
            BackColor = desiredBackground;
        Opacity = _collapsed && !_animating ? _orbOpacityPercent / 100d : 1d;
        if (!IsHandleCreated) return;
        UpdateStyles();
        if (IsClickThroughActive)
        {
            var alpha = (byte)Math.Round(_orbOpacityPercent * 255d / 100d);
            SetLayeredWindowAttributes(Handle, 0, alpha, LwaAlpha);
        }
    }

    private void SetViewState(int state)
    {
        if (_viewState == state) return;
        _viewState = state;
        ViewStateChanged?.Invoke(state);
    }

    private void QueueEnsureVisible()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() => EnsureVisibleOnCurrentDisplays());
        }
        catch (InvalidOperationException)
        {
            // The native window can disappear between receiving a system message
            // and queuing the UI-thread recovery callback during shutdown.
        }
    }

    private void NormalizeStoredCollapsedBounds()
    {
        var probeSize = ScaledOrbSize();
        var candidate = _collapsedBounds.IsEmpty
            ? new Point(Bounds.Right - probeSize.Width, Bounds.Bottom - probeSize.Height)
            : _collapsedBounds.Location;
        var location = ClampOrbLocation(candidate);
        var screen = DisplayPlacement.SelectScreen(new Rectangle(location, probeSize));
        var targetDpi = DisplayPlacement.GetEffectiveDpi(screen, DeviceDpi);
        var side = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        _collapsedBounds = new Rectangle(location, new Size(side, side));
    }

    private static void AssignContextMenu(Control root, ContextMenuStrip menu)
    {
        root.ContextMenuStrip = menu;
        foreach (Control child in root.Controls)
            AssignContextMenu(child, menu);
    }

    private Rectangle ClampToWorkingArea(Rectangle bounds)
    {
        var area = DisplayPlacement.SelectScreen(bounds).WorkingArea;
        return ClampToArea(bounds, area);
    }

    private static Rectangle ClampToArea(Rectangle bounds, Rectangle area) =>
        DisplayPlacement.ClampToArea(bounds, area);

    private Point ClampOrbLocation(Point location)
    {
        var probeSide = ScaledOrbSize().Width;
        var target = new Rectangle(location, new Size(probeSide, probeSide));
        var screen = DisplayPlacement.SelectScreen(target);
        var targetDpi = DisplayPlacement.GetEffectiveDpi(screen, DeviceDpi);
        var side = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var area = screen.WorkingArea;
        return new Point(
            Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - side)),
            Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - side)));
    }

    private Point SnapOrbLocationToNearbyEdge(Point location)
    {
        var probeSide = ScaledOrbSize().Width;
        var bounds = new Rectangle(location, new Size(probeSide, probeSide));
        var screen = DisplayPlacement.SelectScreen(bounds);
        var targetDpi = DisplayPlacement.GetEffectiveDpi(screen, DeviceDpi);
        var side = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var area = screen.WorkingArea;
        location = new Point(
            Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - side)),
            Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - side)));

        var leftDistance = Math.Abs(location.X - area.Left);
        var rightDistance = Math.Abs(area.Right - (location.X + side));
        var topDistance = Math.Abs(location.Y - area.Top);
        var bottomDistance = Math.Abs(area.Bottom - (location.Y + side));
        var threshold = DisplayPlacement.ScaleLogicalPixels(SnapThresholdLogicalPixels, targetDpi);

        if (Math.Min(leftDistance, rightDistance) <= threshold)
            location.X = leftDistance <= rightDistance ? area.Left : area.Right - side;
        if (Math.Min(topDistance, bottomDistance) <= threshold)
            location.Y = topDistance <= bottomDistance ? area.Top : area.Bottom - side;
        return location;
    }

    internal Point ResolveReleasedOrbLocation(Point location, bool bypassSnap = false) =>
        _snapToEdge && !bypassSnap
            ? SnapOrbLocationToNearbyEdge(location)
            : ClampOrbLocation(location);

    private void MarkTransitionPreviewCacheDirty()
    {
        _transitionPreviewCacheDirty = true;
        QueueTransitionPreviewCacheRefresh();
    }

    private void QueueTransitionPreviewCacheRefresh()
    {
        if (!_transitionPreviewCacheDirty || _transitionPreviewRefreshQueued ||
            !IsHandleCreated || !Visible || !_collapsed || _animating || IsHidden || IsDisposed)
            return;

        _transitionPreviewRefreshQueued = true;
        try
        {
            BeginInvoke((Action)(() =>
            {
                _transitionPreviewRefreshQueued = false;
                if (!_transitionPreviewCacheDirty || !Visible || !_collapsed || _animating || IsHidden || IsDisposed)
                    return;
                RefreshTransitionPreviewCache();
            }));
        }
        catch (InvalidOperationException)
        {
            _transitionPreviewRefreshQueued = false;
        }
    }

    private void RefreshTransitionPreviewCache()
    {
        var orbBounds = Bounds;
        var expandedSize = ScaledSize(ExpandedPanelSize);
        var fullBounds = ClampToWorkingArea(new Rectangle(
            orbBounds.Right - expandedSize.Width,
            orbBounds.Bottom - expandedSize.Height,
            expandedSize.Width,
            expandedSize.Height));
        try
        {
            // Rendering with a hidden twin avoids hiding/resizing the live orb.
            // Removing the real HWND from DWM, even behind a temporary cover,
            // caused the intermittent dark/white flash seen between transitions.
            ReplaceExpandedPreviewCache(RenderExpandedPreviewOffscreen(fullBounds));
            _transitionPreviewCacheDirty = false;
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        {
            _transitionPreviewCacheDirty = true;
        }
    }

    private Bitmap RenderExpandedPreviewOffscreen(Rectangle fullBounds)
    {
        using var renderer = new QuotaForm
        {
            TopMost = false,
            Bounds = fullBounds
        };
        renderer.CreateControl();
        renderer.ConfigureRings(_ringConfiguration);
        renderer.SetHistory(_history);
        renderer.SetTokenCycleUsage(_dailyTokenUsage.Usage);
        if (_snapshot is not null) renderer.ApplySnapshot(_snapshot);
        if (_lastStatus is not null) renderer.SetStatus(_lastStatus);
        renderer.SetExpandedInstant(fullBounds);
        return renderer.CaptureCurrentPreview();
    }

    private void ReplaceExpandedPreviewCache(Bitmap preview)
    {
        var previous = _cachedExpandedPreview;
        _cachedExpandedPreview = preview;
        previous?.Dispose();
    }

    private Bitmap CaptureExpandedPreview(Rectangle fullBounds)
    {
        var savedBounds = Bounds;
        var savedCollapsed = _collapsed;
        var savedAnimating = _animating;
        var savedOrbVisible = _orb.Visible;
        SuspendLayout();
        try
        {
            _collapsed = false;
            _animating = false;
            Bounds = fullBounds;
            ApplyDetailLayoutForCurrentDpi();
            SetDetailControlsVisible(true);
            _orb.Visible = false;
            PerformLayout();
            UpdateRegion();
            return CaptureCurrentPreview();
        }
        finally
        {
            SetDetailControlsVisible(!savedCollapsed && !savedAnimating);
            _orb.Visible = savedOrbVisible;
            Bounds = savedBounds;
            _collapsed = savedCollapsed;
            _animating = savedAnimating;
            if (savedCollapsed && !savedAnimating)
                _orb.Bounds = ClientRectangle;
            UpdateRegion();
            ResumeLayout(performLayout: false);
        }
    }

    private Bitmap CaptureCurrentPreview()
    {
        var bitmap = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height),
            PixelFormat.Format32bppPArgb);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var bounds = new RectangleF(0.5f, 0.5f, bitmap.Width - 1f, bitmap.Height - 1f);
        var radius = Math.Min(ScaleLogicalPixels(16), Math.Min(bounds.Width, bounds.Height) / 2f);
        MaskRoundedCornersInPlace(bitmap, bounds, radius);
        return bitmap;
    }

    private Bitmap CaptureOrbPreview()
    {
        var savedBounds = _orb.Bounds;
        var savedVisible = _orb.Visible;
        var size = ScaledOrbSize();
        try
        {
            _orb.Visible = true;
            _orb.Bounds = new Rectangle(Point.Empty, size);
            _orb.PerformLayout();
            var bitmap = _orb.RenderTransparentPreview();
            MaskEllipseInPlace(bitmap, new RectangleF(1.25f, 1.25f, size.Width - 2.5f, size.Height - 2.5f));
            return bitmap;
        }
        finally
        {
            _orb.Bounds = savedBounds;
            _orb.Visible = savedVisible;
        }
    }

    private static void MaskRoundedCornersInPlace(Bitmap bitmap, RectangleF bounds, float radius)
    {
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            var edge = Math.Min(bitmap.Width / 2, Math.Max(1, (int)Math.Ceiling(radius + 1f)));
            var leftCenter = bounds.Left + radius;
            var rightCenter = bounds.Right - radius;
            var topCenter = bounds.Top + radius;
            var bottomCenter = bounds.Bottom - radius;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixelY = y + 0.5f;
                var centerY = pixelY < topCenter ? topCenter : pixelY > bottomCenter ? bottomCenter : float.NaN;
                if (float.IsNaN(centerY)) continue;
                var rowPointer = data.Scan0 + y * data.Stride;
                Marshal.Copy(rowPointer, row, 0, row.Length);
                for (var x = 0; x < edge; x++)
                {
                    ApplyCoverage(row, x, CircleCoverage(x + 0.5f, pixelY, leftCenter, centerY, radius));
                    var rightX = bitmap.Width - 1 - x;
                    ApplyCoverage(row, rightX, CircleCoverage(rightX + 0.5f, pixelY, rightCenter, centerY, radius));
                }
                Marshal.Copy(row, 0, rowPointer, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void MaskEllipseInPlace(Bitmap bitmap, RectangleF bounds)
    {
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            var centerX = bounds.Left + bounds.Width / 2f;
            var centerY = bounds.Top + bounds.Height / 2f;
            var radiusX = Math.Max(0.5f, bounds.Width / 2f);
            var radiusY = Math.Max(0.5f, bounds.Height / 2f);
            var edgeRadius = Math.Min(radiusX, radiusY);
            for (var y = 0; y < bitmap.Height; y++)
            {
                var rowPointer = data.Scan0 + y * data.Stride;
                Marshal.Copy(rowPointer, row, 0, row.Length);
                var normalizedY = (y + 0.5f - centerY) / radiusY;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var normalizedX = (x + 0.5f - centerX) / radiusX;
                    var normalizedDistance = Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                    var coverage = Math.Clamp((1d - normalizedDistance) * edgeRadius + 0.5d, 0d, 1d);
                    ApplyCoverage(row, x, coverage);
                }
                Marshal.Copy(row, 0, rowPointer, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static double CircleCoverage(float x, float y, float centerX, float centerY, float radius)
    {
        var deltaX = x - centerX;
        var deltaY = y - centerY;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return Math.Clamp(radius + 0.5d - distance, 0d, 1d);
    }

    private static void ApplyCoverage(byte[] row, int x, double coverage)
    {
        if (coverage >= 0.999d) return;
        var multiplier = Math.Clamp((int)Math.Round(coverage * 255d), 0, 255);
        var offset = x * 4;
        row[offset] = (byte)((row[offset] * multiplier + 127) / 255);
        row[offset + 1] = (byte)((row[offset + 1] * multiplier + 127) / 255);
        row[offset + 2] = (byte)((row[offset + 2] * multiplier + 127) / 255);
        row[offset + 3] = (byte)((row[offset + 3] * multiplier + 127) / 255);
    }

    private static GraphicsPath CreateGeniePath(Size size, PointF anchor, double appearance)
    {
        const int samples = 12;
        var points = new List<PointF>((samples + 1) * 2);
        for (var index = 0; index <= samples; index++)
        {
            var y = size.Height * index / (float)samples;
            points.Add(MapGeniePoint(size, anchor, new PointF(0f, y), appearance));
        }
        for (var index = samples; index >= 0; index--)
        {
            var y = size.Height * index / (float)samples;
            points.Add(MapGeniePoint(size, anchor, new PointF(size.Width, y), appearance));
        }

        var path = new GraphicsPath();
        path.AddPolygon(points.ToArray());
        path.CloseFigure();
        return path;
    }

    internal static void DrawGeniePreview(Graphics graphics, Bitmap preview, PointF anchor, double appearance)
    {
        if (appearance <= 0.012d) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;

        var stripHeight = Math.Max(5, preview.Height / 24);
        var size = preview.Size;
        for (var sourceY = 0; sourceY < preview.Height; sourceY += stripHeight)
        {
            var height = Math.Min(stripHeight + 1, preview.Height - sourceY);
            var topLeft = MapGeniePoint(size, anchor, new PointF(0f, sourceY), appearance);
            var topRight = MapGeniePoint(size, anchor, new PointF(preview.Width, sourceY), appearance);
            var bottomLeft = MapGeniePoint(size, anchor, new PointF(0f, sourceY + height), appearance);
            PointF[] destination = [topLeft, topRight, bottomLeft];
            graphics.DrawImage(preview, destination,
                new RectangleF(0f, sourceY, preview.Width, height),
                GraphicsUnit.Pixel);
        }
    }

    internal static void DrawTransitionOrbPreview(
        Graphics graphics,
        Bitmap preview,
        PointF anchor,
        double scale)
    {
        if (scale <= 0.015d) return;
        var width = Math.Max(1f, (float)(preview.Width * scale));
        var height = Math.Max(1f, (float)(preview.Height * scale));
        var destination = new RectangleF(
            anchor.X - width / 2f,
            anchor.Y - height / 2f,
            width,
            height);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(preview, destination);
    }

    internal static PointF MapGeniePoint(Size size, PointF anchor, PointF source, double appearance)
    {
        appearance = Math.Clamp(appearance, 0d, 1d);
        if (appearance <= 0d) return anchor;
        if (appearance >= 1d) return source;

        var verticalFactor = EaseOutCubic(appearance);
        var maximumVerticalDistance = Math.Max(1f, Math.Max(anchor.Y, size.Height - anchor.Y));
        var distanceFromAnchor = Math.Min(1d, Math.Abs(source.Y - anchor.Y) / maximumVerticalDistance);
        var localAppearance = Math.Clamp(appearance * (1d + distanceFromAnchor * 0.22d), 0d, 1d);
        var horizontalFactor = SmoothStep(localAppearance);
        var yRatio = size.Height <= 0 ? 0d : source.Y / size.Height;
        var twist = Math.Sin((yRatio * 1.7d + appearance * 0.55d) * Math.PI) *
                    Math.Sin(appearance * Math.PI) * 10d;

        return new PointF(
            (float)(anchor.X + (source.X - anchor.X) * horizontalFactor + twist),
            (float)(anchor.Y + (source.Y - anchor.Y) * verticalFactor));
    }

    private static Rectangle Interpolate(Rectangle from, Rectangle to, double amount) => new(
        (int)Math.Round(from.X + (to.X - from.X) * amount),
        (int)Math.Round(from.Y + (to.Y - from.Y) * amount),
        (int)Math.Round(from.Width + (to.Width - from.Width) * amount),
        (int)Math.Round(from.Height + (to.Height - from.Height) * amount));

    internal static Rectangle InterpolateLampTransition(Rectangle from, Rectangle to, double progress, bool expanding)
    {
        progress = Math.Clamp(progress, 0d, 1d);
        var amount = expanding
            ? EaseOutQuart(progress)
            : 1d - EaseOutQuart(1d - progress);
        var width = Lerp(from.Width, to.Width, amount);
        var height = Lerp(from.Height, to.Height, amount);
        var right = Lerp(from.Right, to.Right, amount);
        var bottom = Lerp(from.Bottom, to.Bottom, amount);
        return new Rectangle(right - width, bottom - height, width, height);
    }

    private static double EaseOutQuart(double value) => 1d - Math.Pow(1d - value, 4d);

    private static double EaseOutCubic(double value) => 1d - Math.Pow(1d - value, 3d);

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * (3d - 2d * value);
    }

    private static int Lerp(int from, int to, double amount) =>
        (int)Math.Round(from + (to - from) * amount);
}
