namespace Win2DS5.Core;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

public static class KeyboardPlacement
{
    public const int Gap = 18;

    public static PixelPoint NearPointer(PixelPoint cursor, PixelRect work, int width, int height)
    {
        int x = cursor.X - (width / 2);
        int y = cursor.Y + Gap;
        if (y + height > work.Bottom)
            y = cursor.Y - height - Gap;
        return Clamp(x, y, work, width, height);
    }

    public static PixelPoint Clamp(int x, int y, PixelRect area, int width, int height)
    {
        int maxX = area.X + Math.Max(0, area.Width - Math.Max(0, width));
        int maxY = area.Y + Math.Max(0, area.Height - Math.Max(0, height));
        return new PixelPoint(Math.Clamp(x, area.X, maxX), Math.Clamp(y, area.Y, maxY));
    }
}
