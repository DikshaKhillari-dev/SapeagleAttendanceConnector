using SapeagleAttendanceConnector.Models;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.Forms;

public class MapExistingUsersForm : Form
{
    private readonly ApiService _apiService;
    private readonly int _comId;
    private readonly string _machineType;
    private readonly List<MachineEmployee> _machineEmployees;
    private readonly List<ErpEmployee> _erpEmployees;
    private readonly HeaderBanner _header = new() { Title = "Map Existing Users", Height = 84 };
    private readonly CardPanel _gridCard = new() { Dock = DockStyle.Fill, Padding = new Padding(16) };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        BackgroundColor = Theme.Surface,
        BorderStyle = BorderStyle.None,
        Font = Theme.FontListItem,
        ColumnHeadersHeight = 34,
        RowTemplate = { Height = 36 }
    };

    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 92, BackColor = Theme.Surface };
    private readonly Button _btnSave = new() { Text = "Save Mapping" };
    private readonly Button _btnClose = new() { Text = "Close" };

    public MapExistingUsersForm(string deviceLabel, List<MachineEmployee> machineEmployees,
     List<ErpEmployee> erpEmployees, ApiService apiService, int comId, string machineType)
    {
        _machineEmployees = machineEmployees;
        _erpEmployees = erpEmployees;
        _apiService = apiService;
        _comId = comId;
        _machineType = machineType;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "Map Existing Users";
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        BackColor = Theme.Background;

        _header.Subtitle = deviceLabel;

        BuildGrid();
        BuildFooter();

        var centered = new CenteredColumn(760) { Padding = new Padding(0, 20, 0, 20) };
        centered.Content = _gridCard;
        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        scrollHost.Controls.Add(centered);

        Controls.Add(scrollHost);
        Controls.Add(_header);
        Controls.Add(_footer);

        Load += async (_, _) => await LoadExistingMappingAsync();
    }

    private void BuildGrid()
    {
        var colEnroll = new DataGridViewTextBoxColumn
        {
            Name = "Enroll",
            HeaderText = "MACHINE ENROLL #",
            ReadOnly = true,
            FillWeight = 25
        };
        var colName = new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "MACHINE USER",
            ReadOnly = true,
            FillWeight = 30
        };

        var displayList = _erpEmployees
            .Select(e => new ErpEmployee
            {
                EmployeeId = e.EmployeeId,
                EmployeeCode = e.EmployeeCode,
                EmployeeName = string.IsNullOrWhiteSpace(e.EmployeeName) ? e.EmployeeCode : e.EmployeeName
            })
            .OrderBy(e => e.EmployeeName)
            .ToList();

        var colErp = new DataGridViewComboBoxColumn
        {
            Name = "ErpEmployee",
            HeaderText = "MAP TO ERP EMPLOYEE",
            DataSource = displayList,
            DisplayMember = nameof(ErpEmployee.EmployeeName),
            ValueMember = nameof(ErpEmployee.EmployeeId),
            FillWeight = 45,
            FlatStyle = FlatStyle.Flat
        };

        _grid.Columns.AddRange(colEnroll, colName, colErp);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        foreach (var m in _machineEmployees.OrderBy(m => m.Name))
        {
            int idx = _grid.Rows.Add(m.EnrollNumber, m.Name, null);
            _grid.Rows[idx].Tag = m.EnrollNumber;
        }

        _gridCard.Controls.Add(_grid);
    }

    private async Task LoadExistingMappingAsync()
    {
        try
        {
            var existing = await _apiService.GetEmployeeMappingAsync(_comId);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var enroll = row.Tag as string ?? "";
                if (existing.TryGetValue(enroll, out long empId))
                    row.Cells["ErpEmployee"].Value = empId;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[MapExistingUsersForm] LoadExistingMappingAsync: exception - {ex.Message}");
        }
    }

    private void BuildFooter()
    {
        Theme.StylePrimaryButton(_btnSave);
        Theme.StyleSecondaryButton(_btnClose);
        _btnSave.AutoSize = true; _btnSave.MinimumSize = new Size(160, 44); _btnSave.Padding = new Padding(20, 0, 20, 0);
        _btnClose.AutoSize = true; _btnClose.MinimumSize = new Size(110, 44); _btnClose.Padding = new Padding(20, 0, 20, 0);

        _btnSave.Click += async (_, _) => await SaveAsync();
        _btnClose.Click += (_, _) => Close();

        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _btnClose.Margin = new Padding(0, 12, 12, 12);
        _btnSave.Margin = new Padding(0, 12, 0, 12);
        row.Controls.Add(new Panel { Dock = DockStyle.Fill }, 0, 0);
        row.Controls.Add(_btnClose, 1, 0);
        row.Controls.Add(_btnSave, 2, 0);

        var centeredFooter = new CenteredColumn(760) { Dock = DockStyle.Fill };
        centeredFooter.Content = row;
        _footer.Controls.Add(centeredFooter);
        _footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
    }

    private async Task SaveAsync()
    {
        var mappings = new List<EmployeeMappingEntry>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var enroll = row.Tag as string ?? "";
            if (row.Cells["ErpEmployee"].Value is long empId && empId > 0)
            {
                mappings.Add(new EmployeeMappingEntry
                {
                    ComId = _comId,
                    MachineType = _machineType,
                    EnrollNumber = enroll,
                    EmpId = empId,
                    CreatedBy = 0 
                });
            }
        }

        if (mappings.Count == 0)
        {
            MessageBox.Show(this, "Map at least one machine user before saving.", "Map Existing Users",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _btnSave.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            bool ok = await _apiService.SaveEmployeeMappingAsync(mappings);
            MessageBox.Show(this, ok ? $"Saved {mappings.Count} mapping(s)." : "Failed to save mapping.",
                "Map Existing Users", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            if (ok) Close();
        }
        finally
        {
            Cursor = Cursors.Default;
            _btnSave.Enabled = true;
        }
    }
}