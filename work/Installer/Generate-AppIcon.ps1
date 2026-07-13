param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$PreviewPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing.dll' -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class CodexQuotaIconGenerator
{
    private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
    private static readonly Color Canvas = Color.FromArgb(20, 22, 21);
    private static readonly Color Surface = Color.FromArgb(29, 32, 30);
    private static readonly Color Border = Color.FromArgb(68, 74, 70);
    private static readonly Color Track = Color.FromArgb(58, 64, 60);
    private static readonly Color Mint = Color.FromArgb(106, 228, 176);
    private static readonly Color Sky = Color.FromArgb(126, 196, 255);

    public static void Generate(string outputPath, string previewPath)
    {
        var images = new List<byte[]>();
        foreach (var size in Sizes)
        {
            using (var bitmap = Draw(size))
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                images.Add(stream.ToArray());
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
        using (var file = File.Create(outputPath))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)images.Count);
            var offset = 6 + images.Count * 16;
            for (var index = 0; index < images.Count; index++)
            {
                var size = Sizes[index];
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)(size == 256 ? 0 : size));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)images[index].Length);
                writer.Write((uint)offset);
                offset += images[index].Length;
            }
            foreach (var image in images)
                writer.Write(image);
        }

        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(previewPath)));
            using (var preview = Draw(256))
                preview.Save(previewPath, ImageFormat.Png);
        }
    }

    private static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var inset = Math.Max(1f, size * 0.055f);
            var disk = new RectangleF(inset, inset, size - inset * 2f, size - inset * 2f);
            using (var shadow = new SolidBrush(Color.FromArgb(54, 0, 0, 0)))
                graphics.FillEllipse(shadow, disk.X + size * 0.018f, disk.Y + size * 0.025f, disk.Width, disk.Height);
            using (var background = new LinearGradientBrush(
                disk, Surface, Canvas, LinearGradientMode.ForwardDiagonal))
                graphics.FillEllipse(background, disk);
            using (var outline = new Pen(Border, Math.Max(0.65f, size * 0.018f)))
                graphics.DrawEllipse(outline, disk);

            DrawQuotaArc(graphics, size, 0.185f, 0.092f, 0.82f, Mint);
            DrawQuotaArc(graphics, size, 0.315f, 0.072f, 0.58f, Sky);
        }
        return bitmap;
    }

    private static void DrawQuotaArc(
        Graphics graphics,
        int size,
        float insetRatio,
        float strokeRatio,
        float progress,
        Color color)
    {
        const float startAngle = 138f;
        const float sweep = 264f;
        var inset = size * insetRatio;
        var rectangle = new RectangleF(inset, inset, size - inset * 2f, size - inset * 2f);
        var stroke = Math.Max(1.2f, size * strokeRatio);
        using (var track = new Pen(Track, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
            graphics.DrawArc(track, rectangle, startAngle, sweep);
        using (var value = new Pen(color, stroke)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        })
            graphics.DrawArc(value, rectangle, startAngle, sweep * progress);
    }
}
'@

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$resolvedPreview = if ([string]::IsNullOrWhiteSpace($PreviewPath)) { '' } else { [IO.Path]::GetFullPath($PreviewPath) }
[CodexQuotaIconGenerator]::Generate($resolvedOutput, $resolvedPreview)
Write-Output "PASS multi-size minimalist icon | 16,20,24,32,40,48,64,128,256 | $resolvedOutput"
