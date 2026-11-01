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
    private (BitmapSource? frame, bool flip, int x, int y, int w, int h) _last;

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
        Content = _image;
        SourceInitialized += (_, _) => _hwnd = new WindowInteropHelper(this).Handle;
        // The window exists (hidden) from the start so it can be positioned before it is first shown.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        Win32.SetExStyle(_hwnd, Win32.GetExStyle(_hwnd) | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);

        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Pressed?.Invoke(Cursor()); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Moved?.Invoke(Cursor()); };
        MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { ReleaseMouseCapture(); Released?.Invoke(Cursor()); } e.Handled = true; };
        MouseRightButtonUp += (_, e) => { RightClicked?.Invoke(); e.Handled = true; };
    }

    private static Win32.POINT Cursor()
    {
        Win32.GetCursorPos(out var p);
        return p;
    }

    /// <summary>Draws <paramref name="frame"/> (mirrored when <paramref name="flip"/>) in the physical rectangle; does nothing when nothing changed.</summary>
    public void Present(BitmapSource frame, bool flip, int x, int y, int w, int h)
    {
        var state = (frame, flip, x, y, w, h);
        if (state.Equals(_last)) return;
        if (!ReferenceEquals(_last.frame, frame)) _image.Source = frame;
        if (_last.flip != flip) _flip.ScaleX = flip ? -1 : 1;
        _last = state;
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, x, y, Math.Max(1, w), Math.Max(1, h), Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
        if (!IsVisible) Show();
    }

    /// <summary>Hides without destroying; the next <see cref="Present"/> shows it again.</summary>
    public void Conceal()
    {
        if (IsVisible) Hide();
        _last = default;
    }
}
