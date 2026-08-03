using SapeagleAttendanceConnector.Forms;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector;

internal static class Program
{
    [STAThread]
    static void Main()
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

        var showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "SapeagleAttendanceConnector_ShowEvent");

        Application.ThreadException += (s, e) =>
            MessageBox.Show($"Error: {e.Exception.Message}", "Sapeagle Connector", MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            MessageBox.Show($"Fatal Error: {(e.ExceptionObject as Exception)?.Message}", "Sapeagle Connector", MessageBoxButtons.OK, MessageBoxIcon.Error);

        ApplicationConfiguration.Initialize();

        var configService = new ConfigService();
        var apiService = new ApiService();
        var queueService = new QueueService();
        var checkpointService = new CheckpointService();
        var syncService = new SyncService(apiService, queueService, checkpointService);
        var employeeSyncService = new EmployeeSyncService(apiService, checkpointService);

        using (var splash = new SplashForm())
        {
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
}