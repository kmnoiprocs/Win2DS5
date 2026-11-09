namespace Win2DS5.Core;

public struct TouchSample
{
    public bool Active;
    public int X;
    public int Y;
    public int Id;
}

public readonly record struct TouchEvent(TouchZone Zone, GestureKind Kind);

public sealed class TouchStepResult
{
    public double PointerDx { get; set; }
    public double PointerDy { get; set; }
    public double ScrollX { get; set; }
    public double ScrollY { get; set; }
    public TouchZone Zone { get; set; } = TouchZone.Center;
    public List<TouchEvent> Events { get; } = [];
}

public sealed class TouchGestureEngine
{
    private bool _fingerDown;
    private bool _dragged;
    private bool _longFired;
    private bool _sawTwo;
    private bool _hasPrevA;
    private bool _hasPrevB;
    private int _startX;
    private int _startY;
    private int _lastAX;
    private int _lastAY;
    private int _lastBX;
    private int _lastBY;
    private TimeSpan _downAt;

    public TouchZone Zone { get; private set; } = TouchZone.Center;

    public void Reset()
    {
        _fingerDown = false;
        _dragged = false;
        _longFired = false;
        _sawTwo = false;
        _hasPrevA = false;
        _hasPrevB = false;
    }

    public TouchStepResult Update(TouchSample a, TouchSample b, TouchpadSettings settings, TimeSpan now, double dt)
    {
        var result = new TouchStepResult { Zone = Zone };
        if (settings.Mode == TouchpadMode.Off)
        {
            Reset();
            return result;
        }

        if (a.Active && b.Active)
        {
            _sawTwo = true;
            _fingerDown = true;
            _dragged = true;
            Zone = ZoneMap.Hit(a.X, a.Y, settings);
            result.Zone = Zone;
            if (settings.TwoFingerScroll && _hasPrevA && _hasPrevB)
            {
                double dx = ((a.X - _lastAX) + (b.X - _lastBX)) * 0.5;
                double dy = ((a.Y - _lastAY) + (b.Y - _lastBY)) * 0.5;
                result.ScrollX = dx * settings.ScrollScale;
                result.ScrollY = -dy * settings.ScrollScale;
            }

            Remember(a, b, true);
            return result;
        }

        if (a.Active)
        {
            Zone = ZoneMap.Hit(a.X, a.Y, settings);
            result.Zone = Zone;
            if (!_fingerDown)
            {
                _fingerDown = true;
                _downAt = now;
                _startX = a.X;
                _startY = a.Y;
                _dragged = _sawTwo;
                _longFired = false;
                Remember(a, b, false);
                if (settings.Mode == TouchpadMode.Stick)
                    ApplyStick(a, settings, dt, result);
                return result;
            }

            int travel = Math.Abs(a.X - _startX) + Math.Abs(a.Y - _startY);
            if (travel > settings.DragSlop)
                _dragged = true;

            int dx = a.X - _lastAX;
            int dy = a.Y - _lastAY;
            bool jump = Math.Abs(dx) > 400 || Math.Abs(dy) > 300;
            if (!jump && settings.Mode == TouchpadMode.Trackpad && _hasPrevA)
            {
                bool edge = settings.EdgeScroll &&
                    (a.X < 1919 * settings.EdgeFraction || a.X > 1919 * (1 - settings.EdgeFraction));
                if (edge)
                    result.ScrollY = -dy * settings.EdgeScrollScale;
                else
                {
                    result.PointerDx = Shape(dx, settings.Curve, settings.Sensitivity);
                    result.PointerDy = Shape(dy, settings.Curve, settings.Sensitivity);
                }
            }
            else if (settings.Mode == TouchpadMode.Stick)
            {
                ApplyStick(a, settings, dt, result);
            }

            if (!_longFired && !_dragged && !_sawTwo &&
                (now - _downAt).TotalMilliseconds >= settings.LongPressMs)
            {
                _longFired = true;
                result.Events.Add(new TouchEvent(Zone, GestureKind.LongPress));
            }

            Remember(a, b, false);
            return result;
        }

        if (_fingerDown && !_sawTwo && !_dragged && !_longFired &&
            (now - _downAt).TotalMilliseconds <= settings.LongPressMs)
        {
            result.Events.Add(new TouchEvent(Zone, GestureKind.Tap));
        }

        _fingerDown = false;
        _sawTwo = false;
        _hasPrevA = false;
        _hasPrevB = false;
        result.Zone = Zone;
        return result;
    }

    private static void ApplyStick(TouchSample a, TouchpadSettings settings, double dt, TouchStepResult result)
    {
        double nx = (a.X / 1919.0 * 2) - 1;
        double ny = (a.Y / 1079.0 * 2) - 1;
        var vector = StickMath.FromNormalized(nx, ny, settings.StickDeadzone, settings.Curve, 1, 1);
        result.PointerDx = vector.X * settings.StickSensitivity * dt;
        result.PointerDy = vector.Y * settings.StickSensitivity * dt;
    }

    private static double Shape(double delta, double curve, double sensitivity)
    {
        double sign = Math.Sign(delta);
        double mag = Math.Abs(delta);
        if (mag < 0.01)
            return 0;
        double shaped = Math.Pow(mag / 40.0, Math.Clamp(curve, 0.2, 5)) * 40.0;
        return sign * shaped * sensitivity;
    }

    private void Remember(TouchSample a, TouchSample b, bool two)
    {
        if (a.Active)
        {
            _lastAX = a.X;
            _lastAY = a.Y;
            _hasPrevA = true;
        }

        if (two && b.Active)
        {
            _lastBX = b.X;
            _lastBY = b.Y;
            _hasPrevB = true;
        }
        else
        {
            _hasPrevB = false;
        }
    }
}
