namespace CodexQuota.App;

internal static class TrayIconFactory
{
    public static Stream Create(double remainingPercent)
    {
        const int size = 32;
        const int pixelBytes = size * size * 4;
        const int maskBytes = size * 4;
        var stream = new MemoryStream(22 + 40 + pixelBytes + maskBytes);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(40 + pixelBytes + maskBytes);
            writer.Write(22);
            writer.Write(40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(pixelBytes);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            var progress = Math.Clamp(remainingPercent, 0d, 100d) / 100d;
            for (var y = size - 1; y >= 0; y--)
            for (var x = 0; x < size; x++)
            {
                var dx = x + .5 - size / 2d;
                var dy = y + .5 - size / 2d;
                var radius = Math.Sqrt(dx * dx + dy * dy);
                var alpha = Math.Clamp(1d - Math.Abs(radius - 11.3) / 2.4, 0d, 1d);
                var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
                if (angle < 0) angle += 360;
                var delta = (angle - 135 + 360) % 360;
                var active = delta <= 270 * progress;
                var (r, g, b) = active ? (87, 217, 170) : (56, 70, 64);
                writer.Write((byte)b);
                writer.Write((byte)g);
                writer.Write((byte)r);
                writer.Write((byte)Math.Round(alpha * 255));
            }
            writer.Write(new byte[maskBytes]);
        }
        stream.Position = 0;
        return stream;
    }
}
