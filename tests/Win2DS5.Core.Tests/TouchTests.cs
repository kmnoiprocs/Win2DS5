using Win2DS5.Core;

namespace Win2DS5.Core.Tests;

public class TouchTests
{
    [Fact]
    public void TopZone_WinsOverTheSideUnderIt()
    {
        var settings = new TouchpadSettings { TopFraction = 0.22, LeftFraction = 0.33, RightFraction = 0.33 };
        Assert.Equal(TouchZone.Top, ZoneMap.Hit(100, 40, settings));
        Assert.Equal(TouchZone.Left, ZoneMap.Hit(100, 500, settings));
        Assert.Equal(TouchZone.Right, ZoneMap.Hit(1800, 500, settings));
        Assert.Equal(TouchZone.Center, ZoneMap.Hit(960, 500, settings));
    }

    [Fact]
    public void Tap_FiresOnce_AndADragDoesNot()
    {
        var engine = new TouchGestureEngine();
        var settings = Settings();
        engine.Update(Finger(960, 500), default, settings, Ms(0), 0.01);
        var tap = engine.Update(default, default, settings, Ms(80), 0.01);
        Assert.Contains(tap.Events, ev => ev.Kind == GestureKind.Tap && ev.Zone == TouchZone.Center);

        var drag = new TouchGestureEngine();
        drag.Update(Finger(960, 500), default, settings, Ms(0), 0.01);
        drag.Update(Finger(1100, 500), default, settings, Ms(30), 0.01);
        var up = drag.Update(default, default, settings, Ms(60), 0.01);
        Assert.Empty(up.Events);
    }

    [Fact]
    public void LongPress_FiresWhileTheFingerIsStill()
    {
        var engine = new TouchGestureEngine();
        var settings = Settings();
        engine.Update(Finger(200, 800), default, settings, Ms(0), 0.01);
        var held = engine.Update(Finger(200, 800), default, settings, Ms(400), 0.01);
        Assert.Contains(held.Events, ev => ev.Kind == GestureKind.LongPress && ev.Zone == TouchZone.Left);
        var again = engine.Update(Finger(200, 800), default, settings, Ms(500), 0.01);
        Assert.Empty(again.Events);
    }

    [Fact]
    public void Trackpad_MovesByTheShapedDelta()
    {
        var engine = new TouchGestureEngine();
        var settings = Settings();
        settings.Curve = 1;
        settings.Sensitivity = 1;
        engine.Update(Finger(400, 400), default, settings, Ms(0), 0.01);
        var moved = engine.Update(Finger(440, 420), default, settings, Ms(16), 0.01);
        Assert.Equal(40, moved.PointerDx, 1);
        Assert.Equal(20, moved.PointerDy, 1);
    }

    [Fact]
    public void TwoFingers_ScrollAndDoNotTap()
    {
        var engine = new TouchGestureEngine();
        var settings = Settings();
        engine.Update(Finger(400, 400), Finger(500, 400), settings, Ms(0), 0.01);
        var scrolled = engine.Update(Finger(400, 440), Finger(500, 440), settings, Ms(16), 0.01);
        var up = engine.Update(default, default, settings, Ms(40), 0.01);
        Assert.True(scrolled.ScrollY < 0);
        Assert.Empty(up.Events);
    }

    [Fact]
    public void StickMode_MovesFromTheCenter()
    {
        var engine = new TouchGestureEngine();
        var settings = Settings();
        settings.Mode = TouchpadMode.Stick;
        settings.StickDeadzone = 0;
        settings.Curve = 1;
        settings.StickSensitivity = 1000;
        var moved = engine.Update(Finger(1919, 540), default, settings, Ms(0), 0.05);
        Assert.True(moved.PointerDx > 40);
    }

    private static TouchpadSettings Settings() => new()
    {
        Mode = TouchpadMode.Trackpad,
        LongPressMs = 400,
        DragSlop = 36,
        Curve = 1,
        Sensitivity = 1,
        TwoFingerScroll = true,
        ScrollScale = 1.5,
        EdgeScroll = false,
    };

    private static TouchSample Finger(int x, int y) => new() { Active = true, X = x, Y = y };

    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);
}
