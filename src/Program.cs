// Program.cs — Entry point.
using System.Windows.Forms;

namespace HighlightOnCopy;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // When launched as a dotnet tool, dotnet.exe attaches a console window.
        // Free it immediately so the app appears as a pure tray application.
        NativeMethods.FreeConsole();

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
