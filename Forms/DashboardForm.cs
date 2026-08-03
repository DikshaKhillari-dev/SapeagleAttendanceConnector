using SapeagleAttendanceConnector.Models;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.Forms;

public class DashboardForm : Form
{
    private readonly CompanyConfig _company;
    private readonly ApiService _apiService;
    private readonly ConfigService _configService;
    private readonly SyncService _syncService;
    private readonly EmployeeSyncService _employeeSyncService;
    private readonly CheckpointService _checkpointService;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60000 };
    private readonly NotifyIcon _trayIcon;

    private readonly HeaderBanner _header = new() { Title = "Sapeagle Attendance Connector" };
    private readonly Badge _connectionBadge = new() { Text = "Connected" };

    private readonly DotIndicator _statusDot = new() { Size = new Size(12, 12) };
    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Fill,
        Font = Theme.FontHeading,
        ForeColor = Theme.TextPrimary,
        BackColor = Theme.Surface,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private readonly Label _lblLastSync = new()
    {
        Dock = DockStyle.Top,
        Height = 22,
        Font = Theme.FontSmall,
        ForeColor = Theme.TextSecondary
    };

    private readonly Button _btnSyncNow = new() { Text = "Sync Now" };
    private readonly Button _btnSyncEmployees = new() { Text = "Sync Employees" };
    private readonly Button _btnMapUsers = new() { Text = "Map Users" };
    private readonly Button _btnExit = new() { Text = "Exit" };
    // Reset Checkpoint button intentionally hidden from the UI (kept out of layout/tray menu
    // below) - resetting a checkpoint forces the next sync to re-read a device's old backlog,
    // which re-inserts already-synced records as duplicates. RunResetCheckpointAsync() is kept
    // in the code below, unused, in case a support workflow needs it again later.

    public DashboardForm(
    CompanyConfig company,
    ApiService apiService,
    ConfigService configService,
    SyncService syncService,
    EmployeeSyncService employeeSyncService,
    CheckpointService checkpointService)
    {
        _company = company;
        _apiService = apiService;
        _configService = configService;
        _syncService = syncService;
        _employeeSyncService = employeeSyncService;
        _checkpointService = checkpointService;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Sapeagle Attendance Connector";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(640, 420);
        Size = new Size(820, 480);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = Theme.Background;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        _header.Subtitle = "Company : " + string.Join(", ", _company.Machines.Select(m => m.CompanyName).Distinct());

        _header.Controls.Add(_connectionBadge);
        _connectionBadge.BringToFront();
        _header.Resize += (_, _) => PositionConnectionBadge();
        PositionConnectionBadge();
        var body = BuildBody();

        Controls.Add(body);
        Controls.Add(_header);

        _btnSyncNow.Click += async (_, _) => await RunSyncAsync();
        _btnSyncEmployees.Click += async (_, _) => await RunEmployeeSyncAsync();
        _btnMapUsers.Click += async (_, _) => await RunMapUsersAsync();
        _btnExit.Click += (_, _) => { _trayIcon.Visible = false; Application.Exit(); };

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Sapeagle Attendance Connector",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; };

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; });
        trayMenu.Items.Add("Sync Now", null, async (_, _) => await RunSyncAsync());
        trayMenu.Items.Add("Sync Employees", null, async (_, _) => await RunEmployeeSyncAsync());
        trayMenu.Items.Add("Map Users", null, async (_, _) => await RunMapUsersAsync());
        trayMenu.Items.Add("Exit", null, (_, _) => { _trayIcon.Visible = false; Application.Exit(); });
        _trayIcon.ContextMenuStrip = trayMenu;

        _syncService.StatusChanged += msg => BeginInvoke(() => SetStatus(msg));
        _employeeSyncService.StatusChanged += msg => BeginInvoke(() => SetStatus(msg));

        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };

        Shown += (_, _) =>
        {
            var workingArea = Screen.FromControl(this).WorkingArea;
            MaximizedBounds = workingArea;
            if (WindowState != FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            Bounds = workingArea;
        };

        SetStatus("Initializing...");
        _lblLastSync.Text = "Last sync : —";

        _timer.Tick += async (_, _) => await RunSyncAsync();
        Load += async (_, _) => { await FastForwardCheckpointsOnceAsync(); _timer.Start(); await RunSyncAsync(); };
    }

    private Panel BuildBody()
    {
        var statusCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(22) };

        var statusHeaderRow = new BufferedPanel { Dock = DockStyle.Top, Height = 26, Padding = new Padding(20, 0, 0, 0) };
        _statusDot.BackColor = Theme.Success;
        _statusDot.Location = new Point(0, 7);
        statusHeaderRow.Controls.Add(_statusDot);
        statusHeaderRow.Controls.Add(_lblStatus);

        // Bottom-to-top add order (see EmployeeSyncForm notes).
        statusCard.Controls.Add(_lblLastSync);
        statusCard.Controls.Add(statusHeaderRow);

        var actionsCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(22), Margin = new Padding(0, 16, 0, 0) };
        var actionsLabel = new Label
        {
            Text = "QUICK ACTIONS",
            Dock = DockStyle.Top,
            Height = 22,
            Font = Theme.FontSmallBold,
            ForeColor = Theme.TextSecondary
        };

        Theme.StylePrimaryButton(_btnSyncNow);
        Theme.StylePrimaryButton(_btnSyncEmployees);
        Theme.StyleSecondaryButton(_btnMapUsers);
        Theme.StyleSecondaryButton(_btnExit);
        foreach (var b in new[] { _btnSyncNow, _btnSyncEmployees, _btnMapUsers, _btnExit })
            Theme.ApplyRoundedCorners(b, 8);

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 0)
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        buttonRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var actionButtons = new[] { _btnSyncNow, _btnSyncEmployees, _btnMapUsers, _btnExit };
        foreach (var b in actionButtons)
        {
            b.AutoSize = false;
            b.Dock = DockStyle.Fill;
            b.Margin = new Padding(0, 0, 12, 12);
        }
        buttonRow.Controls.Add(actionButtons[0], 0, 0);
        buttonRow.Controls.Add(actionButtons[1], 1, 0);
        buttonRow.Controls.Add(actionButtons[2], 2, 0);
        buttonRow.Controls.Add(actionButtons[3], 3, 0);

        // Bottom-to-top: buttonRow added first, then label above it.
        actionsCard.Controls.Add(buttonRow);
        actionsCard.Controls.Add(actionsLabel);

        var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = Theme.Background };
        stack.Controls.Add(statusCard, 0, 0);
        stack.Controls.Add(actionsCard, 0, 1);

        var centered = new CenteredColumn(720) { Padding = new Padding(0, 24, 0, 24) };
        centered.Content = stack;

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        scrollHost.Controls.Add(centered);
        return scrollHost;
    }

    private void SetStatus(string message)
    {
        _lblStatus.Text = message;
        var lower = message.ToLowerInvariant();
        _statusDot.BackColor = lower.Contains("fail") || lower.Contains("error") || lower.Contains("could not")
            ? Theme.Danger
            : lower.Contains("syncing") || lower.Contains("initializ")
                ? Theme.Warning
                : Theme.Success;

        _lblStatus.Parent?.Invalidate(true);
        _lblStatus.Invalidate();
        _statusDot.Invalidate();
    }

    private async Task RunSyncAsync()
    {
        SetStatus("Syncing...");
        var machineIds = _company.Machines.Select(m => m.MachineId).ToList();
        Logger.Log($"[Dashboard] RunSyncAsync: company has {_company.Machines.Count} activated machine(s), MachineIds=[{string.Join(", ", machineIds)}]");
        await _syncService.RunCycleAsync(machineIds);
        _lblLastSync.Text = $"Last sync : {_syncService.LastSyncTime:hh:mm tt}";
    }

    private async Task RunEmployeeSyncAsync()
    {
        _btnSyncEmployees.Enabled = false;
        try
        {
            if (_company.Machines.Count == 0)
            {
                MessageBox.Show(this, "No machines activated.", "Employee Sync",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var machines = new List<MachineConfig>();
            foreach (var m in _company.Machines)
            {
                Logger.Log($"[EmployeeSync] Fetching machine details for MachineId={m.MachineId} ('{m.MachineName}')...");
                var mc = await _apiService.GetMachineAsync(m.MachineId);
                if (mc != null)
                {
                    Logger.Log($"[EmployeeSync] MachineId={m.MachineId}: fetched '{mc.MachineName}' DeviceId(raw)='{mc.DeviceId}' " +
                               $"MachineType='{mc.MachineType}' Ip={mc.IpAddress}:{mc.Port} IsActive={mc.IsActive}");
                    machines.Add(mc);
                }
                else
                {
                    Logger.Log($"[EmployeeSync] MachineId={m.MachineId} ('{m.MachineName}'): GetMachineAsync returned null, skipped.");
                }
            }

            if (machines.Count == 0)
            {
                MessageBox.Show(this, "Could not fetch activated machine details.", "Employee Sync",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (var machine in machines)
            {
                if (!int.TryParse(machine.DeviceId, out int machineNumber))
                {
                    Logger.Log($"[EmployeeSync] '{machine.MachineName}' has invalid Device ID '{machine.DeviceId}', skipped.");
                    continue;
                }

                Logger.Log($"[EmployeeSync] '{machine.MachineName}' DeviceId(raw)='{machine.DeviceId}' parsed -> machineNumber={machineNumber}");

                _syncService.DisconnectMachine(machine.Id);

                var preview = await _employeeSyncService.PrepareAsync(machine, machineNumber);
                if (preview == null)
                {
                    MessageBox.Show(this,
                        $"Could not connect to '{machine.MachineName}' ({machine.IpAddress}).",
                        "Employee Sync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }

                try
                {
                    using var dialog = new EmployeeSyncForm(
                     preview.DeviceLabel, preview.MachineEmployees, preview.ErpEmployees,
                     _apiService, machine.ComId, preview.Provider);

                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        SetStatus($"Employee sync cancelled for {preview.DeviceLabel}.");
                        continue;
                    }

                    SetStatus($"Syncing employees for {preview.DeviceLabel}... this can take a while, please wait.");
                    var result = await Task.Run(() =>
                        _employeeSyncService.Execute(preview, false, dialog.SelectedEmployees));

                    using var resultForm = new EmployeeSyncResultForm(result);
                    resultForm.ShowDialog(this);
                }
                finally
                {
                    preview.Provider.Disconnect();
                    preview.Provider.Dispose();
                }
            }
        }
        finally
        {
            _btnSyncEmployees.Enabled = true;
        }
    }

    private async Task RunMapUsersAsync()
    {
        _btnMapUsers.Enabled = false;
        try
        {
            if (_company.Machines.Count == 0)
            {
                MessageBox.Show(this, "No machines activated.", "Map Existing Users",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var machines = new List<MachineConfig>();
            foreach (var m in _company.Machines)
            {
                var mc = await _apiService.GetMachineAsync(m.MachineId);
                if (mc != null) machines.Add(mc);
            }

            if (machines.Count == 0)
            {
                MessageBox.Show(this, "Could not fetch activated machine details.", "Map Existing Users",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            foreach (var machine in machines)
            {
                if (!int.TryParse(machine.DeviceId, out int machineNumber))
                {
                    Logger.Log($"[MapUsers] '{machine.MachineName}' has invalid Device ID '{machine.DeviceId}', skipped.");
                    continue;
                }

                _syncService.DisconnectMachine(machine.Id);

                var preview = await _employeeSyncService.PrepareAsync(machine, machineNumber);
                if (preview == null)
                {
                    MessageBox.Show(this,
                        $"Could not connect to '{machine.MachineName}' ({machine.IpAddress}).",
                        "Map Existing Users", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }

                try
                {
                    using var dialog = new MapExistingUsersForm(
                        preview.DeviceLabel, preview.MachineEmployees, preview.ErpEmployees,
                        _apiService, machine.ComId, machine.MachineType);
                    dialog.ShowDialog(this);
                }
                finally
                {
                    preview.Provider.Disconnect();
                    preview.Provider.Dispose();
                }
            }
        }
        finally
        {
            _btnMapUsers.Enabled = true;
        }
    }

    /// <summary>
    /// Runs once, ever, on this machine. As of this update, whatever backlog exists on
    /// each activated device has already been synced (and any duplicates already cleaned
    /// up in the database) - so every activated device's checkpoint is fast-forwarded to
    /// "now" here, meaning the very next sync only pulls punches that happen after this
    /// moment instead of re-reading the device's old backlog and creating duplicates again.
    /// A marker file makes sure this only ever runs the first time the app starts after
    /// this update; later runs are no-ops.
    /// </summary>
    private async Task FastForwardCheckpointsOnceAsync()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SapeagleAttendanceConnector", "Data");
        Directory.CreateDirectory(dataDir);
        var flagPath = Path.Combine(dataDir, "checkpoint-fastforward-applied.flag");
        if (File.Exists(flagPath)) return;

        foreach (var m in _company.Machines)
        {
            try
            {
                var mc = await _apiService.GetMachineAsync(m.MachineId);
                if (mc == null)
                {
                    Logger.Log($"[Checkpoint] FastForward: MachineId={m.MachineId} - GetMachineAsync returned null, skipped.");
                    continue;
                }

                var deviceKey = $"{mc.MachineType}:{mc.IpAddress}:{mc.DeviceId}";
                _checkpointService.MarkSyncedUpToNow(deviceKey);
                Logger.Log($"[Checkpoint] FastForward: '{mc.MachineName}' ({deviceKey}) checkpoint set to now - " +
                           "next sync will only fetch punches after this moment.");
            }
            catch (Exception ex)
            {
                Logger.Log($"[Checkpoint] FastForward: failed for MachineId={m.MachineId} - {ex.Message}");
            }
        }

        File.WriteAllText(flagPath, $"Applied at {DateTime.Now:O}");
    }

    private async Task RunResetCheckpointAsync()
    {
        var machines = new List<MachineConfig>();
        foreach (var m in _company.Machines)
        {
            var mc = await _apiService.GetMachineAsync(m.MachineId);
            if (mc != null) machines.Add(mc);
        }

        if (machines.Count == 0)
        {
            MessageBox.Show(this, "No activated machines found.", "Reset Checkpoint",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new MachinePickerDialog(machines);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedMachine == null)
            return;

        var machine = picker.SelectedMachine;
        var deviceKey = $"{machine.MachineType}:{machine.IpAddress}:{machine.DeviceId}";

        var confirm = MessageBox.Show(this,
            $"'{machine.MachineName}' ka sync checkpoint reset karoge?\n\n" +
            "Agla sync is device ke saare purane logs dobara fetch karega — " +
            "sirf tab karo jab pichla synced data database se already delete ho chuka ho, " +
            "warna duplicate records ban sakte hain.",
            "Reset Checkpoint", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes) return;

        _checkpointService.ResetCheckpoint(deviceKey);
        MessageBox.Show(this, $"Checkpoint reset ho gaya '{machine.MachineName}' ke liye.",
            "Reset Checkpoint", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }


    private void PositionConnectionBadge()
    {
        _connectionBadge.Location = new Point(
            _header.Width - _connectionBadge.Width - 28,
            (_header.Height - _connectionBadge.Height) / 2);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon.Visible = false;
        _syncService.DisconnectAll();
        base.OnFormClosing(e);
    }
}