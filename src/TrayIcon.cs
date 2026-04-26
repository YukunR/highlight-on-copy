// TrayIcon.cs — System-tray notification icon with a minimal right-click menu.
using System.Drawing;
using System.Windows.Forms;

namespace HighlightOnCopy;

internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayIcon(Action onExit)
    {
        var menu = new ContextMenuStrip();
        // Title item (non-clickable header)
        var title = new ToolStripMenuItem("Highlight on Copy") { Enabled = false };
        menu.Items.Add(title);
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

        // Double-clicking the tray icon also exits (convenient during development).
        _notifyIcon.DoubleClick += (_, _) => onExit();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
