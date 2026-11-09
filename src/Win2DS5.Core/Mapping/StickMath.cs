namespace Win2DS5.Core;

public readonly record struct StickVector(double X, double Y);

public static class StickMath
{
    public static StickVector Process(byte rawX, byte rawY, double deadzone, double curve, double horizontal, double vertical)
    {
        double x = Math.Clamp((rawX - 128) / 127.0, -1, 1);
        double y = Math.Clamp((rawY - 128) / 127.0, -1, 1);
        return FromNormalized(x, y, deadzone, curve, horizontal, vertical);
    }

    public static StickVector FromNormalized(double x, double y, double deadzone, double curve, double horizontal, double vertical)
    {
        double mag = Math.Sqrt((x * x) + (y * y));
        if (mag < 0.0001 || mag < deadzone)
            return default;

        if (mag > 1)
        {
            x /= mag;
            y /= mag;
            mag = 1;
        }

        double span = 1 - Math.Clamp(deadzone, 0, 0.9);
        double shaped = Math.Clamp((mag - deadzone) / span, 0, 1);
        shaped = Math.Pow(shaped, Math.Clamp(curve, 0.2, 5));
        return new StickVector(x / mag * shaped * horizontal, y / mag * shaped * vertical);
    }
}

public static class ZoneMap
{
    public static TouchZone Hit(int x, int y, TouchpadSettings settings)
    {
        double nx = Math.Clamp(x / 1919.0, 0, 1);
        double ny = Math.Clamp(y / 1079.0, 0, 1);
        if (ny <= settings.TopFraction)
            return TouchZone.Top;
        if (nx <= settings.LeftFraction)
            return TouchZone.Left;
        if (nx >= 1 - settings.RightFraction)
            return TouchZone.Right;
        return TouchZone.Center;
    }
}
