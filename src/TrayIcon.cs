// TrayIcon.cs — System-tray notification icon with a right-click menu.
using System.Drawing;
using System.Windows.Forms;

namespace HighlightOnCopy;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;

    public TrayIcon(Action onExit, Action onShowSettings, Action onTogglePause)
    {
        _pauseItem = new ToolStripMenuItem("Pause", image: null, onClick: (_, _) => onTogglePause());

        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem("Highlight on Copy") { Enabled = false };
        menu.Items.Add(title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", image: null, onClick: (_, _) => onShowSettings());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", image: null, onClick: (_, _) => onExit());

        _notifyIcon = new NotifyIcon
        {
            // Use a built-in system icon as a placeholder.
            // Replace with a custom .ico resource before release.
            Icon = SystemIcons.Information,
            Text = "Highlight on Copy — running",
            Visible = true,
            ContextMenuStrip = menu,
        };

        // Double-clicking opens the settings window.
        _notifyIcon.DoubleClick += (_, _) => onShowSettings();
    }

    public void UpdatePauseState(bool isPaused)
    {
        _pauseItem.Text = isPaused ? "Resume" : "Pause";
        _notifyIcon.Text = isPaused
            ? "Highlight on Copy — paused"
            : "Highlight on Copy — running";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
