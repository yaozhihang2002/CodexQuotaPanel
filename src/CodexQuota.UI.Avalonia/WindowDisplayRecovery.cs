using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CodexQuota.UI.Avalonia;

/// <summary>Reconciles a retained window after monitor, work-area or DPI changes.</summary>
public sealed class WindowDisplayRecovery
{
    private readonly Window _window;
    private readonly Func<bool> _isPresented;
    private readonly Action? _redrawNativeFrame;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly double _minimumWidth;
    private readonly double _minimumHeight;
    private Size _preferredSize;
    private bool _fitting;
    private bool _closed;

    public WindowDisplayRecovery(Window window, Func<bool>? isPresented = null, Action? redrawNativeFrame = null)
    {
        _window = window;
        _isPresented = isPresented ?? (() => window.IsVisible);
        _redrawNativeFrame = redrawNativeFrame;
        _minimumWidth = window.MinWidth;
        _minimumHeight = window.MinHeight;
        _preferredSize = new Size(window.Width, window.Height);
        _timer.Tick += (_, _) => { _timer.Stop(); RecoverNow(); };
        window.ScalingChanged += QueueRecovery;
        window.Screens.Changed += QueueRecovery;
        window.Opened += QueueRecovery;
        window.Resized += (_, e) =>
        {
            if (!_fitting && e.Reason == WindowResizeReason.User)
                _preferredSize = window.ClientSize;
        };
        window.Closed += (_, _) =>
        {
            _closed = true;
            _timer.Stop();
            window.ScalingChanged -= QueueRecovery;
            window.Screens.Changed -= QueueRecovery;
            window.Opened -= QueueRecovery;
        };
    }

    private void QueueRecovery(object? sender, EventArgs e)
    {
        if (_closed) return;
        _timer.Stop();
        _timer.Start();
    }

    internal void RecoverNow()
    {
        // A transparent dashboard is parked offscreen between opens. Do not
        // move it back into the desktop or overwrite its orb-relative anchor.
        if (_closed || !_isPresented()) return;
        var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
        if (screen is null) return;
        Fit(screen);
        Repaint();
    }

    public PixelSize Fit(Screen screen)
    {
        return Fit(screen.WorkingArea, screen.Scaling);
    }

    internal PixelSize Fit(PixelRect work, double scale)
    {
        _fitting = true;
        try
        {
            var frame = FrameInsets;
            var availableWidth = Math.Max(1, work.Width / scale - frame.Width);
            var availableHeight = Math.Max(1, work.Height / scale - frame.Height);
            _window.MinWidth = Math.Min(_minimumWidth, availableWidth);
            _window.MinHeight = Math.Min(_minimumHeight, availableHeight);
            _window.Width = Math.Min(_preferredSize.Width, availableWidth);
            _window.Height = Math.Min(_preferredSize.Height, availableHeight);
            var outer = new PixelSize((int)Math.Ceiling((_window.Width + frame.Width) * scale),
                (int)Math.Ceiling((_window.Height + frame.Height) * scale));
            var pos = _window.Position;
            _window.Position = new PixelPoint(
                Math.Clamp(pos.X, work.X, Math.Max(work.X, work.Right - outer.Width)),
                Math.Clamp(pos.Y, work.Y, Math.Max(work.Y, work.Bottom - outer.Height)));
            return outer;
        }
        finally { _fitting = false; }
    }

    internal Size FrameInsets
    {
        get
        {
            if (_window.FrameSize is { } frame && _window.ClientSize is { Width: > 0, Height: > 0 } client)
                return new Size(Math.Max(0, frame.Width - client.Width), Math.Max(0, frame.Height - client.Height));
            // Before the first native Show no frame measurement exists yet.
            // A second fit after Show replaces this conservative allowance.
            return _window.WindowDecorations == WindowDecorations.None ? default : new Size(16, 40);
        }
    }

    private void Repaint()
    {
        foreach (var control in _window.GetVisualDescendants().OfType<Control>())
        {
            control.InvalidateMeasure();
            control.InvalidateArrange();
            control.InvalidateVisual();
        }
        _window.InvalidateMeasure();
        _window.InvalidateArrange();
        _window.UpdateLayout();
        _window.InvalidateVisual();
        _redrawNativeFrame?.Invoke();
    }
}
