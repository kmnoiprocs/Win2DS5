using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Win2DS5.App.Runtime;
using Win2DS5.App.Theming;
using Win2DS5.Core;
using WinRT.Interop;

namespace Win2DS5.App.Overlay;

public sealed class KeyboardWindow : Window
{
    private readonly Grid _root = new();
    private readonly Grid _body = new() { RowSpacing = 6 };
    private readonly Grid _bar = new() { Height = 42 };
    private readonly Grid _keys = new();
    private readonly Canvas _selectorLayer = new() { IsHitTestVisible = false };
    private readonly Grid _selector = new();
    private readonly Rectangle _selectorRing = new() { RadiusX = 9, RadiusY = 9, StrokeThickness = 2 };
    private readonly Rectangle _selectorFlash = new() { RadiusX = 9, RadiusY = 9, Opacity = 0 };
    private readonly ScalarTransition _flashFade = new() { Duration = TimeSpan.FromMilliseconds(260) };
    private readonly TextBlock _hint = new()
    {
        Text = KeyboardModel.ControlsHint,
        FontSize = 11,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock _closeLabel = new()
    {
        Text = "Close",
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };
    private readonly Border _close = new()
    {
        Width = 72,
        Height = 26,
        Margin = new Thickness(10, 0, 0, 0),
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _empty = new()
    {
        Text = "Suggestions appear as you type",
        FontSize = 13,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly List<Cap> _caps = [];
    private readonly List<Pill> _pills = [];
    private FrameworkElement? _target;
    private string _suggestionKey = "";
    private int _generation = -1;
    private int _presses = -1;
    private static readonly SubclassProc SubclassDelegate = Subclass;
    private static readonly EnumChildProc EnumChildren = HookChild;
    private GCHandle _self;
    private bool _configured;
    private bool _subclassed;
    private bool _childrenHooked;
    private bool _closeHot;
    private bool _placedOnce;

    public KeyboardWindow()
    {
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _close.Child = _closeLabel;
        _close.PointerEntered += (_, _) => SetCloseHot(true);
        _close.PointerExited += (_, _) => SetCloseHot(false);
        _close.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            DismissFromMouse();
        };
        var hintRow = new Grid();
        hintRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hintRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hintRow.Children.Add(_hint);
        Grid.SetColumn(_close, 1);
        hintRow.Children.Add(_close);
        _body.Children.Add(_bar);
        Grid.SetRow(hintRow, 1);
        _body.Children.Add(hintRow);
        var deck = new Grid();
        deck.Children.Add(_keys);
        Grid.SetRow(deck, 2);
        _body.Children.Add(deck);
        Grid.SetRowSpan(_selectorLayer, 3);
        _body.Children.Add(_selectorLayer);

        var selectorGlow = new Grid();
        _selector.Children.Add(selectorGlow);
        _selector.Children.Add(_selectorFlash);
        _selector.Children.Add(_selectorRing);
        _selector.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(120) };
        _selectorLayer.Children.Add(_selector);
        _ = new Glow(selectorGlow, _selectorRing, 12);

        _root.Padding = new Thickness(12, 10, 12, 12);
        _root.Children.Add(_body);
        Content = _root;
        _body.SizeChanged += (_, _) => PlaceSelector(animate: false);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsResizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        RefreshTheme();
    }

    public void RefreshTheme()
    {
        _root.RequestedTheme = Theme.ElementTheme;
        _root.Background = Theme.Brush("PanelGradientBrush");
        _selectorRing.Stroke = Theme.Brush("AccentBrush");
        _selectorFlash.Fill = Theme.Brush("AccentHaloBrush");
        _hint.Foreground = Theme.Brush("MutedBrush");
        _empty.Foreground = Theme.Brush("MutedBrush");
        PaintClose();
        _generation = -1;
        _suggestionKey = "";
        _target = null;
        if (_configured)
            NativeOverlay.SetBorderColor(WindowNative.GetWindowHandle(this), Theme.Accent);
    }

    public void Apply(KeyboardPose pose)
    {
        var hwnd = Ensure();
        if (pose.Generation != _generation)
        {
            _generation = pose.Generation;
            Build(pose);
            _suggestionKey = "";
        }

        SyncKeys(pose);
        SyncSuggestions(pose);
        Track(pose);
        if (pose.Presses != _presses)
        {
            if (_presses >= 0)
                Flash();
            _presses = pose.Presses;
        }

        NativeOverlay.Place(hwnd, pose.Left, pose.Top, pose.Width, pose.Height, pose.Opacity);
        NativeOverlay.ShowWithoutActivating(hwnd);
    }

    public void Dismiss()
    {
        if (!_configured)
            return;
        NativeOverlay.Hide(WindowNative.GetWindowHandle(this));
        _placedOnce = false;
    }

    private IntPtr Ensure()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        if (!_configured)
        {
            NativeOverlay.Configure(hwnd);
            NativeOverlay.SetBorderColor(hwnd, Theme.Accent);
            _configured = true;
        }

        HookInput(hwnd);
        return hwnd;
    }

    private void HookInput(IntPtr hwnd)
    {
        if (!_self.IsAllocated)
            _self = GCHandle.Alloc(this);
        var token = GCHandle.ToIntPtr(_self);
        if (!_subclassed)
        {
            NativeOverlay.AllowClicks(hwnd);
            SetWindowSubclass(hwnd, SubclassDelegate, 2, (nuint)token);
            _subclassed = true;
        }

        if (!_childrenHooked)
            EnumChildWindows(hwnd, EnumChildren, token);
    }

    private void DismissFromMouse() => App.Runtime.SetKeyboard(false);

    private void SetCloseHot(bool hot)
    {
        _closeHot = hot;
        PaintClose();
    }

    private void PaintClose()
    {
        _closeLabel.Foreground = Theme.Brush(_closeHot ? "AccentTextBrush" : "TextBrush");
        _close.Background = Theme.Brush(_closeHot ? "AccentSoftBrush" : "ButtonFillBrush");
        _close.BorderBrush = Theme.Brush(_closeHot ? "AccentHaloBrush" : "LineBrush");
    }

    private bool HitClose(int clientX, int clientY)
    {
        if (_close.ActualWidth <= 0 || _close.ActualHeight <= 0)
            return false;
        Windows.Foundation.Point origin;
        try
        {
            origin = _close.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point(0, 0));
        }
        catch (Exception)
        {
            return false;
        }

        double scale = NativeOverlay.Scale(WindowNative.GetWindowHandle(this));
        double x = origin.X * scale;
        double y = origin.Y * scale;
        double w = _close.ActualWidth * scale;
        double h = _close.ActualHeight * scale;
        return clientX >= x - 4 && clientY >= y - 4 && clientX < x + w + 4 && clientY < y + h + 4;
    }

    private static IntPtr Subclass(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data)
    {
        if (GCHandle.FromIntPtr((IntPtr)data).Target is KeyboardWindow window)
        {
            if (message == NativeOverlay.WmMouseActivate)
                return new IntPtr(NativeOverlay.MaNoActivate);
            if ((message == 0x0201 || message == 0x0246) && TryClientPoint(hwnd, out int x, out int y) && window.HitClose(x, y))
            {
                window.DispatcherQueue.TryEnqueue(window.DismissFromMouse);
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static bool HookChild(IntPtr child, IntPtr token)
    {
        SetWindowSubclass(child, SubclassDelegate, 2, (nuint)token);
        if (GCHandle.FromIntPtr(token).Target is KeyboardWindow window)
            window._childrenHooked = true;
        return true;
    }

    private static bool TryClientPoint(IntPtr hwnd, out int x, out int y)
    {
        if (!GetCursorPos(out var point) || !ScreenToClient(hwnd, ref point))
        {
            x = 0;
            y = 0;
            return false;
        }

        x = point.X;
        y = point.Y;
        return true;
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, nuint id, nuint data);

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwnd, EnumChildProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);

    private void Build(KeyboardPose pose)
    {
        _keys.Children.Clear();
        _keys.RowDefinitions.Clear();
        _keys.ColumnDefinitions.Clear();
        _caps.Clear();
        _target = null;
        for (int row = 0; row < pose.Rows.Count; row++)
            _keys.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        for (int row = 0; row < pose.Rows.Count; row++)
        {
            var line = new Grid();
            foreach (var cell in pose.Rows[row])
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cell.Span, GridUnitType.Star) });
            for (int column = 0; column < pose.Rows[row].Count; column++)
            {
                var super = new TextBlock
                {
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                    IsHitTestVisible = false,
                };
                var label = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
                var face = new Grid();
                face.Children.Add(super);
                face.Children.Add(label);
                var key = new Border
                {
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(9),
                    BorderThickness = new Thickness(1),
                    Child = face,
                    IsHitTestVisible = false,
                };
                key.SizeChanged += (_, _) =>
                {
                    if (ReferenceEquals(key, _target))
                        PlaceSelector(animate: false);
                };
                Grid.SetColumn(key, column);
                line.Children.Add(key);
                _caps.Add(new Cap(key, label, super));
            }

            Grid.SetRow(line, row);
            _keys.Children.Add(line);
        }
    }

    private void SyncKeys(KeyboardPose pose)
    {
        int index = 0;
        foreach (var row in pose.Rows)
        {
            foreach (var cell in row)
            {
                if (index >= _caps.Count)
                    return;
                var cap = _caps[index++];
                cap.Label.Text = cell.Label;
                cap.Super.Text = cell.Super;
                bool hasSuper = cell.Super.Length > 0;
                cap.Super.Visibility = hasSuper ? Visibility.Visible : Visibility.Collapsed;
                cap.Label.VerticalAlignment = hasSuper ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                cap.Label.Margin = hasSuper ? new Thickness(0, 0, 0, 3) : new Thickness(0);
                cap.Label.FontSize = cell.Label.Length switch
                {
                    <= 1 => 18,
                    <= 2 => 16,
                    <= 5 => 13,
                    _ => 11,
                };
                if (cell.Selected)
                {
                    cap.Label.Foreground = Theme.Brush("AccentTextBrush");
                    cap.Key.Background = Theme.Brush("AccentSoftBrush");
                }
                else if (cell.Latched)
                {
                    cap.Label.Foreground = Theme.Brush("AccentTextBrush");
                    cap.Key.Background = Theme.Brush("AccentSoftBrush");
                }
                else
                {
                    cap.Label.Foreground = Theme.Brush("TextBrush");
                    cap.Key.Background = Theme.Brush("ButtonFillBrush");
                }

                cap.Key.BorderBrush = Theme.Brush("LineBrush");
                cap.Super.Foreground = Theme.Brush(cell.EmphasizeSuper ? "AccentTextBrush" : "MutedBrush");
            }
        }
    }

    private void SyncSuggestions(KeyboardPose pose)
    {
        var key = string.Join("\n", pose.Suggestions);
        if (key != _suggestionKey)
        {
            _suggestionKey = key;
            BuildPills(pose);
        }

        _empty.Visibility = pose.Suggestions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _pills.Count; i++)
        {
            bool active = i == pose.SuggestionIndex;
            var pill = _pills[i];
            pill.Word.Foreground = Theme.Brush(active ? "AccentTextBrush" : "TextBrush");
            pill.Box.Background = Theme.Brush(active ? "AccentSoftBrush" : "PanelAltBrush");
            pill.Box.BorderBrush = Theme.Brush(active ? "AccentHaloBrush" : "LineBrush");
            pill.Badge.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            pill.Word.Margin = active ? new Thickness(10, 0, 28, 0) : new Thickness(10, 0, 10, 0);
        }
    }

    private void BuildPills(KeyboardPose pose)
    {
        _bar.Children.Clear();
        _bar.ColumnDefinitions.Clear();
        _pills.Clear();
        int count = Math.Max(pose.Suggestions.Count, 1);
        for (int i = 0; i < count; i++)
            _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < pose.Suggestions.Count; i++)
        {
            var word = new TextBlock
            {
                Text = pose.Suggestions[i],
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            var badge = new TextBlock
            {
                Text = "R1",
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                IsHitTestVisible = false,
            };
            var face = new Grid();
            face.Children.Add(word);
            face.Children.Add(badge);
            var box = new Border
            {
                Margin = new Thickness(2, 0, 2, 0),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Child = face,
                IsHitTestVisible = false,
            };
            box.SizeChanged += (_, _) =>
            {
                if (ReferenceEquals(box, _target))
                    PlaceSelector(animate: false);
            };
            Grid.SetColumn(box, i);
            _bar.Children.Add(box);
            _pills.Add(new Pill(box, word, badge));
        }

        Grid.SetColumnSpan(_empty, count);
        _bar.Children.Add(_empty);
    }

    private void Track(KeyboardPose pose)
    {
        FrameworkElement? target = null;
        if (pose.SuggestionFocus && pose.SuggestionIndex >= 0 && pose.SuggestionIndex < _pills.Count)
            target = _pills[pose.SuggestionIndex].Box;
        else
        {
            int index = 0;
            int selected = -1;
            foreach (var row in pose.Rows)
            {
                foreach (var cell in row)
                {
                    if (cell.Selected)
                        selected = index;
                    index++;
                }
            }

            if (selected >= 0 && selected < _caps.Count)
                target = _caps[selected].Key;
        }

        if (ReferenceEquals(target, _target))
            return;
        _target = target;
        PlaceSelector(animate: _placedOnce);
    }

    private void PlaceSelector(bool animate)
    {
        if (_target == null || _target.ActualWidth <= 0 || _target.ActualHeight <= 0)
        {
            _selector.Opacity = 0;
            return;
        }

        var origin = _target.TransformToVisual(_body).TransformPoint(new Windows.Foundation.Point(0, 0));
        _selector.TranslationTransition = animate ? new Vector3Transition { Duration = TimeSpan.FromMilliseconds(120) } : null;
        _selector.Width = _target.ActualWidth;
        _selector.Height = _target.ActualHeight;
        _selector.Translation = new Vector3((float)origin.X, (float)origin.Y, 0);
        _selector.Opacity = 1;
        _placedOnce = true;
    }

    private void Flash()
    {
        _selectorFlash.OpacityTransition = null;
        _selectorFlash.Opacity = 1;
        DispatcherQueue.TryEnqueue(() =>
        {
            _selectorFlash.OpacityTransition = _flashFade;
            _selectorFlash.Opacity = 0;
        });
    }

    private sealed record Cap(Border Key, TextBlock Label, TextBlock Super);

    private sealed record Pill(Border Box, TextBlock Word, TextBlock Badge);
}
