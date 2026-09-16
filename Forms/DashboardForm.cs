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

    private readonly StatCard _statusStat = new();
    private readonly StatCard _machinesStat = new();
    private readonly StatCard _lastSyncStat = new();

    private readonly Panel _machineListHost = new() { Dock = DockStyle.Top, AutoSize = true };

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

        _header.GradientStart = Theme.PrimaryDarker;
        _header.GradientEnd = Theme.Primary;
        _header.Subtitle = "Company : " + string.Join(", ", _company.Machines.Select(m => m.CompanyName).Distinct());

        _header.Controls.Add(_connectionBadge);
        _connectionBadge.BringToFront();
        _header.Resize += (_, _) => PositionConnectionBadge();
        PositionConnectionBadge();
        var body = BuildBody();

        Controls.Add(body);
        Controls.Add(_header);

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
        RefreshMachineList();

        _timer.Tick += async (_, _) => await RunSyncAsync();
        Load += async (_, _) => { _timer.Start(); await RunSyncAsync(); };
    }

    private Panel BuildBody()
    {
        var statsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1
        };
        statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
        statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
        statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4F));
        statsRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));

        _statusStat.Caption = "Status";
        _statusStat.IconGlyph = "\uE73E";
        _statusStat.IconBackColor = Theme.Success;
        _statusStat.Value = "Initializing...";
        _statusStat.Dock = DockStyle.Fill;
        _statusStat.Margin = new Padding(0, 0, 8, 0);

        _machinesStat.Caption = "Machines";
        _machinesStat.IconGlyph = "\uE977";
        _machinesStat.IconBackColor = Theme.Primary;
        _machinesStat.Value = _company.Machines.Count.ToString();
        _machinesStat.Dock = DockStyle.Fill;
        _machinesStat.Margin = new Padding(8, 0, 8, 0);

        _lastSyncStat.Caption = "Last Sync";
        _lastSyncStat.IconGlyph = "\uE121";
        _lastSyncStat.IconBackColor = Theme.Accent;
        _lastSyncStat.Value = "\u2014";
        _lastSyncStat.Dock = DockStyle.Fill;
        _lastSyncStat.Margin = new Padding(8, 0, 0, 0);

        statsRow.Controls.Add(_statusStat, 0, 0);
        statsRow.Controls.Add(_machinesStat, 1, 0);
        statsRow.Controls.Add(_lastSyncStat, 2, 0);

        var machinesCard = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(22), Margin = new Padding(0, 16, 0, 0) };
        var machinesLabel = new Label
        {
            Text = "CONNECTED MACHINES",
            Dock = DockStyle.Top,
            Height = 24,
            Font = Theme.FontSmallBold,
            ForeColor = Theme.TextSecondary
        };
        _machineListHost.Dock = DockStyle.Top;
        machinesCard.Controls.Add(_machineListHost);
        machinesCard.Controls.Add(machinesLabel);

        var actionsLabel = new Label
        {
            Text = "QUICK ACTIONS",
            Dock = DockStyle.Top,
            Height = 24,
            Font = Theme.FontSmallBold,
            ForeColor = Theme.TextSecondary,
            Margin = new Padding(0, 16, 0, 0)
        };

        var tileGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2
        };
        tileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
        tileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
        tileGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4F));
        tileGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        tileGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

        var tileSync = MakeTile("\uE895", "Sync Now", "Pull latest punches", Theme.Primary);
        tileSync.Click += async (_, _) => await RunSyncAsync();

        var tileEmployees = MakeTile("\uE716", "Sync Employees", "Push employees to device", Theme.Accent);
        tileEmployees.Click += async (_, _) => await RunEmployeeSyncAsync();

        var tileMap = MakeTile("\uE8C8", "Map Users", "Link machine to ERP users", Theme.Success);
        tileMap.Click += async (_, _) => await RunMapUsersAsync();

        var tileSimulate = MakeTile("\uE945", "Simulate Punch", "Test without hardware", Theme.Warning);
        tileSimulate.Click += async (_, _) => await RunSimulatePunchAsync();

        var tileLogs = MakeTile("\uE7C3", "Open Logs", "View connector log file", Theme.TextSecondary);
        tileLogs.Click += (_, _) => OpenLogsFolder();

        var tileExit = MakeTile("\uE711", "Exit", "Close the connector", Theme.Danger);
        tileExit.Click += (_, _) => { _trayIcon.Visible = false; Application.Exit(); };

        var tiles = new[] { tileSync, tileEmployees, tileMap, tileSimulate, tileLogs, tileExit };
        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i].Dock = DockStyle.Fill;
            tiles[i].Margin = new Padding(0, 0, 10, 10);
            tileGrid.Controls.Add(tiles[i], i % 3, i / 3);
        }

        var stack = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 4, AutoSize = true, BackColor = Theme.Background };
        stack.Controls.Add(statsRow, 0, 0);
        stack.Controls.Add(machinesCard, 0, 1);
        stack.Controls.Add(actionsLabel, 0, 2);
        stack.Controls.Add(tileGrid, 0, 3);

        var centered = new CenteredColumn(900) { Padding = new Padding(0, 24, 0, 24) };
        centered.Content = stack;

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        scrollHost.Controls.Add(centered);
        return scrollHost;
    }

    private static ActionTile MakeTile(string glyph, string title, string subtitle, Color accent)
    {
        var tile = new ActionTile
        {
            Glyph = glyph,
            Title = title,
            Subtitle = subtitle,
            AccentColor = accent
        };
        return tile;
    }

    private void RefreshMachineList()
    {
        _machineListHost.Controls.Clear();

        if (_company.Machines.Count == 0)
        {
            var empty = new Label
            {
                Text = "No machines activated yet.",
                Dock = DockStyle.Top,
                Height = 32,
                Font = Theme.FontBody,
                ForeColor = Theme.TextSecondary
            };
            _machineListHost.Controls.Add(empty);
            return;
        }

        var rows = _company.Machines
            .OrderBy(m => m.MachineName)
            .Select(m => new MachineRow(m.MachineName, m.CompanyName, Theme.Success))
            .ToList();

        for (int i = rows.Count - 1; i >= 0; i--)
            _machineListHost.Controls.Add(rows[i]);

        _machinesStat.Value = _company.Machines.Count.ToString();
    }

    private void SetStatus(string message)
    {
        _statusStat.Value = message;
        var lower = message.ToLowerInvariant();

        if (lower.Contains("fail") || lower.Contains("error") || lower.Contains("could not"))
        {
            _statusStat.IconBackColor = Theme.Danger;
            _statusStat.IconGlyph = "\uE783";
            _connectionBadge.Text = "Error";
            _connectionBadge.PillBackColor = Theme.DangerLight;
            _connectionBadge.PillForeColor = Theme.Danger;
        }
        else if (lower.Contains("syncing") || lower.Contains("initializ"))
        {
            _statusStat.IconBackColor = Theme.Warning;
            _statusStat.IconGlyph = "\uE895";
            _connectionBadge.Text = "Syncing";
            _connectionBadge.PillBackColor = Theme.WarningLight;
            _connectionBadge.PillForeColor = Theme.Warning;
        }
        else
        {
            _statusStat.IconBackColor = Theme.Success;
            _statusStat.IconGlyph = "\uE73E";
            _connectionBadge.Text = "Connected";
            _connectionBadge.PillBackColor = Theme.SuccessLight;
            _connectionBadge.PillForeColor = Theme.Success;
        }

        _statusStat.Invalidate(true);
        _connectionBadge.Invalidate();
    }

    private async Task RunSyncAsync()
    {
        SetStatus("Syncing...");
        var machineIds = _company.Machines.Select(m => m.MachineId).ToList();
        Logger.Log($"[Dashboard] RunSyncAsync: company has {_company.Machines.Count} activated machine(s), MachineIds=[{string.Join(", ", machineIds)}]");
        await _syncService.RunCycleAsync(machineIds);
        _lastSyncStat.Value = $"{_syncService.LastSyncTime:hh:mm tt}";
        SetStatus("Connected");
    }

    private async Task RunEmployeeSyncAsync()
    {
        _timer.Stop();
        await _syncService.DeviceLock.WaitAsync();
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

                if (string.Equals(machine.MachineType?.Trim(), "ETIMEOFFICE", StringComparison.OrdinalIgnoreCase))
                {
                    // eTimeOffice's API exposes no employee create/delete endpoint (confirmed
                    // with vendor support), so "Sync Employees" (which enrolls ERP employees
                    // onto the device) can never succeed here — every row would just log a
                    // silent CreateEmployee=false. Point the user at "Map Existing Users"
                    // instead, which uses AttendanceEmployeeMapping and actually works.
                    MessageBox.Show(this,
                        $"'{machine.MachineName}' is an eTimeOffice cloud machine. eTimeOffice does not support " +
                        "creating or deleting employees via its API — enrollment must be done on eTimeOffice's own portal. " +
                        "Use \"Map Existing Users\" to map its enroll numbers to ERP employees instead.",
                        "Employee Sync not supported", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    continue;
                }

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
            _syncService.DeviceLock.Release();
            _timer.Start();
        }
    }

    private async Task RunMapUsersAsync()
    {
        _timer.Stop();
        await _syncService.DeviceLock.WaitAsync();
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
            _syncService.DeviceLock.Release();
            _timer.Start();
        }
    }

    private async Task RunSimulatePunchAsync()
    {
        var machines = new List<MachineConfig>();
        foreach (var m in _company.Machines)
        {
            var mc = await _apiService.GetMachineAsync(m.MachineId);
            if (mc != null && string.Equals(mc.MachineType?.Trim(), "MOCK", StringComparison.OrdinalIgnoreCase))
                machines.Add(mc);
        }

        if (machines.Count == 0)
        {
            MessageBox.Show(this,
                "No machine with MachineType 'MOCK' found. Add one to test without hardware.",
                "Simulate Punch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        MachineConfig machine;
        if (machines.Count == 1)
        {
            machine = machines[0];
        }
        else
        {
            using var picker = new MachinePickerDialog(machines);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedMachine == null) return;
            machine = picker.SelectedMachine;
        }

        using var prompt = new PromptDialog("Simulate Punch", "Enroll Number", "101");
        if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(prompt.Value)) return;

        using var mock = new MockProvider(machine.Id, _checkpointService);
        mock.SimulatePunch(prompt.Value);

        MessageBox.Show(this,
            $"Punch simulated for enroll number '{prompt.Value}' on '{machine.MachineName}'.\nRun Sync Now to pull it in.",
            "Simulate Punch", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void OpenLogsFolder()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SapeagleAttendanceConnector");
        Directory.CreateDirectory(dir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Log($"[Dashboard] OpenLogsFolder error: {ex.Message}");
        }
    }

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

                var deviceKey = $"{mc.MachineType}:{mc.Id}";
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
        var deviceKey = $"{machine.MachineType}:{machine.Id}";

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