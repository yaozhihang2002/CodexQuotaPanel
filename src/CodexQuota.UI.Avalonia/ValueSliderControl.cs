using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CodexQuota.UI.Avalonia;

public sealed class ValueSliderControl : Control
{
    private double _value;
    private bool _dragging;

    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public double Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(_value - normalized) < .0001) return;
            _value = normalized;
            InvalidateVisual();
        }
    }

    public IBrush TrackBrush { get; init; } = UiPalette.B("#34413C");
    public IBrush FillBrush { get; init; } = UiPalette.B("#57D9AA");
    public IBrush ThumbBrush { get; init; } = UiPalette.B("#F4F7F3");
    public event Action<double>? ValueChanged;

    public ValueSliderControl()
    {
        Width = 170;
        Height = 28;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => _dragging = false;
        PointerWheelChanged += (_, e) => e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var radius = 3d;
        var left = 9d;
        var right = Math.Max(left + 1, Bounds.Width - 9d);
        var y = Bounds.Height / 2;
        var progress = Maximum <= Minimum ? 0 : Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0d, 1d);
        var x = left + (right - left) * progress;
        DrawPill(context, TrackBrush, left, right, y, radius);
        DrawPill(context, FillBrush, left, x, y, radius);
        var thumbRadius = 7d;
        context.DrawEllipse(ThumbBrush, new Pen(FillBrush, 2),
            new Rect(x - thumbRadius, y - thumbRadius, thumbRadius * 2, thumbRadius * 2));
        if (IsFocused)
            context.DrawEllipse(null, new Pen(FillBrush, 1),
                new Rect(x - thumbRadius - 3, y - thumbRadius - 3, (thumbRadius + 3) * 2, (thumbRadius + 3) * 2));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Key switch { Key.Left or Key.Down => -1, Key.Right or Key.Up => 1, _ => 0 };
        if (step == 0) return;
        SetFromValue(Value + step, true);
        e.Handled = true;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        e.Pointer.Capture(this);
        SetFromPosition(e.GetPosition(this).X);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        SetFromPosition(e.GetPosition(this).X);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        SetFromPosition(e.GetPosition(this).X);
        e.Handled = true;
    }

    private void SetFromPosition(double x)
    {
        var left = 9d;
        var width = Math.Max(1, Bounds.Width - 18d);
        SetFromValue(Minimum + Math.Clamp((x - left) / width, 0d, 1d) * (Maximum - Minimum), true);
    }

    private void SetFromValue(double value, bool notify)
    {
        Value = value;
        if (notify) ValueChanged?.Invoke(Value);
    }

    private static void DrawPill(DrawingContext context, IBrush brush, double left, double right, double y, double radius)
    {
        if (right <= left) return;
        context.DrawRectangle(brush, null, new Rect(left, y - radius, right - left, radius * 2));
        context.DrawEllipse(brush, null, new Rect(left - radius, y - radius, radius * 2, radius * 2));
        context.DrawEllipse(brush, null, new Rect(right - radius, y - radius, radius * 2, radius * 2));
    }
}
