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

    public TrayIcon(Action openSettings, Action openUpdate, Action togglePause, Action togglePets, Action<string> setThemeMode, Action quit)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("hanami.ico")!;
        _status = new WinForms.ToolStripMenuItem("Running") { Enabled = false };
        _pause = new WinForms.ToolStripMenuItem("Pause wallpapers", null, (_, _) => togglePause());
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem("Open Hanami", null, (_, _) => openSettings()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(_status);
        _updateItem = new WinForms.ToolStripMenuItem("Update", null, (_, _) => openUpdate()) { Visible = false };
        menu.Items.Add(_updateItem);
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
            Icon = new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize),
            Text = "Hanami",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) openSettings(); };
        _icon.BalloonTipClicked += (_, _) => openUpdate();   // the only balloon Hanami shows is the update one
    }

    /// <summary>"Update to x.y.z" stays in the menu while an update is available (null hides it).</summary>
    public void UpdateAvailable(string? version)
    {
        _updateItem.Visible = version is not null;
        if (version is not null) _updateItem.Text = $"Update to {version}";
    }

    /// <summary>The once-per-version notification; clicking it opens the update in the settings window.</summary>
    public void NotifyUpdate(string version) =>
        _icon.ShowBalloonTip(10_000, "Hanami update available", $"Version {version} is ready. Click to see what's new.", WinForms.ToolTipIcon.Info);

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
        var tip = $"Hanami — {status}";
        _icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
