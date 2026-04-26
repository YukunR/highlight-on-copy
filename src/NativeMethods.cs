// NativeMethods.cs — Win32 P/Invoke declarations
// Future migration path: replace DllImport with Microsoft.Windows.CsWin32 source generator
// for better type safety and NativeAOT compatibility.
using System.Runtime.InteropServices;

namespace HighlightOnCopy;

internal static class NativeMethods
{
    // ---- Clipboard ----

    /// <summary>
    /// Registers the given window to receive WM_CLIPBOARDUPDATE when the
    /// clipboard is updated. Supersedes the fragile SetClipboardViewer chain.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>Returns the handle of the window that placed the current data on the clipboard.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetClipboardOwner();

    /// <summary>Returns true if the clipboard contains data in the given format.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsClipboardFormatAvailable(uint format);

    internal const uint CF_UNICODETEXT = 13;
    internal const uint CF_HDROP = 15; // File drop list

    // ---- Window ----

    /// <summary>Returns the handle to the foreground window (the window the user is working in).</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>Returns the class name of the given window.</summary>
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    /// <summary>Walks the parent chain to retrieve an ancestor window.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    internal const uint GA_ROOT = 2; // topmost parent (no parent window above it)

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // ---- Input ----

    /// <summary>
    /// Retrieves the time of the last input event (keyboard, mouse, etc.).
    /// Used to distinguish programmatic clipboard writes from user-initiated copies.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    internal static extern uint GetTickCount();

    /// <summary>
    /// Returns the async state of a virtual key. The high-order bit (0x8000) is set
    /// if the key is currently down at the moment of the call.
    /// </summary>
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    internal const int VK_CONTROL = 0x11;
    internal const int VK_C = 0x43;
    internal const int VK_X = 0x58;

    // ---- DPI ----

    /// <summary>
    /// Makes the process DPI-aware so that screen coordinates from GetWindowRect
    /// and UI Automation match physical pixels, preventing overlay misalignment
    /// on high-DPI monitors. Call before any window creation.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDPIAware();

    // ---- Console ----

    /// <summary>
    /// Detaches the process from its inherited console window.
    /// When running as a dotnet tool, dotnet.exe provides a console. Calling
    /// FreeConsole() immediately at startup hides it so the app behaves like a
    /// pure tray application with no visible terminal window.
    /// </summary>
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeConsole();

    // ---- Structs ----

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
        public System.Drawing.Rectangle ToRectangle() =>
            System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime; // Tick count of last input event
    }

    // ---- Window style constants ----

    /// <summary>Mouse events pass through the window (click-through).</summary>
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    /// <summary>Window does not activate when shown — prevents focus steal.</summary>
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    /// <summary>Window does not appear in the taskbar or Alt+Tab switcher.</summary>
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
}
