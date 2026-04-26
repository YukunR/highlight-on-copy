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

    // Last CF_HDROP file list shown in the overlay (sorted). Used to suppress
    // duplicate file-copy highlights caused by Explorer re-writing the clipboard
    // during tab operations (duplicate tab, tab switch) without Ctrl+C/X.
    private IReadOnlyList<string>? _lastShownHdrop;

    public AppContext()
    {
        _trayIcon = new TrayIcon(Application.Exit);
        _clipboardMonitor = new ClipboardMonitor(OnClipboardChanged);
    }

    // ---------------------------------
    // ---- Clipboard event handler ----
    // ---------------------------------
    // Called on the UI thread, ~80ms after WM_CLIPBOARDUPDATE.
    // wasCtrlCX: true when Ctrl+C or Ctrl+X was held at clipboard-write time.

    private void OnClipboardChanged(bool wasCtrlCX)
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

        // ---- Duplicate file-copy suppression ----
        // Explorer re-writes CF_HDROP with the current selection during tab operations
        // (duplicate tab, tab switch) without any Ctrl+C/X keystroke. If the file list
        // is identical to the last overlay we showed and no keyboard copy was detected,
        // treat this as an internal write and skip.
        if (hasFiles && !wasCtrlCX)
        {
            List<string> currentFiles;
            try
            {
                var drop = Clipboard.GetFileDropList();
                currentFiles = new List<string>(drop.Count);
                foreach (string? f in drop)
                    if (f != null) currentFiles.Add(f);
                currentFiles.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { currentFiles = []; }

            if (_lastShownHdrop != null && currentFiles.SequenceEqual(
                    _lastShownHdrop, StringComparer.OrdinalIgnoreCase))
                return;
        }

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

        // Record the file list so we can suppress Explorer's internal re-writes
        // (tab duplication, etc.) that repeat the same CF_HDROP without Ctrl+C/X.
        if (hasFiles)
        {
            try
            {
                var drop = Clipboard.GetFileDropList();
                var saved = new List<string>(drop.Count);
                foreach (string? f in drop)
                    if (f != null) saved.Add(f);
                saved.Sort(StringComparer.OrdinalIgnoreCase);
                _lastShownHdrop = saved;
            }
            catch { _lastShownHdrop = null; }
        }
        else
        {
            _lastShownHdrop = null;
        }
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
