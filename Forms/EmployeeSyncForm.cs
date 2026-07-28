using System.Drawing.Drawing2D;
using SapeagleAttendanceConnector.Models;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.Forms;

public class EmployeeSyncForm : Form
{
    private readonly ApiService _apiService;
    private readonly int _comId;
    private readonly IAttendanceProvider? _provider;
    private readonly List<MachineEmployee> _machineEmployees;
    private readonly ToolTip _machineTooltip = new() { AutoPopDelay = 15000, InitialDelay = 300 };
    private List<ErpEmployee> _erpEmployees;
    private bool _suppressDepartmentReload;
    private bool _suppressSelectAllSync;
    private bool _suppressItemCheckSync;

    // ---- Header ----
    private readonly HeaderBanner _header = new() { Title = "Employee Sync", Height = 84 };

    // ---- Stat cards ----
    private readonly StatCard _statMachine = new() { Caption = "Machine Employees", AccentColor = Theme.Accent };
    private readonly StatCard _statErp = new() { Caption = "ERP Employees", AccentColor = Theme.Primary };

    // ---- Filter card ----
    private readonly CardPanel _filterCard = new();
    private readonly Label _lblDepartment = new()
    {
        Text = "DEPARTMENT",
        Dock = DockStyle.Top,
        Height = 22,
        Font = Theme.FontColumnHeader,
        ForeColor = Theme.TextSecondary
    };
    private readonly ComboBox _cmbDepartment = new()
    {
        Dock = DockStyle.Top,
        Height = 36,
        DropDownStyle = ComboBoxStyle.DropDownList
    };
    // ---- Employee list card ----
    private readonly CardPanel _listCard = new();
    private readonly TableLayoutPanel _listHeaderRow = new()
    {
        Dock = DockStyle.Top,
        Height = 42,
        ColumnCount = 3,
        RowCount = 1,
        BackColor = Color.Transparent
    };
    private readonly CheckBox _chkSelectAll = new()
    {
        Text = "Select All",
        AutoSize = true,
        Anchor = AnchorStyles.Left
    };
    private readonly TextBox _txtSearch = new()
    {
        PlaceholderText = "Search by name or code",
        Dock = DockStyle.Fill,
        Margin = new Padding(14, 5, 14, 5)
    };
    private readonly Label _lblSelectedCount = new()
    {
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        TextAlign = ContentAlignment.MiddleRight,
        Font = Theme.FontSmallBold,
        ForeColor = Theme.TextSecondary
    };
    // Full department-filtered list from the API (unaffected by the search box).
    // _displayedEmployees is the subset currently shown in _clbEmployees after the
    // search filter is applied; _selectedCodes is the persistent selection set so
    // ticking a box survives typing/clearing a search or switching department.
    private List<ErpEmployee> _displayedEmployees = new();
    private readonly HashSet<string> _selectedCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CheckedListBox _clbEmployees = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        IntegralHeight = false,
        BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 40,
        Font = Theme.FontListItem
    };
    private readonly Panel _columnHeaderRow = new() { Dock = DockStyle.Top, Height = 22 };
    private readonly Label _lblColCode = new()
    {
        Text = "CODE",
        AutoSize = false,
        Font = Theme.FontColumnHeader,
        ForeColor = Theme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _lblColName = new()
    {
        Text = "EMPLOYEE NAME",
        AutoSize = false,
        Font = Theme.FontColumnHeader,
        ForeColor = Theme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };

    // ---- Footer ----
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 92, BackColor = Theme.Surface };
    private readonly Button _btnSync = new() { Text = "Sync Employees" };
    private readonly Button _btnCancel = new() { Text = "Cancel" };

    public List<ErpEmployee> SelectedEmployees =>
        _erpEmployees.Where(e => _selectedCodes.Contains(e.EmployeeCode)).ToList();

    public EmployeeSyncForm(string deviceLabel, List<MachineEmployee> machineEmployees, List<ErpEmployee> erpEmployees,
        ApiService apiService, int comId, IAttendanceProvider? provider = null)
    {
        _erpEmployees = erpEmployees;
        _apiService = apiService;
        _comId = comId;
        _provider = provider;
        _machineEmployees = machineEmployees;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Employee Sync";
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = Theme.Background;
        WindowState = FormWindowState.Maximized;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        _header.Title = "Employee Sync";
        _header.Subtitle = deviceLabel;

        BuildStatCards(machineEmployees.Count, erpEmployees.Count);

        RefreshMachineTooltip();
        if (_machineEmployees.Count > 0)
        {
            _statMachine.OnViewDetails = () => ShowMachineEmployeesDialog(deviceLabel);
        }
        BuildFilterCard();
        BuildListCard();
        BuildFooter();

        // ---- Assemble the centered, scroll-safe body ----
        var contentStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Background
        };
        contentStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
        contentStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        contentStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var statsRow = BuildStatsRow();
        statsRow.Dock = DockStyle.Fill;
        statsRow.Margin = new Padding(0, 0, 0, 16);

        _filterCard.Dock = DockStyle.Fill;
        _filterCard.Margin = new Padding(0, 0, 0, 16);

        _listCard.Dock = DockStyle.Fill;
        _listCard.Margin = new Padding(0);

        contentStack.Controls.Add(statsRow, 0, 0);
        contentStack.Controls.Add(_filterCard, 0, 1);
        contentStack.Controls.Add(_listCard, 0, 2);

        var centered = new CenteredColumn(760) { Padding = new Padding(0, 20, 0, 20) };
        centered.Content = contentStack;

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        scrollHost.Controls.Add(centered);

        Controls.Add(scrollHost);
        Controls.Add(_header);
        Controls.Add(_footer);

        AcceptButton = _btnSync;
        CancelButton = _btnCancel;


        ApplyFilterAndPopulate();

        // Re-assert maximized state once the window handle exists. On some laptop/DPI
        // configurations a modal dialog's WindowState set in the constructor doesn't
        // stick, leaving the dialog rendered smaller than the screen with content
        // appearing cut off. We also pin MaximizedBounds to the *working area* (screen
        // minus the taskbar) — without this a maximized dialog can size itself to the
        // full physical screen and end up partly hidden behind the taskbar, which is
        // what was clipping the footer's Sync/Cancel buttons.
        Shown += (_, _) =>
        {
            var workingArea = Screen.FromControl(this).WorkingArea;
            MaximizedBounds = workingArea;
            if (WindowState != FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            Bounds = workingArea;
        };

        Load += async (_, _) => await LoadDepartmentsAsync();
    }

    private void ShowMachineEmployeesDialog(string deviceLabel)
    {
        // _machineEmployees is passed by reference and mutated in-place by the dialog
        // when employees are deleted, so it (and preview.MachineEmployees back in
        // DashboardForm, which is the very same list instance) stay in sync automatically.
        using var dlg = new MachineEmployeesListForm(deviceLabel, _machineEmployees, _provider);
        dlg.ShowDialog(this);

        _statMachine.Value = _machineEmployees.Count.ToString("N0");
        RefreshMachineTooltip();
        if (_machineEmployees.Count == 0)
            _statMachine.OnViewDetails = null;
    }

    private void RefreshMachineTooltip()
    {
        var names = string.Join("\n", _machineEmployees.Select(m => m.Name));
        _machineTooltip.SetToolTip(_statMachine, names);
    }

    private void BuildStatCards(int machineEmployeeCount, int erpCount)
    {
        _statMachine.Value = machineEmployeeCount.ToString("N0");
        _statErp.Value = erpCount.ToString("N0");
    }

    private TableLayoutPanel BuildStatsRow()
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _statMachine.Dock = DockStyle.Fill;
        _statMachine.Margin = new Padding(0, 0, 8, 0);
        _statErp.Dock = DockStyle.Fill;
        _statErp.Margin = new Padding(8, 0, 0, 0);

        row.Controls.Add(_statMachine, 0, 0);
        row.Controls.Add(_statErp, 1, 0);
        return row;
    }

    private void BuildFilterCard()
    {
        Theme.StyleComboBox(_cmbDepartment);
        _cmbDepartment.Font = Theme.FontComboLarge;
        _cmbDepartment.ItemHeight = TextRenderer.MeasureText("Ag", Theme.FontComboLarge).Height + 4;

        _cmbDepartment.SelectedIndexChanged += async (_, _) =>
        {
            if (_suppressDepartmentReload) return;
            await ReloadEmployeesForDepartmentAsync();
        };


        _filterCard.Controls.Add(_cmbDepartment);
        Spacer(_filterCard, 6);
        _filterCard.Controls.Add(_lblDepartment);
    }

    private static void Spacer(Control parent, int height)
    {
        parent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = height, BackColor = Color.Transparent });
    }

    private void BuildListCard()
    {
        _listCard.Padding = new Padding(18, 16, 18, 8);

        Theme.StyleCheckBox(_chkSelectAll);
        _chkSelectAll.Font = Theme.FontBodyBold;

        Theme.StyleTextBox(_txtSearch);
        _txtSearch.Font = Theme.FontListItem;
        _txtSearch.TextChanged += (_, _) => ApplyFilterAndPopulate();

        _listHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _listHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _listHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _listHeaderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _listHeaderRow.Controls.Add(_chkSelectAll, 0, 0);
        _listHeaderRow.Controls.Add(_txtSearch, 1, 0);
        _listHeaderRow.Controls.Add(_lblSelectedCount, 2, 0);

        var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

        _clbEmployees.DrawItem += ClbEmployees_DrawItem;
        _clbEmployees.ItemCheck += ClbEmployees_ItemCheck;

        _chkSelectAll.CheckedChanged += (_, _) =>
        {
            if (_suppressSelectAllSync) return;
            SetAllChecked(_chkSelectAll.Checked);
        };

        // Column headings, aligned with where ClbEmployees_DrawItem starts drawing the
        // checkbox (Left+10) and the "code  ·  name" text (checkbox right edge + 12).
        // The measurement font must match the list's actual item font (FontListItem),
        // otherwise the header drifts out of alignment with the text it's meant to sit above.
        int codeColX = 10 + 18 + 12; // boxRect.Left offset + boxSize + text gap
        int nameColX = codeColX + TextRenderer.MeasureText("EMP0000  ·  ", Theme.FontListItem).Width;
        int headerRowHeight = TextRenderer.MeasureText("Ag", Theme.FontColumnHeader).Height + 4;
        _columnHeaderRow.Height = headerRowHeight;
        _lblColCode.Location = new Point(codeColX, 2);
        _lblColCode.Size = new Size(Math.Max(60, nameColX - codeColX), headerRowHeight);
        _lblColName.Location = new Point(nameColX, 2);
        _lblColName.Size = new Size(220, headerRowHeight);
        _columnHeaderRow.Controls.Add(_lblColName);
        _columnHeaderRow.Controls.Add(_lblColCode);

        _listCard.Controls.Add(_clbEmployees);
        Spacer(_listCard, 4);
        _listCard.Controls.Add(_columnHeaderRow);
        Spacer(_listCard, 8);
        _listCard.Controls.Add(divider);
        Spacer(_listCard, 6);
        _listCard.Controls.Add(_listHeaderRow);
    }

    private void BuildFooter()
    {
        Theme.StylePrimaryButton(_btnSync);
        Theme.StyleSecondaryButton(_btnCancel);

        // AutoSize (with a minimum floor) instead of a fixed pixel Width so the button
        // always grows to fit its text — a fixed Width clipped "Sync Employees" down to
        // "Sync Employe" on higher-DPI displays where the scaled font needed more room.
        _btnSync.AutoSize = true;
        _btnSync.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _btnSync.Padding = new Padding(24, 0, 24, 0);
        _btnSync.MinimumSize = new Size(200, 44);

        _btnCancel.AutoSize = true;
        _btnCancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _btnCancel.Padding = new Padding(20, 0, 20, 0);
        _btnCancel.MinimumSize = new Size(130, 44);

        _btnSync.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        _btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0)
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Anchor Top+Bottom (not just Right) so the buttons center themselves in the
        // footer's available height instead of hugging a fixed top margin - which is
        // what made them look pinned to the very bottom edge of the dialog.
        _btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        _btnCancel.Margin = new Padding(0, 12, 12, 12);
        _btnSync.Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        _btnSync.Margin = new Padding(0, 12, 0, 12);

        buttonRow.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent }, 0, 0);
        buttonRow.Controls.Add(_btnCancel, 1, 0);
        buttonRow.Controls.Add(_btnSync, 2, 0);

        var topBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

        var centeredFooter = new CenteredColumn(760) { Padding = new Padding(0, 0, 0, 0) };
        centeredFooter.Content = buttonRow;
        centeredFooter.Dock = DockStyle.Fill;

        _footer.Controls.Add(centeredFooter);
        _footer.Controls.Add(topBorder);
    }

    private void ClbEmployees_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool isChecked = _clbEmployees.GetItemChecked(e.Index);
        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool alt = e.Index % 2 == 1;

        var bgColor = isSelected ? Theme.PrimaryLight : (alt ? Theme.SurfaceAlt : Theme.Surface);
        using (var bgBrush = new SolidBrush(bgColor))
            g.FillRectangle(bgBrush, e.Bounds);

        const int boxSize = 18;
        var boxRect = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - boxSize) / 2, boxSize, boxSize);
        using var boxPath = Theme.RoundedRect(boxRect, 4);

        if (isChecked)
        {
            using var fillBrush = new SolidBrush(Theme.Primary);
            g.FillPath(fillBrush, boxPath);
            using var checkPen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            var p1 = new Point(boxRect.Left + 4, boxRect.Top + 9);
            var p2 = new Point(boxRect.Left + 7, boxRect.Top + 13);
            var p3 = new Point(boxRect.Left + 14, boxRect.Top + 5);
            g.DrawLines(checkPen, new[] { p1, p2, p3 });
        }
        else
        {
            using var borderPen = new Pen(Theme.BorderStrong, 1.5f);
            g.DrawPath(borderPen, boxPath);
        }

        var text = _clbEmployees.Items[e.Index]?.ToString() ?? string.Empty;
        var textRect = new Rectangle(boxRect.Right + 12, e.Bounds.Top, e.Bounds.Width - boxRect.Right - 20, e.Bounds.Height);
        TextRenderer.DrawText(g, text, Theme.FontListItem, textRect, Theme.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var linePen = new Pen(Theme.Border);
        g.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private async Task LoadDepartmentsAsync()
    {
        _suppressDepartmentReload = true;
        _cmbDepartment.Enabled = false;
        try
        {
            var departments = await _apiService.GetDepartmentsAsync(_comId);

            _cmbDepartment.Items.Clear();
            _cmbDepartment.Items.Add(new DepartmentItem(0, "All Departments"));
            foreach (var d in departments)
                _cmbDepartment.Items.Add(new DepartmentItem(d.Id, d.Name));

            _cmbDepartment.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Logger.Log($"[EmployeeSyncForm] LoadDepartmentsAsync: exception - {ex.Message}");
        }
        finally
        {
            _cmbDepartment.Enabled = true;
            _suppressDepartmentReload = false;
        }
    }

    private async Task ReloadEmployeesForDepartmentAsync()
    {
        if (_cmbDepartment.SelectedItem is not DepartmentItem selected) return;

        _cmbDepartment.Enabled = false;
        _clbEmployees.Enabled = false;
        _statErp.Value = "…";

        try
        {
            int? departmentId = selected.Id == 0 ? null : selected.Id;
            _erpEmployees = await _apiService.GetEmployeesForSyncAsync(_comId, departmentId);

            _statErp.Value = _erpEmployees.Count.ToString("N0");
            ApplyFilterAndPopulate();
        }
        catch (Exception ex)
        {
            Logger.Log($"[EmployeeSyncForm] ReloadEmployeesForDepartmentAsync: exception - {ex.Message}");
            MessageBox.Show(this, "Could not load employees for the selected department.", "Employee Sync",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _cmbDepartment.Enabled = true;
            _clbEmployees.Enabled = true;
        }
    }

    /// <summary>Re-derives the visible list from _erpEmployees + the current search box
    /// text (matches employee code or name, case-insensitive) and repopulates the list.</summary>
    private void ApplyFilterAndPopulate()
    {
        string q = _txtSearch.Text.Trim();
        List<ErpEmployee> filtered = string.IsNullOrEmpty(q)
            ? _erpEmployees
            : _erpEmployees.Where(e =>
                (!string.IsNullOrEmpty(e.EmployeeCode) && e.EmployeeCode.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(e.EmployeeName) && e.EmployeeName.Contains(q, StringComparison.OrdinalIgnoreCase)))
              .ToList();
        PopulateEmployeeList(filtered);
    }

    private void PopulateEmployeeList(List<ErpEmployee> employees)
    {
        _displayedEmployees = employees;

        _suppressItemCheckSync = true;
        _clbEmployees.Items.Clear();
        foreach (var emp in employees)
            _clbEmployees.Items.Add($"{emp.EmployeeCode}  ·  {emp.EmployeeName}", _selectedCodes.Contains(emp.EmployeeCode));
        _suppressItemCheckSync = false;

        _suppressSelectAllSync = true;
        _chkSelectAll.Checked = employees.Count > 0 && employees.All(e => _selectedCodes.Contains(e.EmployeeCode));
        _suppressSelectAllSync = false;

        UpdateSelectedCountLabel();
    }

    private void ClbEmployees_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppressItemCheckSync) return;

        if (e.Index >= 0 && e.Index < _displayedEmployees.Count)
        {
            var code = _displayedEmployees[e.Index].EmployeeCode;
            if (e.NewValue == CheckState.Checked) _selectedCodes.Add(code);
            else _selectedCodes.Remove(code);
        }

        BeginInvoke(SyncSelectionState);
    }

    private void SetAllChecked(bool check)
    {
        _suppressItemCheckSync = true;
        for (int i = 0; i < _clbEmployees.Items.Count; i++)
        {
            _clbEmployees.SetItemChecked(i, check);
            if (i < _displayedEmployees.Count)
            {
                var code = _displayedEmployees[i].EmployeeCode;
                if (check) _selectedCodes.Add(code);
                else _selectedCodes.Remove(code);
            }
        }
        _suppressItemCheckSync = false;

        UpdateSelectedCountLabel();
        _clbEmployees.Invalidate();
    }

    private void SyncSelectionState()
    {
        if (_suppressItemCheckSync) return;

        bool allChecked = _clbEmployees.Items.Count > 0 &&
                           _clbEmployees.CheckedItems.Count == _clbEmployees.Items.Count;

        _suppressSelectAllSync = true;
        _chkSelectAll.Checked = allChecked;
        _suppressSelectAllSync = false;

        UpdateSelectedCountLabel();
    }

    private void UpdateSelectedCountLabel()
    {
        // Counted against the full department-filtered list (not just what the search
        // box currently shows) so the total stays meaningful while the person is typing.
        int selectedInScope = _erpEmployees.Count(e => _selectedCodes.Contains(e.EmployeeCode));
        _lblSelectedCount.Text = $"{selectedInScope} of {_erpEmployees.Count} selected";
    }

    private sealed class DepartmentItem
    {
        public int Id { get; }
        public string Name { get; }

        public DepartmentItem(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}

/// <summary>Dialog listing the employees already enrolled on a machine (enroll number +
/// name), opened from the "Machine Employees" stat card. Each employee has a checkbox;
/// the selected ones can be removed from the machine via the Delete button (after a
/// confirmation prompt).</summary>
internal class MachineEmployeesListForm : Form
{
    // The same List<MachineEmployee> instance the caller (EmployeeSyncForm, and in turn
    // DashboardForm's EmployeeSyncPreview) holds — mutating it in place here means the
    // deletion is automatically reflected everywhere else that reads it, no extra
    // callback/event plumbing required.
    private readonly List<MachineEmployee> _machineEmployees;
    private readonly IAttendanceProvider? _provider;

    // Employees currently shown, in list order, kept in sync with the ListBox rows.
    private List<MachineEmployee> _shown;

    private readonly Label _countLabel = new()
    {
        Dock = DockStyle.Top,
        Height = 24,
        Font = Theme.FontSmallBold,
        ForeColor = Theme.TextSecondary
    };

    private readonly CheckedListBox _list = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        Font = Theme.FontListItem,
        IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 40,
        CheckOnClick = true
    };
    private readonly Panel _columnHeaderRow = new() { Dock = DockStyle.Top, Height = 24 };
    private readonly Label _lblColCode = new()
    {
        Text = "CODE",
        AutoSize = false,
        Font = Theme.FontColumnHeader,
        ForeColor = Theme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _lblColName = new()
    {
        Text = "EMPLOYEE NAME",
        AutoSize = false,
        Font = Theme.FontColumnHeader,
        ForeColor = Theme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private readonly Button _btnDelete = new() { Text = "Delete" };
    private readonly Button _btnClose = new() { Text = "Close" };

    public MachineEmployeesListForm(string deviceLabel, List<MachineEmployee> machineEmployees, IAttendanceProvider? provider)
    {
        _machineEmployees = machineEmployees;
        _provider = provider;
        _shown = _machineEmployees.OrderBy(m => m.Name).ToList();

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Machine Employees";
        MinimumSize = new Size(420, 420);
        Size = new Size(460, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = Theme.Background;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        var header = new HeaderBanner { Title = "Machine Employees", Subtitle = deviceLabel, Height = 84 };

        var listCard = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(16), Padding = new Padding(14, 12, 14, 12) };

        _list.DrawItem += List_DrawItem;
        _list.ItemCheck += (_, _) => BeginInvoke(UpdateDeleteButtonState);
        RefreshList();

        // Column headings, aligned exactly with where List_DrawItem starts drawing the
        // checkbox (Left+10) and the "code  ·  name" text (checkbox right edge + 12) -
        // now that the list is owner-drawn, this lines up precisely instead of
        // approximating the native CheckedListBox glyph position.
        int codeColX = 10 + 18 + 12; // boxRect.Left offset + boxSize + text gap
        int nameColX = codeColX + TextRenderer.MeasureText("EMP0000  ·  ", Theme.FontListItem).Width;
        int headerRowHeight = TextRenderer.MeasureText("Ag", Theme.FontColumnHeader).Height + 4;
        _columnHeaderRow.Height = headerRowHeight;
        _lblColCode.Location = new Point(codeColX, 2);
        _lblColCode.Size = new Size(Math.Max(60, nameColX - codeColX), headerRowHeight);
        _lblColName.Location = new Point(nameColX, 2);
        _lblColName.Size = new Size(220, headerRowHeight);
        _columnHeaderRow.Controls.Add(_lblColName);
        _columnHeaderRow.Controls.Add(_lblColCode);

        listCard.Controls.Add(_list);
        listCard.Controls.Add(_columnHeaderRow);
        listCard.Controls.Add(_countLabel);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 72, BackColor = Theme.Surface };

        Theme.StyleDangerButton(_btnDelete);
        _btnDelete.AutoSize = true;
        _btnDelete.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _btnDelete.Padding = new Padding(20, 0, 20, 0);
        _btnDelete.MinimumSize = new Size(100, 40);
        _btnDelete.Enabled = false;
        _btnDelete.Click += BtnDelete_Click;

        Theme.StylePrimaryButton(_btnClose);
        _btnClose.AutoSize = true;
        _btnClose.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _btnClose.Padding = new Padding(20, 0, 20, 0);
        _btnClose.MinimumSize = new Size(110, 40);
        _btnClose.Click += (_, _) => Close();

        void PositionFooterButtons()
        {
            _btnDelete.Location = new Point(18, 16);
            _btnClose.Location = new Point(footer.Width - _btnClose.Width - 18, 16);
        }
        footer.Resize += (_, _) => PositionFooterButtons();
        PositionFooterButtons();

        var topBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
        footer.Controls.Add(_btnDelete);
        footer.Controls.Add(_btnClose);
        footer.Controls.Add(topBorder);

        Controls.Add(listCard);
        Controls.Add(footer);
        Controls.Add(header);

        AcceptButton = _btnClose;
        CancelButton = _btnClose;
    }

    private void List_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool isChecked = _list.GetItemChecked(e.Index);
        bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool alt = e.Index % 2 == 1;

        var bgColor = isSelected ? Theme.PrimaryLight : (alt ? Theme.SurfaceAlt : Theme.Surface);
        using (var bgBrush = new SolidBrush(bgColor))
            g.FillRectangle(bgBrush, e.Bounds);

        const int boxSize = 18;
        var boxRect = new Rectangle(e.Bounds.Left + 10, e.Bounds.Top + (e.Bounds.Height - boxSize) / 2, boxSize, boxSize);
        using var boxPath = Theme.RoundedRect(boxRect, 4);

        if (isChecked)
        {
            using var fillBrush = new SolidBrush(Theme.Primary);
            g.FillPath(fillBrush, boxPath);
            using var checkPen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            var p1 = new Point(boxRect.Left + 4, boxRect.Top + 9);
            var p2 = new Point(boxRect.Left + 7, boxRect.Top + 13);
            var p3 = new Point(boxRect.Left + 14, boxRect.Top + 5);
            g.DrawLines(checkPen, new[] { p1, p2, p3 });
        }
        else
        {
            using var borderPen = new Pen(Theme.BorderStrong, 1.5f);
            g.DrawPath(borderPen, boxPath);
        }

        var text = _list.Items[e.Index]?.ToString() ?? string.Empty;
        var textRect = new Rectangle(boxRect.Right + 12, e.Bounds.Top, e.Bounds.Width - boxRect.Right - 20, e.Bounds.Height);
        TextRenderer.DrawText(g, text, Theme.FontListItem, textRect, Theme.TextPrimary,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        using var linePen = new Pen(Theme.Border);
        g.DrawLine(linePen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var emp in _shown)
            _list.Items.Add(string.IsNullOrWhiteSpace(emp.EnrollNumber) ? emp.Name : $"{emp.EnrollNumber}  ·  {emp.Name}");

        _countLabel.Text = $"{_shown.Count} employee(s) currently enrolled on this machine";
        UpdateDeleteButtonState();
    }

    private void UpdateDeleteButtonState()
    {
        _btnDelete.Enabled = _list.CheckedIndices.Count > 0;
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        var toDelete = _list.CheckedIndices.Cast<int>().Select(i => _shown[i]).ToList();
        if (toDelete.Count == 0) return;

        string message = toDelete.Count == 1
            ? $"Are you sure you want to delete \"{toDelete[0].Name}\" from this machine?"
            : $"Are you sure you want to delete these {toDelete.Count} employees from this machine?";

        var confirmed = MessageBox.Show(this, message, "Delete Employee",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirmed != DialogResult.Yes) return;

        var failed = new List<string>();
        _btnDelete.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            foreach (var emp in toDelete)
            {
                bool ok = _provider != null && _provider.DeleteEmployee(emp.EnrollNumber);
                if (ok)
                {
                    _machineEmployees.RemoveAll(m => m.EnrollNumber == emp.EnrollNumber);
                }
                else
                {
                    failed.Add(string.IsNullOrWhiteSpace(emp.EnrollNumber) ? emp.Name : $"{emp.EnrollNumber} - {emp.Name}");
                }
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        _shown = _machineEmployees.OrderBy(m => m.Name).ToList();
        RefreshList();

        if (failed.Count > 0)
        {
            MessageBox.Show(this,
                "Could not delete the following employee(s) from the machine:\n" + string.Join("\n", failed),
                "Delete Employee", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}