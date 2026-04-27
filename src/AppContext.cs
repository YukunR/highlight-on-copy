// AppContext.cs — ApplicationContext subclass that owns all subsystems and
// wires clipboard events to the overlay display pipeline.
//
// Flow:
//   AddClipboardFormatListener → WM_CLIPBOARDUPDATE → 80ms timer →
//   RateLimiter.ShouldTrigger() → SelectionLocator.GetSelectionRects() →
//   GlowOverlay.ShowOver()
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HighlightOnCopy;

internal sealed class AppContext : ApplicationContext
{
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly RateLimiter _rateLimiter = new();
    private readonly TrayIcon _trayIcon;
    private readonly Control _uiInvoker = new();
    private readonly CancellationTokenSource _pipeCts = new();
    private bool _isPaused;
    private SettingsWindow? _settingsWindow;

    // Last CF_HDROP file list shown in the overlay (sorted). Used to suppress
    // duplicate file-copy highlights caused by Explorer re-writing the clipboard
    // during tab operations (duplicate tab, tab switch) without Ctrl+C/X.
    private IReadOnlyList<string>? _lastShownHdrop;

    public bool IsPaused => _isPaused;

    public AppContext()
    {
        _uiInvoker.CreateControl();   // forces HWND creation before Application.Run()
        _trayIcon = new TrayIcon(Application.Exit, ShowSettingsWindow, TogglePause);
        _clipboardMonitor = new ClipboardMonitor(OnClipboardChanged);
        _ = RunPipeServerAsync(_pipeCts.Token);
    }

    public void TogglePause()
    {
        _isPaused = !_isPaused;
        _trayIcon.UpdatePauseState(_isPaused);
        _settingsWindow?.UpdatePauseState(_isPaused);
    }

    public void ShowSettingsWindow()
    {
        _settingsWindow ??= new SettingsWindow(this);
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async Task RunPipeServerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    Program.PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                var buf = new byte[64];
                int n = await server.ReadAsync(buf.AsMemory(), ct);
                if (Encoding.UTF8.GetString(buf, 0, n) == "SHOW_SETTINGS")
                    _uiInvoker.BeginInvoke(ShowSettingsWindow);
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { /* transient — loop restarts */ }
        }
    }

    // ---------------------------------
    // ---- Clipboard event handler ----
    // ---------------------------------
    // Called on the UI thread, ~80ms after WM_CLIPBOARDUPDATE.
    // wasCtrlCX: true when Ctrl+C or Ctrl+X was held at clipboard-write time.

    private void OnClipboardChanged(bool wasCtrlCX)
    {
        if (_isPaused) return;

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

        // ---- Read file list once ----
        // Build both the sorted path list (for dedup + _lastShownHdrop) and a name set
        // (with and without extension) for SelectionLocator to filter stale UIA entries.
        List<string>? currentFiles = null;
        HashSet<string>? clipboardFileNames = null;
        if (hasFiles)
        {
            try
            {
                var drop = Clipboard.GetFileDropList();
                currentFiles = new List<string>(drop.Count);
                clipboardFileNames = new HashSet<string>(drop.Count * 2, StringComparer.OrdinalIgnoreCase);
                foreach (string? path in drop)
                {
                    if (path == null) continue;
                    currentFiles.Add(path);
                    clipboardFileNames.Add(Path.GetFileName(path));
                    clipboardFileNames.Add(Path.GetFileNameWithoutExtension(path));
                }
                currentFiles.Sort(StringComparer.OrdinalIgnoreCase);
                if (clipboardFileNames.Count == 0) clipboardFileNames = null;
            }
            catch { currentFiles = null; clipboardFileNames = null; }
        }

        // ---- Duplicate file-copy suppression ----
        // Explorer re-writes CF_HDROP with the current selection during tab operations
        // (duplicate tab, tab switch) without any Ctrl+C/X keystroke. If the file list
        // is identical to the last overlay we showed and no keyboard copy was detected,
        // treat this as an internal write and skip.
        if (hasFiles && !wasCtrlCX && currentFiles != null)
        {
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
            rects = SelectionLocator.GetSelectionRects(targetHwnd, clipboardLineCount, clipboardFileNames);
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
        _lastShownHdrop = hasFiles ? currentFiles : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pipeCts.Cancel();
            _pipeCts.Dispose();
            _uiInvoker.Dispose();
            _clipboardMonitor.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
