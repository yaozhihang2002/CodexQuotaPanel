using System.Drawing.Drawing2D;

namespace CodexQuotaPanel;

internal sealed partial class QuotaForm
{
    public void RestoreOrbLocation(int? x, int? y)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RestoreOrbLocation(x, y));
            return;
        }
        if (x is null || y is null || !_collapsed || _animating) return;

        _hasRestoredOrbLocation = true;
        var location = ClampOrbLocation(new Point(x.Value, y.Value));
        NormalizeCollapsedGeometry(location);
        ApplyOrbPresentation();
        UpdateRegion();
        Invalidate(true);
    }

    public Point GetRestorableOrbLocation()
    {
        if (_collapsed && !_animating)
            return ClampOrbLocation(Location);

        if (!_collapsedBounds.IsEmpty)
            return ClampOrbLocation(_collapsedBounds.Location);

        var orbSize = ScaledOrbSize();
        return ClampOrbLocation(new Point(
            Bounds.Right - orbSize.Width,
            Bounds.Bottom - orbSize.Height));
    }

    public void EnsureVisibleOnCurrentDisplays()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => EnsureVisibleOnCurrentDisplays());
            return;
        }

        if (IsDisposed) return;
        HideHoverPreview();

        if (_animating)
        {
            var wasHidden = IsHidden;
            if (_transitionExpanding)
                SetExpandedInstant(ClampToWorkingArea(_transitionTo));
            else
                SetCollapsedInstant(new Rectangle(ClampOrbLocation(_transitionTo.Location), ScaledOrbSize()));
            if (!wasHidden)
                SetViewState(_transitionExpanding ? DetailsViewState : OrbViewState);
        }

        if (_collapsed)
        {
            var previousLocation = Location;
            var location = ClampOrbLocation(Location);
            NormalizeCollapsedGeometry(location);
            ApplyOrbPresentation();
            UpdateRegion();
            Invalidate(true);
            if (previousLocation != Location) OrbPositionChanged?.Invoke(Location);
            return;
        }

        var previousOrbLocation = _collapsedBounds.Location;
        Bounds = ClampToWorkingArea(Bounds);
        _expandedBounds = Bounds;
        NormalizeStoredCollapsedBounds();
        if (previousOrbLocation != _collapsedBounds.Location)
            OrbPositionChanged?.Invoke(_collapsedBounds.Location);
    }

    public void RefreshDisplayEnvironment()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshDisplayEnvironment);
            return;
        }

        if (IsDisposed) return;
        ApplyDetailLayoutForCurrentDpi(force: true);
        EnsureVisibleOnCurrentDisplays();
        MarkTransitionPreviewCacheDirty();
        _orb.Invalidate();
        UpdateRegion();
        Invalidate(true);
        ReassertTopMostPreference();
    }

    public void MoveOrbToCurrentDisplay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => MoveOrbToCurrentDisplay());
            return;
        }

        if (IsDisposed) return;
        HideHoverPreview();

        if (_animating)
        {
            var wasHidden = IsHidden;
            if (_transitionExpanding)
                SetExpandedInstant(_transitionTo);
            else
                SetCollapsedInstant(_transitionTo);
            if (!wasHidden)
                SetViewState(_transitionExpanding ? DetailsViewState : OrbViewState);
        }

        var targetScreen = Screen.FromPoint(Cursor.Position);
        var area = targetScreen.WorkingArea;
        var targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
        var margin = DisplayPlacement.ScaleLogicalPixels(20, targetDpi);
        var previousOrbLocation = _collapsedBounds.IsEmpty ? Location : _collapsedBounds.Location;

        if (!_collapsed)
        {
            var cardSize = Bounds.Size;
            var cardLocation = new Point(
                Math.Max(area.Left, area.Right - cardSize.Width - margin),
                Math.Max(area.Top, area.Bottom - cardSize.Height - margin));
            Bounds = ClampToArea(new Rectangle(cardLocation, cardSize), area);
            _expandedBounds = Bounds;

            // Moving across monitors can synchronously update DeviceDpi, so calculate
            // the stored orb anchor after the expanded card has reached its display.
            targetScreen = Screen.FromRectangle(Bounds);
            area = targetScreen.WorkingArea;
            targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
            margin = DisplayPlacement.ScaleLogicalPixels(20, targetDpi);
        }

        var orbSide = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var orbSize = new Size(orbSide, orbSide);
        var orbLocation = new Point(
            Math.Max(area.Left, area.Right - orbSize.Width - margin),
            Math.Max(area.Top, area.Bottom - orbSize.Height - margin));
        orbLocation = ClampOrbLocation(orbLocation);
        _collapsedBounds = new Rectangle(orbLocation, orbSize);
        _orbReturnLocation = orbLocation;

        if (_collapsed)
        {
            NormalizeCollapsedGeometry(orbLocation);
            ApplyOrbPresentation();
            UpdateRegion();
            Invalidate(true);
        }

        if (previousOrbLocation != _collapsedBounds.Location)
            OrbPositionChanged?.Invoke(_collapsedBounds.Location);
    }

    public void ShowDetails(bool animate = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowDetails(animate));
            return;
        }

        HideHoverPreview();
        if (_animating)
        {
            if (_transitionExpanding) return;
            if (animate) BeginTransition(_expandedBounds, expanding: true);
            else SetExpandedInstant(_expandedBounds);
        }
        else if (_collapsed)
        {
            _collapsedBounds = Bounds;
            _orbReturnLocation = Bounds.Location;
            var expandedSize = ScaledSize(ExpandedPanelSize);
            var target = ClampToWorkingArea(new Rectangle(
                Bounds.Right - expandedSize.Width,
                Bounds.Bottom - expandedSize.Height,
                expandedSize.Width,
                expandedSize.Height));
            _expandedBounds = target;
            if (animate) BeginTransition(target, expanding: true);
            else SetExpandedInstant(target);
        }

        if (!Visible && !_animating) Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        ReassertTopMostPreference();
        if (!_animating) SetViewState(DetailsViewState);
    }

    public void ShowOrb(bool animate = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowOrb(animate));
            return;
        }

        if (!Visible && !_animating) Show();
        WindowState = FormWindowState.Normal;
        if (_animating)
        {
            if (_transitionExpanding) CollapseToOrb(animate);
        }
        else if (!_collapsed)
        {
            CollapseToOrb(animate);
        }
        BringToFront();
        ReassertTopMostPreference();
        if (!_animating) SetViewState(OrbViewState);
    }

    public void CollapseToOrb(bool animate = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => CollapseToOrb(animate));
            return;
        }

        if (_animating)
        {
            if (!_transitionExpanding) return;
            if (animate) BeginTransition(_collapsedBounds, expanding: false);
            else
            {
                SetCollapsedInstant(_collapsedBounds);
                if (!IsHidden) SetViewState(OrbViewState);
                OrbPositionChanged?.Invoke(Location);
            }
            return;
        }
        if (_collapsed)
        {
            if (Visible) SetViewState(OrbViewState);
            return;
        }

        _expandedBounds = Bounds;
        var orbSize = ScaledOrbSize();
        var returnLocation = _orbReturnLocation ??
                             (_collapsedBounds.IsEmpty
                                 ? new Point(Bounds.Right - orbSize.Width, Bounds.Bottom - orbSize.Height)
                                 : _collapsedBounds.Location);
        var target = ResolveOrbReturnBounds(returnLocation);
        _collapsedBounds = target;
        if (animate) BeginTransition(target, expanding: false);
        else
        {
            SetCollapsedInstant(target);
            if (!IsHidden) SetViewState(OrbViewState);
            OrbPositionChanged?.Invoke(Location);
        }
    }

    public void ShowPanel() => ShowDetails();

    public void HidePanel()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => HidePanel());
            return;
        }

        HideHoverPreview();
        if (_animating)
        {
            if (_transitionExpanding)
                SetExpandedInstant(_transitionTo);
            else
                SetCollapsedInstant(_transitionTo);
        }
        Hide();
        SetViewState(HiddenViewState);
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    public void SavePreview(string path)
    {
        CreateControl();
        foreach (Control child in Controls) child.CreateControl();
        PerformLayout();
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
        if (_animating && Region is not null)
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            using var outside = new Region(new Rectangle(Point.Empty, bitmap.Size));
            outside.Exclude(Region);
            using var transparent = new SolidBrush(Color.Transparent);
            graphics.FillRegion(transparent, outside);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
