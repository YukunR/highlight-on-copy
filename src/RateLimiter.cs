// RateLimiter.cs — Guards against false triggers from programmatic clipboard writes.
//
// A clipboard write that should trigger the overlay must pass ALL of these checks:
//
//   1. Minimum interval: at least 800ms since the last successful trigger.
//      Prevents cascading flashes when an app writes multiple clipboard formats
//      in quick succession (though ClipboardMonitor's 80ms delay helps too).
//
//   2. Recent user input: GetLastInputInfo must show activity within 250ms.
//      Password managers, clipboard history tools, and background services
//      routinely update the clipboard while the user is idle. This check
//      rejects those writes.
//
// Note: This heuristic is intentionally conservative. A user who copies with
// a keyboard shortcut will always satisfy both conditions. A programmatic write
// that happens to coincide with recent mouse movement might slip through, but
// that is acceptable (a rare, harmless false positive is better than missing
// legitimate copies).
using System.Runtime.InteropServices;

namespace HighlightOnCopy;

internal sealed class RateLimiter
{
    /// <summary>Minimum time between consecutive triggers.</summary>
    private const uint MinIntervalMs = 800;

    /// <summary>User must have produced input within this many milliseconds.</summary>
    private const uint MaxInputIdleMs = 250;

    private uint _lastTriggerTick;

    public bool ShouldTrigger()
    {
        uint now = NativeMethods.GetTickCount();

        // ---- Check 1: Minimum interval ----
        // unchecked subtraction handles the 49.7-day GetTickCount wrap-around.
        if (unchecked(now - _lastTriggerTick) < MinIntervalMs)
            return false;

        // ---- Check 2: Recent user activity ----
        var lii = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };

        if (NativeMethods.GetLastInputInfo(ref lii))
        {
            uint idleMs = unchecked(now - lii.dwTime);
            if (idleMs > MaxInputIdleMs)
                return false; // User has been idle — likely a programmatic write
        }
        // If GetLastInputInfo fails (rare), we allow the trigger conservatively.

        _lastTriggerTick = now;
        return true;
    }
}
