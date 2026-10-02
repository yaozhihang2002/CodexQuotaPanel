using Avalonia;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace CodexQuota.UI.Avalonia;

public sealed partial class OrbWindow
{
    private const double EdgeFadeMilliseconds = 240;
    private readonly DispatcherTimer _edgeMotionTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private EdgeMotion? _edgeMotion;
    internal bool IsEdgeAnimating => _edgeMotion is not null;
    internal double EdgeMotionProgress => _edgeMotion?.Progress ?? 1;
    internal bool IsEdgeClearing => _edgeMotion?.Phase == EdgePhase.Clearing;

    private enum EdgePhase { FadeOut, Clearing, Preparing, FadeIn }

    private sealed class EdgeMotion(PixelPoint target, Size size, bool collapse, double opacity, bool alreadyTarget)
    {
        public PixelPoint Target { get; } = target;
        public Size Size { get; } = size;
        public bool Collapse { get; } = collapse;
        public double FromOpacity { get; set; } = opacity;
        public EdgePhase Phase { get; set; } = alreadyTarget ? EdgePhase.FadeIn : EdgePhase.FadeOut;
        public Task? Frame { get; set; }
        public long FrameRequestedAt { get; set; }
        public long? Started { get; set; }
        public double Progress { get; set; }
    }

    private void StartEdgeMotion(PixelPoint target, double width, double height, bool collapse, bool animate)
    {
        if (_closing) return;
        _edgeMotionTimer.Stop();
        _edgeMotion = new EdgeMotion(target, new Size(width, height), collapse, Opacity,
            ReferenceEquals(Content, collapse ? _edge : _orb));
        if (!animate || !IsVisible) { FinishEdgeMotion(); return; }
        _edgeMotionTimer.Tick -= OnEdgeMotionTick;
        _edgeMotionTimer.Tick += OnEdgeMotionTick;
        _edgeMotionTimer.Start();
    }

    private void OnEdgeMotionTick(object? sender, EventArgs args)
    {
        if (_closing || _edgeMotion is not { } motion) { _edgeMotionTimer.Stop(); return; }
        if (motion.Phase is EdgePhase.Clearing or EdgePhase.Preparing)
        {
            // A UI property reaching zero is not a rendered transparent frame.
            // Keep the old footprint until its empty surface has been rendered;
            // then prepare the new footprint, still invisible, before fading in.
            if (motion.Frame?.IsCompletedSuccessfully != true)
            {
                // A suspended/lost renderer must not strand the recovery target.
                if (Environment.TickCount64 - motion.FrameRequestedAt >= 2000) FinishEdgeMotion();
                return;
            }
            if (motion.Phase == EdgePhase.Clearing)
            {
                ApplyEdgeSurface(motion);
                WaitForEdgeFrame(motion, EdgePhase.Preparing);
            }
            else
            {
                motion.Phase = EdgePhase.FadeIn;
                motion.FromOpacity = 0;
                motion.Started = null;
                motion.Frame = null;
            }
            return;
        }
        motion.Started ??= Environment.TickCount64;
        var t = Math.Clamp((Environment.TickCount64 - motion.Started.Value) / EdgeFadeMilliseconds, 0, 1);
        var eased = t * t * (3 - 2 * t);
        var fadingIn = motion.Phase == EdgePhase.FadeIn;
        motion.Progress = (fadingIn ? .5 : 0) + t * .5;
        Opacity = fadingIn ? motion.FromOpacity + (1 - motion.FromOpacity) * eased
            : motion.FromOpacity * (1 - eased);
        if (t < 1) return;
        if (fadingIn) { FinishEdgeMotion(); return; }

        Opacity = 0;
        Content = null;
        WaitForEdgeFrame(motion, EdgePhase.Clearing);
    }

    private void WaitForEdgeFrame(EdgeMotion motion, EdgePhase phase)
    {
        motion.Phase = phase;
        motion.Progress = .5;
        motion.FrameRequestedAt = Environment.TickCount64;
        UpdateLayout();
        InvalidateVisual();
        motion.Frame = ElementComposition.GetElementVisual(this)?.Compositor
            .RequestCompositionBatchCommitAsync().Rendered ?? Task.CompletedTask;
    }

    private void ApplyEdgeSurface(EdgeMotion motion)
    {
        _edgeTransition = true;
        try
        {
            if (Width != motion.Size.Width || Height != motion.Size.Height)
                SetSurfaceSize(motion.Size.Width, motion.Size.Height);
            var target = motion.Collapse ? (object)_edge : _orb;
            if (!ReferenceEquals(Content, target)) Content = target;
            if (Position != motion.Target) Position = motion.Target;
        }
        finally { _edgeTransition = false; }
    }

    private void FinishEdgeMotion()
    {
        _edgeMotionTimer.Stop();
        if (_edgeMotion is not { } motion || _closing) return;
        _edgeMotion = null;
        ApplyEdgeSurface(motion);
        Opacity = 1;
        if (!motion.Collapse) _expandedPosition = null;
    }
}
