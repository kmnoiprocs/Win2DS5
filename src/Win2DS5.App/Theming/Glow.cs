using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Win2DS5.App.Theming;

internal sealed class Glow
{
    private readonly DropShadow _shadow;
    private readonly Func<Color> _color;

    public Glow(FrameworkElement host, Shape source, float blur, float opacity = 0.95f, Func<Color>? color = null, float offsetY = 0)
    {
        var compositor = CompositionTarget.GetCompositorForCurrentThread();
        _color = color ?? (() => Theme.Accent);
        _shadow = compositor.CreateDropShadow();
        _shadow.BlurRadius = blur;
        _shadow.Opacity = opacity;
        _shadow.Offset = new Vector3(0, offsetY, 0);
        _shadow.Mask = source.GetAlphaMask();
        _shadow.Color = _color();
        var sprite = compositor.CreateSpriteVisual();
        sprite.RelativeSizeAdjustment = Vector2.One;
        sprite.Shadow = _shadow;
        ElementCompositionPreview.SetElementChildVisual(host, sprite);
        Theme.Changed += Refresh;
    }

    public void Refresh() => _shadow.Color = _color();

    public void SetOpacity(float opacity) => _shadow.Opacity = opacity;
}

public static class Motion
{
    public static void Lift(FrameworkElement element, float hover = 1.06f, float press = 0.94f)
    {
        element.ScaleTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(150) };
        element.SizeChanged += (_, _) =>
            element.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        element.PointerEntered += (_, _) => element.Scale = new Vector3(hover, hover, 1);
        element.PointerExited += (_, _) => element.Scale = Vector3.One;
        element.PointerCanceled += (_, _) => element.Scale = Vector3.One;
        element.AddHandler(UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => element.Scale = new Vector3(press, press, 1)), true);
        element.AddHandler(UIElement.PointerReleasedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => element.Scale = new Vector3(hover, hover, 1)), true);
    }

    public static void Enter(UIElement page)
    {
        page.OpacityTransition = null;
        page.TranslationTransition = null;
        page.Opacity = 0;
        page.Translation = new Vector3(0, 14, 0);
        page.Visibility = Visibility.Visible;
        page.DispatcherQueue.TryEnqueue(() =>
        {
            page.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(220) };
            page.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(280) };
            page.Opacity = 1;
            page.Translation = Vector3.Zero;
        });
    }

    public static void Fade(UIElement element, int milliseconds = 140) =>
        element.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(milliseconds) };
}
