using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hanami.Native;

namespace Hanami.App;

/// <summary>
/// One pet: a small borderless, transparent, topmost window that draws a single frame. It never takes focus
/// (WS_EX_NOACTIVATE, no taskbar or Alt+Tab entry), and because it is a layered window with per-pixel alpha, clicks
/// on its transparent parts go to whatever is underneath. All positions are physical screen pixels, applied with
/// SetWindowPos so WPF's DIP/DPI conversion never gets involved.
/// </summary>
public sealed class PetWindow : Window
{
    private readonly Image _image = new();
    private readonly ScaleTransform _flip = new(1, 1);
    private IntPtr _hwnd;
    private readonly Canvas _hearts = new() { IsHitTestVisible = false };
    private readonly TextBlock[] _heartMarks = new TextBlock[3];
    private (BitmapSource? frame, bool flip, int x, int y, int w, int h, int hearts) _last;

    /// <summary>Left button went down / moved / went up, with the cursor in physical pixels (move and up only while pressed).</summary>
    public event Action<Win32.POINT>? Pressed, Moved, Released;
    public event Action? RightClicked;

    public PetWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Width = Height = 1;
        _image.Stretch = Stretch.Fill;
        _image.RenderTransformOrigin = new Point(0.5, 0.5);
        _image.RenderTransform = _flip;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        for (var i = 0; i < _heartMarks.Length; i++)
        {
            _heartMarks[i] = new TextBlock { Text = "\u2665", Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x6f, 0x9c)), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            _hearts.Children.Add(_heartMarks[i]);
        }
        var root = new Grid();
        root.Children.Add(_image);
        root.Children.Add(_hearts);
        Content = root;
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        // The window exists (hidden) from the start so it can be positioned before it is first shown.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        Win32.SetExStyle(_hwnd, Win32.GetExStyle(_hwnd) | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);

        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Pressed?.Invoke(CursorPos()); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Moved?.Invoke(CursorPos()); };
        MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { ReleaseMouseCapture(); Released?.Invoke(CursorPos()); } e.Handled = true; };
        MouseRightButtonUp += (_, e) => { RightClicked?.Invoke(); e.Handled = true; };
    }

    private static Win32.POINT CursorPos()
    {
        Win32.GetCursorPos(out var p);
        return p;
    }

    /// <summary>
    /// Draws <paramref name="frame"/> (mirrored when <paramref name="flip"/>) in the physical rectangle; does nothing
    /// when nothing changed. <paramref name="hearts"/> is 0â€“1 while small hearts rise inside the window (a click), else âˆ’1.
    /// </summary>
    public void Present(BitmapSource frame, bool flip, int x, int y, int w, int h, double hearts = -1)
    {
        var step = hearts < 0 ? -1 : (int)(hearts * 20);
        var state = (frame, flip, x, y, w, h, step);
        if (state.Equals(_last)) return;
        PaintHearts(step < 0 ? -1 : step / 20.0, w, h);
        if (!ReferenceEquals(_last.frame, frame)) _image.Source = frame;
        if (_last.flip != flip) _flip.ScaleX = flip ? -1 : 1;
        _last = state;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, Math.Max(1, w), Math.Max(1, h), Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
        if (!IsVisible) Show();
    }

    private void PaintHearts(double t, double w, double h)
    {
        for (var i = 0; i < _heartMarks.Length; i++)
        {
            var m = _heartMarks[i];
            if (t < 0) { m.Visibility = Visibility.Collapsed; continue; }
            // Device pixels, since the window is sized in physical pixels (about 1 DIP per physical px under a per-monitor-aware app at the window's own DPI).
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            m.FontSize = Math.Max(8, h * 0.16 / dpi);
            Canvas.SetLeft(m, w / dpi * (0.28 + 0.2 * i));
            Canvas.SetTop(m, h / dpi * (0.38 - 0.34 * Math.Clamp(t * (1.2 - 0.15 * i), 0, 1) - 0.04 * i));
            m.Opacity = Math.Clamp(1.4 - 1.2 * t, 0, 1);
            m.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Hides without destroying; the next <see cref="Present"/> shows it again.</summary>
    public void Conceal()
    {
        if (IsVisible) Hide();
        _last = default;
    }
}
