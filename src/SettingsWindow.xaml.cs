// SettingsWindow.xaml.cs — Settings / control panel shown on tray double-click.
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace HighlightOnCopy;

internal partial class SettingsWindow : Window
{
    private readonly AppContext _appContext;
    private bool _isLoading;
    private bool _reallyClose;

    internal SettingsWindow(AppContext appContext)
    {
        _appContext = appContext;
        InitializeComponent();
        LoadState();
    }

    private void LoadState()
    {
        _isLoading = true;
        UpdatePauseState(_appContext.IsPaused);
        StartupCheckBox.IsChecked = IsStartupEnabled();
        _isLoading = false;
    }

    internal void UpdatePauseState(bool isPaused)
    {
        StatusDot.Fill = isPaused
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00))
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50));
        TogglePauseButton.Content = isPaused ? "恢复" : "暂停";
    }

    private void TogglePauseButton_Click(object sender, RoutedEventArgs e)
        => _appContext.TogglePause();

    private void StartupCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SetStartupEnabled(true);
    }

    private void StartupCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SetStartupEnabled(false);
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
        => System.Windows.Forms.Application.Exit();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_reallyClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    internal void CloseForReal()
    {
        _reallyClose = true;
        Close();
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("HighlightOnCopy") != null;
    }

    private static void SetStartupEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key == null) return;
        if (enable)
        {
            var path = Environment.ProcessPath
                ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(path)) return;
            key.SetValue("HighlightOnCopy", path);
        }
        else
            key.DeleteValue("HighlightOnCopy", throwOnMissingValue: false);
    }
}
