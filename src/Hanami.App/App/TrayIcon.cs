using Hanami.Core;
using Hanami.Native;
using WinForms = System.Windows.Forms;

namespace Hanami.App;

/// <summary>The only always-visible piece of Hanami: the notification-area icon and its menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _pause;
    private readonly WinForms.ToolStripMenuItem _status;
    private readonly WinForms.ToolStripMenuItem _petsItem;
    private readonly WinForms.ToolStripMenuItem _theme;
    private readonly WinForms.ToolStripSeparator _themeSep = new();
    private readonly WinForms.ToolStripMenuItem _updateItem;
    private readonly WinForms.ToolStripMenuItem _updateDetails;
    private readonly WinForms.ToolStripSeparator _updateSep = new();
    private readonly System.Drawing.Icon _plainIcon;
    private readonly System.Drawing.Icon _dotIcon;
    private string _statusText = "Running";
    private UpdatePrompt? _prompt;

    public TrayIcon(Action openSettings, Action openUpdate, Action installUpdate, Action togglePause, Action togglePets, Action<string> setThemeMode, Action quit)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("hanami.ico")!;
        _plainIcon = new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
        _dotIcon = WithDot(_plainIcon);
        _status = new WinForms.ToolStripMenuItem("Running") { Enabled = false };
        _pause = new WinForms.ToolStripMenuItem("Pause wallpapers", null, (_, _) => togglePause());
        var menu = new WinForms.ContextMenuStrip();
        var bold = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        // The update items lead the menu while a version is offered. The first one's click is the user's click to install.
        _updateItem = new WinForms.ToolStripMenuItem("Update", null, (_, _) => installUpdate()) { Visible = false, Font = bold };
        _updateDetails = new WinForms.ToolStripMenuItem("View details", null, (_, _) => openUpdate()) { Visible = false };
        _updateSep.Visible = false;
        menu.Items.Add(_updateItem);
        menu.Items.Add(_updateDetails);
        menu.Items.Add(_updateSep);
        menu.Items.Add(new WinForms.ToolStripMenuItem("Open Hanami", null, (_, _) => openSettings()) { Font = bold });
        menu.Items.Add(_status);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(_pause);
        _petsItem = new WinForms.ToolStripMenuItem("Hide pets", null, (_, _) => togglePets()) { Visible = false };
        menu.Items.Add(_petsItem);
        _theme = new WinForms.ToolStripMenuItem("Theme") { Visible = false };
        foreach (var (mode, label) in new[] { ("system", "Follow Windows"), ("light", "Light"), ("dark", "Dark") })
            _theme.DropDownItems.Add(new WinForms.ToolStripMenuItem(label, null, (_, _) => setThemeMode(mode)) { Tag = mode });
        _themeSep.Visible = false;
        menu.Items.Add(_theme);
        menu.Items.Add(_themeSep);
        menu.Items.Add(new WinForms.ToolStripMenuItem("Quit Hanami", null, (_, _) => quit()));

        _icon = new WinForms.NotifyIcon
        {
            Icon = _plainIcon,
            Text = "Hanami",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) openSettings(); };
        _icon.BalloonTipClicked += (_, _) => openUpdate();   // Hanami's only balloons are about the update
    }

    /// <summary>The dot, the two update menu items and the tooltip line while a version is offered (null removes them all).</summary>
    public void ShowUpdate(UpdatePrompt? prompt)
    {
        var dotChanged = (prompt is null) != (_prompt is null);
        _prompt = prompt;
        if (dotChanged) _icon.Icon = prompt is null ? _plainIcon : _dotIcon;   // only on a change: setting it again makes the icon flicker
        _updateItem.Visible = _updateDetails.Visible = _updateSep.Visible = prompt is not null;
        if (prompt is not null)
        {
            _updateItem.Text = prompt.MenuText;
            _updateItem.Enabled = prompt.CanInstall;
        }
        ApplyTip();
    }

    /// <summary>A version's notification and its one reminder; clicking it opens the update in the settings window.</summary>
    public void NotifyUpdate(string version) =>
        _icon.ShowBalloonTip(10_000, "Hanami update available", $"Version {version} is ready. Click to see what's new.", WinForms.ToolTipIcon.Info);

    /// <summary>Says the tray click worked: the download itself shows only in the tooltip and the menu.</summary>
    public void NotifyUpdateStarted(string version) =>
        _icon.ShowBalloonTip(5_000, "Updating Hanami", $"Downloading version {version}. Hanami restarts by itself when it's ready.", WinForms.ToolTipIcon.Info);

    public void NotifyUpdateFailed(string version, string? error) =>
        _icon.ShowBalloonTip(10_000, $"Couldn't update to Hanami {version}", Shorten(error ?? "Something went wrong."), WinForms.ToolTipIcon.Warning);

    private static string Shorten(string text) => text.Length > 200 ? text[..200] + "…" : text;   // a balloon holds under 256 characters

    /// <summary>A copy of the tray icon with the update dot in its corner, ringed so it reads on light and dark taskbars.</summary>
    private static System.Drawing.Icon WithDot(System.Drawing.Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var (x, y, d) = UpdateAttention.DotBounds(bitmap.Width);
            using var ring = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            using var dot = new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromHtml("#E8366F"));   // .nav-dot in web/settings/style.css
            g.FillEllipse(ring, x, y, d - 0.5f, d - 0.5f);
            g.FillEllipse(dot, x + 1, y + 1, d - 2.5f, d - 2.5f);
        }
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)borrowed.Clone();   // the clone owns its own handle
        }
        finally { Win32.DestroyIcon(handle); }
    }

    /// <summary>"Hide pets / Show pets" (this session only) exists while any pet is set up.</summary>
    public void UpdatePets(bool any, bool hidden)
    {
        _petsItem.Visible = any;
        _petsItem.Text = hidden ? "Show pets" : "Hide pets";
    }

    /// <summary>The Theme submenu exists only while light/dark wallpapers are on.</summary>
    public void UpdateTheme(bool split, string mode)
    {
        _theme.Visible = _themeSep.Visible = split;
        foreach (WinForms.ToolStripMenuItem item in _theme.DropDownItems) item.Checked = (string?)item.Tag == mode;
    }

    public void Update(bool paused, string status)
    {
        _pause.Text = paused ? "Resume wallpapers" : "Pause wallpapers";
        _status.Text = status;
        _statusText = status;
        ApplyTip();
    }

    private void ApplyTip() => _icon.Text = UpdateAttention.Tooltip(_statusText, _prompt);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _plainIcon.Dispose();
        _dotIcon.Dispose();
    }
}
