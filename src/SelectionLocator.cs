// SelectionLocator.cs — Resolves screen coordinates of the copied selection.
//
// Four-tier fallback strategy:
//
//   Tier 1 — UI Automation TextPattern
//     Works for ~50% of apps: Notepad, WordPad, Word, VS, native Win32 text controls.
//     Fails silently for: Chrome, Edge, Electron (sandboxed processes).
//
//   Tier 2 — SelectionItemPattern
//     Finds ALL elements with IsSelected=true. Fixes multi-file selection in Explorer.
//     Works for: File Explorer, ListBox, ListView, TreeView, ComboBox.
//
//   Tier 3 — FocusedElement bounding rect
//     Falls back to the keyboard-focused element's bounding box.
//     Broader app support but only covers the single focused item.
//
//   Tier 4 — Window bounding rect
//     Guaranteed 100% coverage. Glows around the whole window.
using System.Drawing;
using System.Windows.Automation;

namespace HighlightOnCopy;

internal static class SelectionLocator
{
    /// <summary>
    /// Returns screen-coordinate rectangles that cover the current selection
    /// inside <paramref name="ownerHwnd"/>. Multi-line selections and multi-file
    /// selections produce multiple rectangles. Never returns an empty array —
    /// Tier 4 guarantees at least the window bounding rect.
    /// </summary>
    public static Rectangle[] GetSelectionRects(IntPtr ownerHwnd, int clipboardLineCount = 1)
    {
        if (ownerHwnd == IntPtr.Zero)
            return Array.Empty<Rectangle>();

        // Electron/Chromium apps (VSCode, Slack, etc.): Tier 2 misfires on the active
        // TabItem and Tier 3 returns a full editor-line rect instead of the selection.
        // Keep Tier 1 (TextPattern works for standard HTML inputs inside Electron, e.g.
        // the Claude sidebar) and skip straight to window rect when it fails.
        //
        // Extra validation required: VSCode's Monaco editor returns non-null but
        // incorrect rects (content-relative coordinates or single full-width line rects)
        // instead of failing silently. ElectronRectsLookValid rejects these so we fall
        // through to the window bounding rect rather than highlighting the wrong area.
        if (IsElectronWindow(ownerHwnd))
        {
            // ownerHwnd is Chrome_MessageWindow — a message-only clipboard IPC window
            // whose GetWindowRect() returns (0,0,0,0). Use the foreground window (the
            // actual visible app, e.g. Chrome_WidgetWin_1) for bounds comparison and
            // fallback so the glow covers the real VSCode window, not an off-screen tile.
            var visibleHwnd = NativeMethods.GetForegroundWindow();
            var rects = TryTextPattern(ownerHwnd);
            if (rects != null && ElectronRectsLookValid(rects, visibleHwnd, clipboardLineCount))
                return rects;
            return FallbackWindowRect(visibleHwnd != IntPtr.Zero ? visibleHwnd : ownerHwnd);
        }

        return TryTextPattern(ownerHwnd)
            ?? TrySelectionPattern(ownerHwnd)
            ?? TryFocusedElement(ownerHwnd)
            ?? FallbackWindowRect(ownerHwnd);
    }

    // --------------------------------------------
    // ---- Tier 1 — UI Automation TextPattern ----
    // --------------------------------------------

    private static Rectangle[]? TryTextPattern(IntPtr hwnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element == null) return null;

            // Text selections live on the focused editable control, not the top-level window.
            var focused = TryGetFocusedElement() ?? element;

            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var rawPattern))
                return null;

            var textPattern = (TextPattern)rawPattern;
            var selection = textPattern.GetSelection();
            if (selection.Length == 0) return null;

            var rects = new List<Rectangle>();
            foreach (var range in selection)
            {
                // GetBoundingRectangles() returns System.Windows.Rect[] (one per line of text).
                System.Windows.Rect[] bounds = range.GetBoundingRectangles();
                foreach (var wr in bounds)
                {
                    int w = (int)wr.Width;
                    int h = (int)wr.Height;
                    if (w > 0 && h > 0)
                        rects.Add(new Rectangle((int)wr.X, (int)wr.Y, w, h));
                }
            }

            return rects.Count > 0 ? rects.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // ---- Tier 2 — SelectionItemPattern (multi-file / multi-item fix) ----
    // ---------------------------------------------------------------------

    private static Rectangle[]? TrySelectionPattern(IntPtr hwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root == null) return null;

            // Find all visible selected items anywhere in the window's subtree.
            // On Windows 11 Explorer, ListView virtualizes off-screen items, so
            // FindAll with IsOffscreen=false stays fast even in large directories.
            var condition = new AndCondition(
                new PropertyCondition(SelectionItemPattern.IsSelectedProperty, true),
                new PropertyCondition(AutomationElement.IsOffscreenProperty, false));

            var selected = root.FindAll(TreeScope.Descendants, condition);
            if (selected.Count == 0) return null;

            // Explorer always marks several UI-chrome elements as selected regardless of
            // the user's actual file selection:
            //   RadioButton — the active view-mode button ("Details", "Tiles", …)
            //   TabItem     — the current tab in the tab bar
            //   TreeItem    — the current directory in the navigation pane
            // We filter these out, with one exception: if focus is on a TreeItem the user
            // is copying from the navigation pane, so TreeItem should be kept.
            var focused = TryGetFocusedElement();
            bool focusOnTreeItem = focused?.Current.ControlType == ControlType.TreeItem;

            var rects = new List<Rectangle>();
            for (int i = 0; i < selected.Count; i++)
            {
                var ct = selected[i].Current.ControlType;
                if (ct == ControlType.RadioButton || ct == ControlType.TabItem)
                    continue;
                if (ct == ControlType.TreeItem && !focusOnTreeItem)
                    continue;

                var bounds = selected[i].Current.BoundingRectangle;
                if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
                {
                    rects.Add(new Rectangle(
                        (int)bounds.X, (int)bounds.Y,
                        (int)bounds.Width, (int)bounds.Height));
                }
            }

            return rects.Count > 0 ? rects.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------
    // ---- Tier 3 — Focused element bounding rect ----
    // ------------------------------------------------

    private static Rectangle[]? TryFocusedElement(IntPtr hwnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hwnd);
            if (element == null) return null;

            var focused = AutomationElement.FocusedElement ?? element;
            var bounds = focused.Current.BoundingRectangle;

            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                return new[]
                {
                    new Rectangle(
                        (int)bounds.X, (int)bounds.Y,
                        (int)bounds.Width, (int)bounds.Height)
                };
            }
        }
        catch { }

        return null;
    }

    // -------------------------------------------------------------
    // ---- Tier 4 — Window bounding rect (guaranteed fallback) ----
    // -------------------------------------------------------------

    private static Rectangle[] FallbackWindowRect(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return Array.Empty<Rectangle>();

        return new[] { rect.ToRectangle() };
    }

    // -----------------
    // ---- Helpers ----
    // -----------------

    private static AutomationElement? TryGetFocusedElement()
    {
        try { return AutomationElement.FocusedElement; }
        catch { return null; }
    }

    private static bool IsElectronWindow(IntPtr hwnd)
    {
        // GetClipboardOwner() for Electron may return an internal renderer child window
        // (e.g. Chrome_RenderWidgetHostHWND) rather than the top-level Chrome_WidgetWin_1.
        // Walk up to the root window first; for top-level windows GetAncestor returns itself.
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        var target = root != IntPtr.Zero ? root : hwnd;

        var sb = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(target, sb, sb.Capacity);
        // All Chromium/Electron windows share the "Chrome_" prefix:
        //   Chrome_WidgetWin_0/1/2, Chrome_RenderWidgetHostHWND, Chrome_MessagePumpWindow…
        return sb.ToString().StartsWith("Chrome_", StringComparison.Ordinal);
    }

    // Returns true when TextPattern rects from an Electron window look like a genuine
    // selection rather than Monaco's internal viewport artifacts. Chromium's accessibility
    // bridge returns non-null but incorrect data for the Monaco editor:
    //   • content-relative coordinates that land outside the window when scrolled
    //   • a single full-width line rect for only the cursor line of a multi-line selection
    // Standard HTML webview content (Claude sidebar, Copilot chat) passes all checks.
    private static bool ElectronRectsLookValid(Rectangle[] rects, IntPtr visibleHwnd, int clipboardLineCount)
    {
        // Bounds check against the actual visible window (not Chrome_MessageWindow, which
        // has GetWindowRect() = (0,0,0,0) and would reject every real rect).
        if (visibleHwnd != IntPtr.Zero && NativeMethods.GetWindowRect(visibleHwnd, out var raw))
        {
            var win = raw.ToRectangle();
            // Reject rects that land outside the visible window — Monaco may return
            // document-relative coordinates that don't account for scroll position.
            if (rects.Any(r => !r.IntersectsWith(win))) return false;
        }

        // For multi-line clipboard content, require more than 1 rect.
        // Monaco returns only 1 rect (the cursor line) for multi-line text selections;
        // a working webview (Claude sidebar) returns one rect per visual line.
        if (clipboardLineCount > 1 && rects.Length == 1)
            return false;

        // Exclude caret-sized rects (Monaco's hidden textarea for single-line selections).
        long totalArea = rects.Sum(r => (long)r.Width * r.Height);
        return totalArea >= 100;
    }
}
