using Avalonia;
using Avalonia.Media;

namespace CodexQuota.UI.Avalonia;

/// <summary>
/// Avalonia translation of the established WinForms flame language. Geometry,
/// thresholds and 88 px reference measurements intentionally stay equivalent;
/// only the drawing API differs.
/// </summary>
public sealed partial class OrbControl
{
    private const double FrozenMaximum = .03;
    private const double CoolMaximum = .25;
    private const double WarmMaximum = .52;
    private const double HotMaximum = .78;

    private static readonly (int X, int Y)[] PixelOuterCells =
    [(-2, 0), (-1, 0), (0, 0), (1, 0), (2, 0), (-2, 1), (-1, 1), (0, 1), (1, 1), (2, 1),
     (-1, 2), (0, 2), (1, 2)];
    private static readonly (int X, int Y)[] PixelMiddleCells =
    [(-1, 0), (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1), (0, 2)];
    private static readonly (int X, int Y)[] PixelCoreCells = [(0, 0), (0, 1)];

    private void DrawWinFormsFluidFlame(DrawingContext context, Point center, double size, double intensity)
    {
        var activity = ClassifyActivity(intensity);
        var scale = size / 88d;
        if (activity == 0)
        {
            DrawFluidFrostSeed(context, center, scale);
            return;
        }

        var inferno = activity == 4 ? SmoothStep(ActivityProgress(intensity, activity)) : 0d;
        var pulse = Math.Sin(_phase);
        var sway = pulse * (.35 + intensity * 1.05 + inferno * .25) * scale;
        var width = (5.8 + intensity * 5.1 + inferno * 2.9) * scale;
        var height = (7.2 + intensity * 9.2 + Math.Abs(pulse) * intensity * 1.5 + inferno * 1.1) * scale;
        var centerX = center.X + sway;
        var baseY = center.Y + 32d * scale;
        var topY = baseY - height;
        var flameColor = FlameColor(intensity, activity);

        if (inferno > .01)
        {
            var sideColor = WithAlpha(Blend(flameColor, Color.FromRgb(255, 178, 72), .42), 220 * inferno);
            context.DrawGeometry(new SolidColorBrush(sideColor), null,
                CreateWinFlame(centerX - width * .43, baseY + .2 * scale,
                    width * (.34 + inferno * .24), height * (.45 + inferno * .25), -sway * .42 - .7 * scale));
            context.DrawGeometry(new SolidColorBrush(sideColor), null,
                CreateWinFlame(centerX + width * .43, baseY + .2 * scale,
                    width * (.31 + inferno * .23), height * (.42 + inferno * .24), sway * .35 + .65 * scale));
        }

        var outer = CreateWinFlame(centerX, baseY, width, height, sway * .45);
        context.DrawGeometry(null,
            new Pen(new SolidColorBrush(WithAlpha(flameColor, 34 + intensity * 24)), 1.8 * scale,
                lineCap: PenLineCap.Round), outer);
        context.DrawGeometry(VerticalBrush(WithAlpha(Blend(Colors.White, flameColor, .48), 235),
            WithAlpha(flameColor, 225)), null, outer);

        var innerIntensity = Math.Clamp((intensity - .08) / .92, 0, 1);
        if (innerIntensity > 0)
        {
            var inner = CreateWinFlame(centerX - sway * .2, baseY - .7 * scale,
                width * .48, height * (.46 + innerIntensity * .12), -sway * .18);
            context.DrawGeometry(new SolidColorBrush(WithAlpha(FlameCoreColor(intensity, activity),
                215 + inferno * 32)), null, inner);
        }

        var emberVisibility = activity switch
        {
            3 => SmoothStep(ActivityProgress(intensity, activity)),
            4 => 1d,
            _ => 0d
        };
        if (emberVisibility > .01)
            DrawFluidEmbers(context, centerX, topY, width, height, sway, scale,
                intensity, flameColor, emberVisibility, inferno);
    }

    private void DrawFluidEmbers(DrawingContext context, double centerX, double topY, double width,
        double height, double sway, double scale, double intensity, Color flameColor,
        double visibility, double inferno)
    {
        ReadOnlySpan<double> phaseOffsets = [.08, .47, .79, .28, .66];
        ReadOnlySpan<double> sideOffsets = [.62, -.42, .88, -.84, .25];
        var count = inferno > 0 ? 3 + (int)Math.Round(inferno * 2) : 1 + (int)Math.Round(visibility * 2);
        for (var index = 0; index < count; index++)
        {
            var life = (_phase * (.205 + index * .018) + phaseOffsets[index]) % 1d;
            var envelope = Math.Sin(life * Math.PI);
            if (envelope < .12) continue;
            var direction = Math.Sign(sideOffsets[index]);
            var drift = direction * life * (1.3 + index * .35) * scale;
            var x = centerX + width * sideOffsets[index] - sway * .32 + drift;
            var y = topY + height * (.56 - life * .62);
            var emberHeight = (1.25 + intensity * 1.05 - life * .34) * scale;
            var emberWidth = Math.Max(.34 * scale, emberHeight * .3);
            var alpha = 168 * envelope * (1 - life * .35) * visibility;
            context.DrawLine(new Pen(new SolidColorBrush(WithAlpha(flameColor, alpha / 4)),
                    Math.Max(.42, .34 * scale), lineCap: PenLineCap.Round),
                new Point(x + direction * .2 * scale, y - emberHeight * .15),
                new Point(x - direction * .45 * scale, y - emberHeight * 1.25));
            context.DrawEllipse(new SolidColorBrush(WithAlpha(flameColor, alpha / 5)), null,
                new Rect(x - emberWidth * 1.25, y - emberHeight * .9,
                    emberWidth * 2.5, emberHeight * 2.5));
            context.DrawGeometry(new SolidColorBrush(WithAlpha(
                    Blend(Color.FromRgb(255, 231, 164), flameColor, .58), alpha)), null,
                CreateWinFlame(x, y + emberHeight * .45, emberWidth, emberHeight, drift * .12));
        }
    }

    private void DrawWinFormsEmber(DrawingContext context, Point center, double size, double intensity)
    {
        var activity = ClassifyActivity(intensity);
        var scale = size / 88d;
        if (activity == 0)
        {
            DrawMinimalFrostSeed(context, center, scale);
            return;
        }

        var breath = .5 + .5 * Math.Sin(_phase * .72);
        var drift = Math.Sin(_phase * .43 + .8) * .42 * scale;
        var inferno = activity == 4 ? SmoothStep(ActivityProgress(intensity, activity)) : 0;
        var color = FlameColor(intensity, activity);
        var width = (5.2 + intensity * 3.3 + breath * .45 + inferno * 1.4) * scale;
        var height = (3.3 + intensity * 2.2 + breath * .35 + inferno * .5) * scale;
        var centerX = center.X + drift;
        var centerY = center.Y + 32d * scale - height * .45;

        context.DrawEllipse(new SolidColorBrush(WithAlpha(color, 16 + intensity * 20)), null,
            new Rect(centerX - width * 1.35, centerY - height * 1.5, width * 2.7, height * 3));
        context.DrawEllipse(new SolidColorBrush(WithAlpha(color, 42 + intensity * 42)), null,
            new Rect(centerX - width * .82, centerY - height * .92, width * 1.64, height * 1.84));
        var ember = CreateWinEmber(centerX, centerY, width, height);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(WithAlpha(
            Blend(Color.Parse("#0D1210"), color, .7), 165)), Math.Max(.7, .8 * scale),
            lineCap: PenLineCap.Round), ember);
        context.DrawGeometry(VerticalBrush(Blend(Colors.White, color, .5),
            Blend(color, Color.Parse("#0D1210"), .2)), null, ember);
        context.DrawLine(new Pen(new SolidColorBrush(WithAlpha(Blend(Colors.White, color, .36), 205)),
                Math.Max(.8, 1.05 * scale), lineCap: PenLineCap.Round),
            new Point(centerX - width * .22, centerY - height * .04),
            new Point(centerX + width * (.13 + breath * .08), centerY - height * .2));

        var wispVisibility = activity switch
        {
            3 => SmoothStep(ActivityProgress(intensity, activity)),
            4 => 1d,
            _ => 0d
        };
        if (wispVisibility <= .01) return;
        var wispLife = .5 + .5 * Math.Sin(_phase * .8 + 1.1);
        var wispHeight = (1.4 + wispVisibility * (.7 + intensity * 2.6 + wispLife * 1.2)) * scale;
        context.DrawGeometry(new SolidColorBrush(WithAlpha(Blend(Colors.White, color, .55),
                (55 + intensity * 55) * wispVisibility)), null,
            CreateWinFlame(centerX + width * .16, centerY - height * .35,
                Math.Max(.75 * scale, width * .16), wispHeight, -drift * .7));
        if (inferno <= .01) return;
        var secondLife = .5 + .5 * Math.Sin(_phase * .92 + 3.2);
        context.DrawGeometry(new SolidColorBrush(WithAlpha(
                Blend(Color.FromRgb(255, 241, 183), color, .38), 120 * inferno)), null,
            CreateWinFlame(centerX - width * .18, centerY - height * .25,
                Math.Max(.7 * scale, width * .14), (2.6 + inferno * 3.2 + secondLife) * scale,
                drift * .62));
    }

    private void DrawWinFormsPixelFlame(DrawingContext context, Point center, double size, double intensity)
    {
        var activity = ClassifyActivity(intensity);
        var scale = size / 88d;
        if (activity == 0)
        {
            DrawPixelFrostSeed(context, center, scale, 0);
            return;
        }
        if (activity == 1)
        {
            var thaw = SmoothStep(ActivityProgress(intensity, activity));
            if (thaw < .35)
            {
                DrawPixelFrostSeed(context, center, scale, thaw);
                return;
            }
        }

        var frame = (int)Math.Floor((_phase * 1.65) % 4);
        var color = FlameColor(intensity, activity);
        var cell = Math.Max(2, Math.Round((2.35 + intensity * .55) * scale));
        var originX = Math.Round(center.X - cell / 2);
        var baseY = center.Y + 33d * scale;
        var outer = new SolidColorBrush(WithAlpha(Blend(color, Color.Parse("#0D1210"), .18), 245));
        var middle = new SolidColorBrush(WithAlpha(color, 250));
        var core = new SolidColorBrush(FlameCoreColor(intensity, activity));
        DrawPixelCells(context, outer, originX, baseY, cell, PixelOuterCells);
        DrawPixelCell(context, outer, originX, baseY, cell, frame is 0 or 3 ? -1 : 0, 3);
        DrawPixelCell(context, outer, originX, baseY, cell, frame is 0 or 1 ? 0 : 1, 4);
        DrawPixelCells(context, middle, originX, baseY, cell, PixelMiddleCells);
        DrawPixelCell(context, middle, originX, baseY, cell, frame is 1 or 2 ? 0 : -1, 3);
        DrawPixelCells(context, core, originX, baseY, cell, PixelCoreCells);
        DrawPixelCell(context, core, originX, baseY, cell, frame == 2 ? 1 : 0, 2);

        var hot = activity switch { 3 => SmoothStep(ActivityProgress(intensity, activity)), 4 => 1d, _ => 0d };
        var inferno = activity == 4 ? SmoothStep(ActivityProgress(intensity, activity)) : 0d;
        if (hot > .18)
        {
            var emberX = frame is 0 or 1 ? 2 : -2;
            DrawPixelCell(context, middle, originX, baseY, cell, emberX, 5);
            if (hot > .68 && frame is 1 or 3) DrawPixelCell(context, outer, originX, baseY, cell, -emberX, 4);
        }
        if (inferno > .12)
        {
            DrawPixelCell(context, outer, originX, baseY, cell, -3, 0);
            DrawPixelCell(context, outer, originX, baseY, cell, 3, 0);
        }
        if (inferno > .38) DrawPixelCell(context, middle, originX, baseY, cell, frame is 0 or 3 ? -2 : 2, 2);
        if (inferno > .68) DrawPixelCell(context, middle, originX, baseY, cell, frame is 0 or 1 ? 1 : -1, 5);
    }

    private static void DrawMinimalFrostSeed(DrawingContext context, Point center, double scale)
    {
        var width = 5.35 * scale;
        var height = 3.45 * scale;
        var centerY = center.Y + 32d * scale - height * .45;
        var ice = Color.FromRgb(112, 205, 252);
        context.DrawEllipse(new SolidColorBrush(WithAlpha(ice, 36)), null,
            new Rect(center.X - width * .95, centerY - height * 1.1, width * 1.9, height * 2.2));
        var seed = CreateWinEmber(center.X, centerY, width, height);
        context.DrawGeometry(VerticalBrush(Blend(Colors.White, ice, .26), Blend(ice, Color.Parse("#0D1210"), .18)),
            new Pen(new SolidColorBrush(WithAlpha(ice, 205)), Math.Max(.65, .72 * scale), lineCap: PenLineCap.Round), seed);
        context.DrawLine(new Pen(new SolidColorBrush(WithAlpha(Colors.White, 220)), Math.Max(.55, .6 * scale),
                lineCap: PenLineCap.Round),
            new Point(center.X - width * .2, centerY - height * .12),
            new Point(center.X + width * .08, centerY - height * .27));
    }

    private static void DrawFluidFrostSeed(DrawingContext context, Point center, double scale)
    {
        var width = 5.9 * scale;
        var height = 7.45 * scale;
        var baseY = center.Y + 32d * scale;
        var topY = baseY - height;
        var ice = Color.FromRgb(108, 201, 255);
        var seed = CreateWinFlame(center.X, baseY, width, height, 0);
        context.DrawGeometry(VerticalBrush(Blend(Colors.White, ice, .22), Blend(ice, Color.Parse("#0D1210"), .16)),
            new Pen(new SolidColorBrush(WithAlpha(ice, 195)), Math.Max(.62, .7 * scale), lineCap: PenLineCap.Round), seed);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(WithAlpha(ice, 42)), Math.Max(1, 1.55 * scale),
            lineCap: PenLineCap.Round), seed);
        var facet = new Pen(new SolidColorBrush(WithAlpha(Blend(Colors.White, ice, .25), 180)),
            Math.Max(.52, .58 * scale), lineCap: PenLineCap.Round);
        context.DrawLine(facet, new Point(center.X, topY + height * .22), new Point(center.X, baseY - height * .18));
        context.DrawLine(facet, new Point(center.X - width * .19, topY + height * .52),
            new Point(center.X + width * .19, topY + height * .52));
    }

    private static void DrawPixelFrostSeed(DrawingContext context, Point center, double scale, double thaw)
    {
        var cell = Math.Max(2, Math.Round(2.2 * scale));
        var originX = Math.Round(center.X - cell / 2);
        var baseY = center.Y + 33d * scale;
        var edge = new SolidColorBrush(Blend(Color.FromRgb(102, 190, 255), Color.Parse("#0D1210"), .1));
        var body = new SolidColorBrush(Color.FromRgb(120, 214, 255));
        var shine = new SolidColorBrush(Color.FromRgb(225, 250, 255));
        var core = new (int X, int Y)[] { (0, 0), (0, 1), (0, 2), (0, 3), (0, 4), (-1, 2), (1, 2) };
        DrawPixelCells(context, edge, originX, baseY, cell, core);
        if (thaw < .24)
            DrawPixelCells(context, edge, originX, baseY, cell, [(-1, 1), (1, 1), (-1, 3), (1, 3)]);
        if (thaw < .12)
        {
            DrawPixelCell(context, edge, originX, baseY, cell, -2, 2);
            DrawPixelCell(context, edge, originX, baseY, cell, 2, 2);
        }
        DrawPixelCells(context, body, originX, baseY, cell, [(0, 1), (0, 2), (0, 3), (-1, 2), (1, 2)]);
        DrawPixelCell(context, shine, originX, baseY, cell, 0, 2);
    }

    private static StreamGeometry CreateWinFlame(double centerX, double baseY, double width, double height, double sway)
    {
        var topY = baseY - height;
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(new Point(centerX - width / 2, baseY), true);
        path.CubicBezierTo(new Point(centerX - width * .76, baseY - height * .33),
            new Point(centerX - width * .18 + sway, topY + height * .32), new Point(centerX + sway, topY));
        path.CubicBezierTo(new Point(centerX + width * .16 + sway, topY + height * .26),
            new Point(centerX + width * .78, baseY - height * .36), new Point(centerX + width / 2, baseY));
        path.CubicBezierTo(new Point(centerX + width * .18, baseY + height * .08),
            new Point(centerX - width * .18, baseY + height * .08), new Point(centerX - width / 2, baseY));
        path.EndFigure(true);
        return geometry;
    }

    private static StreamGeometry CreateWinEmber(double centerX, double centerY, double width, double height)
    {
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(new Point(centerX - width / 2, centerY), true);
        path.CubicBezierTo(new Point(centerX - width * .42, centerY - height * .58),
            new Point(centerX + width * .28, centerY - height * .62),
            new Point(centerX + width / 2, centerY - height * .08));
        path.CubicBezierTo(new Point(centerX + width * .34, centerY + height * .48),
            new Point(centerX - width * .34, centerY + height * .45), new Point(centerX - width / 2, centerY));
        path.EndFigure(true);
        return geometry;
    }

    private static void DrawPixelCells(DrawingContext context, IBrush brush, double originX, double baseY,
        double cell, IEnumerable<(int X, int Y)> cells)
    {
        foreach (var point in cells) DrawPixelCell(context, brush, originX, baseY, cell, point.X, point.Y);
    }

    private static void DrawPixelCell(DrawingContext context, IBrush brush, double originX, double baseY,
        double cell, int x, int y) => context.DrawRectangle(brush, null,
        new Rect(originX + x * cell, baseY - (y + 1) * cell, cell, cell));

    private static LinearGradientBrush VerticalBrush(Color top, Color bottom) => new()
    {
        StartPoint = new RelativePoint(.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(.5, 1, RelativeUnit.Relative),
        GradientStops = [new GradientStop(top, 0), new GradientStop(bottom, 1)]
    };

    private static int ClassifyActivity(double intensity) => intensity switch
    {
        <= FrozenMaximum => 0,
        <= CoolMaximum => 1,
        <= WarmMaximum => 2,
        <= HotMaximum => 3,
        _ => 4
    };

    private static double ActivityProgress(double intensity, int activity)
    {
        var range = activity switch
        {
            1 => (FrozenMaximum, CoolMaximum),
            2 => (CoolMaximum, WarmMaximum),
            3 => (WarmMaximum, HotMaximum),
            4 => (HotMaximum, 1d),
            _ => (0d, FrozenMaximum)
        };
        return Math.Clamp((intensity - range.Item1) / (range.Item2 - range.Item1), 0, 1);
    }

    private static Color FlameColor(double intensity, int activity)
    {
        var progress = SmoothStep(ActivityProgress(intensity, activity));
        var frost = Color.FromRgb(102, 196, 255);
        var cool = Color.FromRgb(122, 216, 247);
        var warmBridge = Color.FromRgb(255, 237, 158);
        var warm = Color.FromRgb(255, 198, 76);
        var hot = Color.FromRgb(255, 100, 66);
        var inferno = Color.FromRgb(220, 49, 45);
        return activity switch
        {
            1 => Blend(frost, cool, progress),
            2 => BlendThrough(cool, warmBridge, warm, progress),
            3 => Blend(warm, hot, progress),
            4 => Blend(hot, inferno, progress),
            _ => frost
        };
    }

    private static Color FlameCoreColor(double intensity, int activity)
    {
        var progress = SmoothStep(ActivityProgress(intensity, activity));
        var frost = Color.FromRgb(218, 247, 255);
        var cool = Color.FromRgb(228, 250, 255);
        var warmBridge = Color.FromRgb(255, 255, 232);
        var warm = Color.FromRgb(255, 249, 202);
        var hot = Color.FromRgb(255, 238, 145);
        var inferno = Color.FromRgb(255, 224, 118);
        return activity switch
        {
            1 => Blend(frost, cool, progress),
            2 => BlendThrough(cool, warmBridge, warm, progress),
            3 => Blend(warm, hot, progress),
            4 => Blend(hot, inferno, progress),
            _ => frost
        };
    }

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value * value * (3 - 2 * value);
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb((byte)Math.Round(from.A + (to.A - from.A) * amount),
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static Color BlendThrough(Color from, Color midpoint, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        const double midpointPosition = .48;
        return amount <= midpointPosition
            ? Blend(from, midpoint, amount / midpointPosition)
            : Blend(midpoint, to, (amount - midpointPosition) / (1 - midpointPosition));
    }

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb(
        (byte)Math.Clamp((int)Math.Round(alpha), 0, 255), color.R, color.G, color.B);
}
