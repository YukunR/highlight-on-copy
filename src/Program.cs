// Program.cs — Entry point.
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HighlightOnCopy;

internal static class Program
{
    // Session-scoped so that each user on a multi-session (RDS/fast-switch) host
    // gets an independent instance guard and IPC channel.
    private static readonly int _sessionId = Process.GetCurrentProcess().SessionId;
    internal static readonly string MutexName = $"Local\\HighlightOnCopy-{_sessionId}";
    internal static readonly string PipeName = $"HighlightOnCopy-{_sessionId}";

    [STAThread]
    static void Main()
    {
        // When launched as a dotnet tool, dotnet.exe attaches a console window.
        // Free it immediately so the app appears as a pure tray application.
        NativeMethods.FreeConsole();

        // Single-instance guard: only one copy may run per user session.
        // The mutex is held for the lifetime of Application.Run() below.
        bool createdNew;
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        if (!createdNew)
        {
            // Another instance is already running — tell it to show its settings window.
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(2000);
                pipe.Write(Encoding.UTF8.GetBytes("SHOW_SETTINGS"));
            }
            catch { /* running instance may be starting up; fail silently */ }
            return;
        }

        // Make the process DPI-aware so that GetWindowRect and UI Automation
        // bounding rectangles are in physical pixels. Without this, on a 150%
        // scaled display all coordinates would be in logical pixels and the
        // overlay would appear at the wrong position/size.
        //
        // SetProcessDPIAware is the simple pre-Vista API; on Windows 10+ the
        // preferred approach is Application.SetHighDpiMode(PerMonitorV2), which
        // WinForms automatically calls on net6+ windows TFMs. Both are kept here
        // for belt-and-suspenders safety.
        NativeMethods.SetProcessDPIAware();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var ctx = new AppContext();
        Application.Run(ctx);
    }
}
