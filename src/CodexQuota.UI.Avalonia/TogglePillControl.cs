using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CodexQuota.UI.Avalonia;

public sealed class TogglePillControl : Control
{
    private bool _isChecked;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            InvalidateVisual();
        }
    }

    public IBrush OnBrush { get; init; } = UiPalette.B("#57D9AA");
    public IBrush OffBrush { get; init; } = UiPalette.B("#34413C");
    public IBrush ThumbBrush { get; init; } = UiPalette.B("#F4F7F3");
    public IBrush FocusBrush { get; init; } = UiPalette.B("#72BFF2");
    public event Action<bool>? ValueChanged;

    public TogglePillControl()
    {
        Width = 46;
        Height = 26;
        MinWidth = 46;
        MinHeight = 26;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Math.Max(24, Bounds.Width);
        var height = Math.Max(16, Bounds.Height);
        var radius = height / 2;
        var track = IsChecked ? OnBrush : OffBrush;
        context.DrawRectangle(track, null, new Rect(radius, 0, Math.Max(0, width - radius * 2), height));
        context.DrawEllipse(track, null, new Rect(0, 0, height, height));
        context.DrawEllipse(track, null, new Rect(width - height, 0, height, height));
        var thumbRadius = radius - 3;
        var thumbX = IsChecked ? width - radius : radius;
        context.DrawEllipse(ThumbBrush, new Pen(UiPalette.B("#33000000"), 1),
            new Rect(thumbX - thumbRadius, radius - thumbRadius, thumbRadius * 2, thumbRadius * 2));
        if (IsFocused)
            context.DrawEllipse(null, new Pen(FocusBrush, 1.4),
                new Rect(1, 1, height - 2, height - 2));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        Focus();
        Toggle();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Space or Key.Enter)) return;
        Toggle();
        e.Handled = true;
    }

    private void Toggle()
    {
        IsChecked = !IsChecked;
        ValueChanged?.Invoke(IsChecked);
    }
}
