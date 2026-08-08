using SapeagleAttendanceConnector.Forms;
using SapeagleAttendanceConnector.Services;
using Microsoft.Win32;
using System.Diagnostics;

namespace SapeagleAttendanceConnector;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "SapeagleAttendanceConnector_SingleInstance", out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Already running -> signal the existing instance to show itself, then exit
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting("SapeagleAttendanceConnector_ShowEvent");
                showEvent.Set();
            }
            catch { /* existing instance couldn't be signaled, ignore */ }
            return;
        }

        bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        var showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "SapeagleAttendanceConnector_ShowEvent");

        Application.ThreadException += (s, e) =>
            MessageBox.Show($"Error: {e.Exception.Message}", "Sapeagle Connector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            MessageBox.Show($"Fatal Error: {(e.ExceptionObject as Exception)?.Message}", "Sapeagle Connector", MessageBoxButtons.OK, MessageBoxIcon.Error);

        ApplicationConfiguration.Initialize();

        EnsureAutoStart();

        var configService = new ConfigService();
        var apiService = new ApiService();
        var queueService = new QueueService();
        var checkpointService = new CheckpointService();
        var syncService = new SyncService(apiService, queueService, checkpointService);
        var employeeSyncService = new EmployeeSyncService(apiService, checkpointService);

        if (!startMinimized)
        {
            using var splash = new SplashForm();
            splash.Show();
            Application.DoEvents();
            System.Threading.Thread.Sleep(800);
        }

        var company = configService.Load();

        while (!company.IsActivated)
        {
            var activationForm = new CompanyActivationForm(apiService, configService);
            var dr = activationForm.ShowDialog();

            if (dr != DialogResult.OK && dr != DialogResult.Retry)
            {
                Application.Exit();
                return;
            }

            company = configService.Load();
        }

        var dashboard = new DashboardForm(company, apiService, configService, syncService, employeeSyncService, checkpointService);

        if (startMinimized)
        {
            // Hide during Load so the window never actually flashes on screen;
            // the tray icon (created in DashboardForm's constructor) stays visible.
            dashboard.Load += (_, _) => dashboard.Hide();
        }

        var listenerThread = new System.Threading.Thread(() =>
        {
            while (true)
            {
                showRequestEvent.WaitOne();
                dashboard.BeginInvoke(() =>
                {
                    dashboard.Show();
                    dashboard.WindowState = FormWindowState.Normal;
                    dashboard.Activate();
                    dashboard.BringToFront();
                });
            }
        })
        { IsBackground = true };
        listenerThread.Start();

        Application.Run(dashboard);
    }

    /// <summary>
    /// Registers this exe to auto-launch (minimized, straight to tray) whenever the
    /// current Windows user logs in, via HKCU\...\Run. No admin rights required.
    /// Safe to call every startup: it only writes the registry when the path/args
    /// differ from what's already there (e.g. after the exe moves or updates).
    /// </summary>
    private static void EnsureAutoStart()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule!.FileName;
            string desired = $"\"{exePath}\" --minimized";

            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

            var existing = key?.GetValue("SapeagleAttendanceConnector") as string;

            if (!string.Equals(existing, desired, StringComparison.OrdinalIgnoreCase))
            {
                key?.SetValue("SapeagleAttendanceConnector", desired);
                Logger.Log("[Startup] Auto-start registry entry added/updated.");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[Startup] Could not register auto-start: {ex.Message}");
        }
    }
}