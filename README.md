# highlight-on-copy

A system-wide Windows utility that highlights your copied selection with a soft blue glow whenever you press Ctrl+C — so you always know the copy succeeded.

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512bd4?style=flat-square)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/platform-Windows-0078d7?style=flat-square)](https://www.microsoft.com/windows)
[![MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![Version](https://img.shields.io/badge/version-0.1.0-orange?style=flat-square)](https://github.com/YukunR/highlight-on-copy/releases)

<div align="center">

<!-- TODO: Replace with actual demo GIF
     Recommended: screen-record a Notepad copy showing the blue glow fade (~350ms)
![demo](docs/demo.gif)
-->

</div>

## Features

- **Instant copy confirmation** — soft blue glow highlights the copied selection and fades out in ~350ms
- **System-wide monitoring** — works across any app: Notepad, Word, Visual Studio, File Explorer, and more
- **Four-tier fallback for selection location** — UI Automation TextPattern → SelectionItemPattern → focused element → window bounding rect
- **Rate limiting** — 800ms cooldown plus a 250ms recent-input check, filtering out programmatic clipboard writes from password managers and background services
- **Per-monitor DPI-aware** (PerMonitorV2) — correct overlay placement on HiDPI displays and multi-monitor setups
- **Zero external NuGet dependencies** — only .NET 10 inbox libraries (WinForms + UI Automation)
- **System tray app** — runs silently in the background with a right-click Exit menu

## Installation

### Via dotnet tool

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```sh
# Install
dotnet tool install -g HighlightOnCopy

# Run (if not starting automatically)
highlight-on-copy

# Uninstall
dotnet tool uninstall -g HighlightOnCopy
```

To launch automatically on login, add `highlight-on-copy` to your Windows startup items
(e.g. `shell:startup` shortcut or Task Scheduler).

### Build from Source

Prerequisites: .NET 10 SDK · Windows 10 version 1803 or later · x64

```sh
git clone https://github.com/YukunR/highlight-on-copy.git
cd highlight-on-copy
dotnet run

# Or publish as a self-contained executable:
dotnet publish -c Release
```

## Usage

After launch the app runs silently in the system tray. Copy anything in any application — the blue glow appears over the selection and fades within about 350ms.

| Action                 | Effect               |
| ---------------------- | -------------------- |
| Single-click tray icon | No action            |
| Right-click → Exit     | Quit the application |
| Double-click tray icon | Quit the application |

Only `CF_UNICODETEXT` (text) and `CF_HDROP` (file) clipboard formats trigger the overlay. Writes from password managers, clipboard history tools, and other background services are filtered out by the rate limiter.

## How It Works

When a copy occurs, Windows broadcasts `WM_CLIPBOARDUPDATE`. The app intercepts that message, validates it is a genuine user action, locates the copied selection on screen, and renders a fading overlay directly over it.

```
WM_CLIPBOARDUPDATE
  └─ ClipboardMonitor (80ms debounce)
       └─ RateLimiter.ShouldTrigger() — 800ms cooldown + recent input check
            └─ Format check: CF_UNICODETEXT or CF_HDROP only
                 └─ SelectionLocator.GetSelectionRects()
                      ├─ Tier 1: UI Automation TextPattern  (Notepad, Word, VS)
                      ├─ Tier 2: SelectionItemPattern       (Explorer multi-file, skipped for Electron)
                      ├─ Tier 3: Focused element bounding rect
                      └─ Tier 4: Window bounding rect       (guaranteed fallback)
                           └─ GlowOverlay.ShowOver()
                           └─ 60fps fade over 350ms → self-dispose
```

**ClipboardMonitor** — uses the modern Vista+ `AddClipboardFormatListener` API (not the fragile `SetClipboardViewer` chain) and applies an 80ms debounce timer. The delay is necessary on Windows 11 because the clipboard owner window is not yet set at the moment `WM_CLIPBOARDUPDATE` fires.

**SelectionLocator** — Electron-based apps (VSCode, Chrome, Slack, Discord) are detected by their `Chrome_WidgetWin_1` window class and handled specially: Tier 2 (`SelectionItemPattern`) is skipped because it would incorrectly fire on browser tabs, and the locator falls back to Tier 1 then Tier 4.

**GlowOverlay** — a `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` WinForms window that covers the union of all selection rectangles. Uses `TransparencyKey = Fuchsia` for the transparent regions and drives a 60fps fade via a 16ms `System.Windows.Forms.Timer`. The window self-disposes when the animation completes.

## Limitations

- **Chrome / Edge / Electron apps** (VSCode, Slack, Discord, etc.) — the sandboxed renderer blocks UI Automation from reading the text selection, so the overlay falls back to highlighting the entire window rather than the exact copied text.
- **Requires Windows 10 version 1803 or later** — needed for `AddClipboardFormatListener`.
- **x64 only.**

## Contributing

Issues and pull requests are welcome at [github.com/YukunR/highlight-on-copy](https://github.com/YukunR/highlight-on-copy/issues).


## License

MIT © highlight-on-copy contributors — see [LICENSE](LICENSE) for details.
