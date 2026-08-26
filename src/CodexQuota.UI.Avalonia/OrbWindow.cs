using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed class OrbWindow : Window
{
    private static readonly Cursor DragCursor = new(StandardCursorType.SizeAll);
    private static readonly Cursor ClickCursor = new(StandardCursorType.Hand);
    private readonly OrbControl _orb;
    private AppSettings _settings = AppSettings.Default;
    private bool _pointerMoved;
    private bool _moveMode;
    private bool _pointerPressed;
    private PixelPoint _pressPosition;
    private PixelPoint _pressPointerScreenPosition;

    public event EventHandler? OpenDetailsRequested;
    public event EventHandler<PixelPoint>? MoveCompleted;

    internal int RenderedRingCount => double.IsFinite(_orb.SecondaryRemainingPercent) ? 2 :
        string.IsNullOrWhiteSpace(_orb.PrimaryLabel) ? 0 : 1;
    internal string RenderedPrimaryLabel => _orb.PrimaryLabel;
    internal string RenderedSecondaryLabel => _orb.SecondaryLabel;
    internal QuotaConnectionState RenderedConnectionState => _orb.ConnectionState;
    internal bool IsMoveMode => _moveMode;
    internal bool HasInteractiveCursor => _orb.Cursor is not null;

    public OrbWindow()
    {
        Title = "CodexQuota";
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = new ScaleTransform(1, 1);

        _orb = new OrbControl { HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch };
        Content = _orb;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => FinishPointerInteraction(false);
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Normalize();
        Width = Height = _settings.OrbSize;
        MinWidth = MinHeight = _settings.OrbSize;
        MaxWidth = MaxHeight = _settings.OrbSize;
        Opacity = _settings.OrbOpacityPercent / 100d;
        Topmost = _settings.AlwaysOnTop;
        _orb.OrbBackground = Color.Parse(_settings.OrbBackground);
        _orb.OuterRingColor = Color.Parse(_settings.OuterRingColor);
        _orb.InnerRingColor = Color.Parse(_settings.InnerRingColor);
        _orb.FeedbackEnabled = _settings.ConsumptionFeedbackEnabled;
        _orb.FeedbackStyle = _settings.ConsumptionFeedbackStyle;
        _orb.AnimateFeedback = !_settings.ReducedMotion;
        UpdatePointerCursor();
    }

    public void ApplyPresentation(QuotaPresentation presentation)
    {
        var windows = presentation.Snapshot?.VisibleWindows ?? [];
        if (windows.Count == 0)
        {
            _orb.RemainingPercent = 0;
            _orb.SecondaryRemainingPercent = double.NaN;
            _orb.PrimaryLabel = string.Empty;
            _orb.SecondaryLabel = string.Empty;
            _orb.Caption = _settings.Language == AppLanguage.SimplifiedChinese ? "等待数据" : "WAITING";
        }
        else
        {
            var selected = UiElements.SelectRingWindows(windows, _settings);
            var primary = selected.Outer!;
            var secondary = selected.Inner;
            _orb.RemainingPercent = primary.ClampedRemainingPercent;
            _orb.PrimaryLabel = UiElements.ShortWindowLabel(primary.WindowMinutes);
            _orb.SecondaryRemainingPercent = secondary?.ClampedRemainingPercent ?? double.NaN;
            _orb.SecondaryLabel = secondary is null ? string.Empty : UiElements.ShortWindowLabel(secondary.WindowMinutes);
            _orb.Caption = _settings.Language == AppLanguage.SimplifiedChinese ? "剩余" : "REMAINING";
        }
        _orb.FeedbackIntensity = Math.Clamp((presentation.Forecast?.PercentPerHour ?? 0) / 8d, 0, 1);
        _orb.ConnectionState = presentation.ConnectionState;
        ToolTip.SetTip(_orb, _settings.HoverPreviewEnabled ? BuildToolTip(presentation) : null);
    }

    public void SetMoveMode(bool enabled)
    {
        _moveMode = enabled;
        _orb.MoveMode = enabled;
        UpdatePointerCursor();
        ToolTip.SetTip(_orb, enabled
            ? (_settings.Language == AppLanguage.SimplifiedChinese
                ? "移动模式：拖动后自动恢复原来的穿透与锁定设置"
                : "Move mode: original click-through and lock settings return after dragging")
            : null);
    }

    private void UpdatePointerCursor()
    {
        if (_settings.ClickThrough && !_moveMode)
        {
            _orb.Cursor = null;
            return;
        }
        _orb.Cursor = _moveMode || !_settings.PositionLocked ? DragCursor : ClickCursor;
    }

    public void RestorePosition(double? x, double? y)
    {
        if (x is null || y is null) return;
        var desired = new PixelPoint((int)Math.Round(x.Value), (int)Math.Round(y.Value));
        var screen = Screens.ScreenFromPoint(desired) ?? Screens.Primary;
        if (screen is null) return;
        var area = screen.WorkingArea;
        Position = new PixelPoint(
            Math.Clamp(desired.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(Width))),
            Math.Clamp(desired.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Height))));
    }

    public (PixelPoint Position, string DisplayId) ConstrainPosition(bool snapToEdge)
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null) return (Position, string.Empty);
        var area = screen.WorkingArea;
        var x = Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(Width)));
        var y = Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Height)));
        if (snapToEdge)
        {
            const int threshold = 18;
            var right = area.Right - (int)Math.Ceiling(Width);
            var bottom = area.Bottom - (int)Math.Ceiling(Height);
            if (Math.Abs(x - area.X) <= threshold) x = area.X;
            else if (Math.Abs(x - right) <= threshold) x = right;
            if (Math.Abs(y - area.Y) <= threshold) y = area.Y;
            else if (Math.Abs(y - bottom) <= threshold) y = bottom;
        }
        Position = new PixelPoint(x, y);
        return (Position, $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}");
    }

    public async Task AnimateOutAsync()
    {
        if (_settings.ReducedMotion) { Hide(); return; }
        await AnimateAsync(Opacity, .02, 1, .72, 90).ConfigureAwait(true);
        Hide();
    }

    public async Task AnimateInAsync()
    {
        Show();
        if (_settings.ReducedMotion) { Opacity = _settings.OrbOpacityPercent / 100d; return; }
        await AnimateAsync(.02, _settings.OrbOpacityPercent / 100d, .72, 1, 105).ConfigureAwait(true);
    }

    private async Task AnimateAsync(double fromOpacity, double toOpacity, double fromScale, double toScale, int milliseconds)
    {
        const int frames = 8;
        for (var i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3);
            Opacity = fromOpacity + (toOpacity - fromOpacity) * eased;
            var scale = fromScale + (toScale - fromScale) * eased;
            if (RenderTransform is ScaleTransform transform) transform.ScaleX = transform.ScaleY = scale;
            await Task.Delay(milliseconds / frames);
        }
    }

    private Control BuildToolTip(QuotaPresentation presentation)
    {
        var palette = UiPalette.For(AppTheme.Dark);
        var stack = new StackPanel { Spacing = 5, Margin = new Thickness(5) };
        foreach (var window in presentation.Snapshot?.VisibleWindows ?? [])
            stack.Children.Add(UiElements.Text($"{UiElements.ShortWindowLabel(window.WindowMinutes)}  {window.ClampedRemainingPercent:0}%  ·  " +
                UiElements.RemainingTime(window.ResetsAt, _settings.Language), 12, FontWeight.SemiBold, palette.TextPrimary));
        stack.Children.Add(UiElements.Text(presentation.Error ?? presentation.Snapshot?.Source ?? "CodexQuota",
            10.5, FontWeight.Normal, palette.TextMuted));
        return new Border { Background = palette.SurfaceRaised, BorderBrush = palette.Border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(9), Child = stack };
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed) return;
        if (!point.Properties.IsLeftButtonPressed || !_moveMode && _settings.ClickThrough) return;
        _pointerPressed = true;
        _pointerMoved = false;
        _pressPosition = Position;
        _pressPointerScreenPosition = this.PointToScreen(e.GetPosition(this));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pointerPressed || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!_moveMode && _settings.PositionLocked) return;
        var pointer = this.PointToScreen(e.GetPosition(this));
        var dx = pointer.X - _pressPointerScreenPosition.X;
        var dy = pointer.Y - _pressPointerScreenPosition.Y;
        if (Math.Abs(dx) + Math.Abs(dy) <= 4) return;
        _pointerMoved = true;
        _orb.InteractionPaused = true;
        Position = new PixelPoint(_pressPosition.X + dx, _pressPosition.Y + dy);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pointerPressed) return;
        FinishPointerInteraction(true);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void FinishPointerInteraction(bool allowClick)
    {
        if (!_pointerPressed) return;
        _pointerPressed = false;
        _orb.InteractionPaused = false;
        if (_pointerMoved)
        {
            var placement = ConstrainPosition(_settings.SnapToEdge);
            MoveCompleted?.Invoke(this, placement.Position);
        }
        else if (allowClick && !_settings.ClickThrough && !_moveMode)
        {
            OpenDetailsRequested?.Invoke(this, EventArgs.Empty);
        }
        _pointerMoved = false;
    }

}
