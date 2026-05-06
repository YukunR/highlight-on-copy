// ClipboardMonitor.cs — Clipboard change notification via a message-only window.
//
// Design notes:
//   • Uses AddClipboardFormatListener (Vista+) — the modern replacement for the
//     fragile SetClipboardViewer chain that required each listener to cooperate.
//   • A NativeWindow subclass acts as the message-only window (HWND_MESSAGE).
//     This avoids creating a visible Form and keeps memory overhead minimal.
//   • Windows 11 known issue: accessing the clipboard directly inside the
//     WM_CLIPBOARDUPDATE handler occasionally fails with Access Denied due to
//     timing/synchronisation in the new clipboard architecture. A 80ms WinForms
//     timer defers actual clipboard access, which reliably avoids the race.
using System.Windows.Forms;

namespace HighlightOnCopy;

internal sealed class ClipboardMonitor : NativeWindow, IDisposable
{
    // WM_CLIPBOARDUPDATE — sent to all listeners registered via AddClipboardFormatListener.
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    // HWND_MESSAGE — special parent handle that creates a message-only (non-visible) window.
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly System.Windows.Forms.Timer _delayTimer;
    private readonly Action<bool> _onClipboardChanged;
    private bool _lastWasCtrlCX;
    private bool _disposed;

    /// <param name="onClipboardChanged">
    /// Callback fired ~80ms after a clipboard update. Always called on the UI thread.
    /// The bool parameter is true when Ctrl+C or Ctrl+X was held at the moment the
    /// clipboard was written (keyboard copy), false for programmatic or mouse-only writes.
    /// </param>
    public ClipboardMonitor(Action<bool> onClipboardChanged)
    {
        _onClipboardChanged = onClipboardChanged;

        // Create the message-only window. NativeWindow.CreateHandle wires up WndProc
        // to the Windows message pump that Application.Run() drives.
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });

        if (!NativeMethods.AddClipboardFormatListener(Handle))
            throw new InvalidOperationException(
                $"AddClipboardFormatListener failed (Handle={Handle}, " +
                $"Win32Error={System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");

        // WinForms Timer runs on the UI thread — safe to update UI from its Tick handler.
        _delayTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _delayTimer.Tick += (_, _) =>
        {
            _delayTimer.Stop();
            _onClipboardChanged(_lastWasCtrlCX);
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE)
        {
            // Capture keyboard state immediately before the 80ms delay, while the
            // keys are still physically held. Used to distinguish Ctrl+C/X (keyboard
            // copy) from programmatic clipboard writes (e.g. Explorer tab duplication).
            bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
            bool c = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_C) & 0x8000) != 0;
            bool x = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_X) & 0x8000) != 0;
            _lastWasCtrlCX = ctrl && (c || x);

            // Restart the 80ms delay on every clipboard update. If the app sends
            // multiple rapid updates (e.g. a rich-text editor copying both
            // CF_RTF and CF_UNICODETEXT), we only trigger once after they settle.
            _delayTimer.Stop();
            _delayTimer.Start();
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _delayTimer.Stop();
        _delayTimer.Dispose();

        if (Handle != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(Handle);
            DestroyHandle();
        }
    }
}
