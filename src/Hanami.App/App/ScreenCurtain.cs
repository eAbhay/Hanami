using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Hanami.Native;
using Hanami.Platform;

namespace Hanami.App;

/// <summary>
/// An opaque topmost window on one monitor, kept directly under that monitor's screensaver window. With "require
/// sign-in" it is what the slide-up reveals, so the unlocked desktop never shows before Windows locks.
/// Never takes focus. Placed in physical pixels with SetWindowPos, since WPF would scale DIPs.
/// </summary>
public sealed class ScreenCurtain : IDisposable
{
    private readonly Window _window;
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };

    public ScreenCurtain(MonitorInfo monitor, IntPtr below)
    {
        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Background = Brushes.Black,
            Left = -32000, Top = -32000, Width = 1, Height = 1,
            Content = new Grid { Children = { _image } },
        };
        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            Win32.SetExStyle(hwnd, Win32.GetExStyle(hwnd) | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE);
            var b = monitor.Bounds;
            Win32.SetWindowPos(hwnd, below, b.Left, b.Top, b.Width, b.Height, Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);
        };
        _window.Show();
    }

    /// <summary>The picture shown over the black; null = plain black.</summary>
    public ImageSource? Image { set => _image.Source = value; }

    public void Dispose()
    {
        try { _window.Close(); } catch { }
    }
}
