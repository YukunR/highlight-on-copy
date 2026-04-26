// AppContext.cs — ApplicationContext subclass that owns all subsystems and
// wires clipboard events to the overlay display pipeline.
//
// Flow:
//   AddClipboardFormatListener → WM_CLIPBOARDUPDATE → 80ms timer →
//   RateLimiter.ShouldTrigger() → SelectionLocator.GetSelectionRects() →
//   GlowOverlay.ShowOver()
using System.Windows.Forms;

namespace HighlightOnCopy;

internal sealed class AppContext : ApplicationContext
{
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly RateLimiter _rateLimiter = new();
    private readonly TrayIcon _trayIcon;

    public AppContext()
    {
        _trayIcon = new TrayIcon(Application.Exit);
        _clipboardMonitor = new ClipboardMonitor(OnClipboardChanged);
    }

    // ---------------------------------
    // ---- Clipboard event handler ----
    // ---------------------------------
    // Called on the UI thread, ~80ms after WM_CLIPBOARDUPDATE.

    private void OnClipboardChanged()
    {
        // ---- Rate-limit + idle check ----
        if (!_rateLimiter.ShouldTrigger())
            return;

        // ---- Clipboard format check ----
        // We only react to text or file copies; ignore clipboard writes from
        // password managers, clipboard history tools, etc. that use exotic formats.
        bool hasText = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT);
        bool hasFiles = NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP);
        if (!hasText && !hasFiles)
            return;

        // ---- Source window ----
        // Strategy: use GetForegroundWindow() captured here (≈80ms after the
        // copy). The foreground window has returned to the source app by now —
        // any right-click context menu has already closed.
        //
        // Secondary: GetClipboardOwner() identifies the window that actually
        // called SetClipboardData. We prefer the foreground window for text
        // selection because the clipboard owner of rich-text editors is often
        // an internal process handle that UI Automation cannot navigate to.
        var fgHwnd = NativeMethods.GetForegroundWindow();
        var ownerHwnd = NativeMethods.GetClipboardOwner();

        // File copies (CF_HDROP): selected files live in the foreground Explorer window.
        // GetClipboardOwner() for file drops returns an internal Shell message window,
        // not the visible file list — using it would make SelectionLocator search the
        // wrong UIA subtree and find 0 selected items.
        //
        // Text copies (CF_UNICODETEXT): prefer the clipboard owner so right-click
        // "Copy" scenarios work correctly (the context menu has already closed by now
        // and the clipboard owner still points to the source document window).
        var targetHwnd = hasFiles
            ? fgHwnd
            : (ownerHwnd != IntPtr.Zero ? ownerHwnd : fgHwnd);

        if (targetHwnd == IntPtr.Zero)
            return;

        // ---- Clipboard line count (for Electron validation) ----
        // Count lines in the copied text so SelectionLocator can detect when
        // Monaco's TextPattern returns only 1 rect for a multi-line selection.
        int clipboardLineCount = 1;
        if (hasText)
        {
            try
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    var trimmed = text.TrimEnd('\n', '\r');
                    clipboardLineCount = Math.Max(1, trimmed.Split('\n').Length);
                }
            }
            catch { }
        }

        // ---- Selection rectangles ----
        // Runs synchronously on the UI thread. UI Automation calls are usually
        // fast (<10ms for native apps). If an app is slow to respond, this
        // will block the UI briefly — acceptable for MVP; a Task.Run wrapper
        // with timeout can be added in a future iteration.
        Rectangle[] rects;
        try
        {
            rects = SelectionLocator.GetSelectionRects(targetHwnd, clipboardLineCount);
        }
        catch
        {
            // SelectionLocator is defensive, but catch any unexpected exceptions
            // to ensure the monitor stays running.
            return;
        }

        if (rects.Length == 0)
            return;

        // ---- Show glow overlay ----
        GlowOverlay.ShowOver(rects);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clipboardMonitor.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
