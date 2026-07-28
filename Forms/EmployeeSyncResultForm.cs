using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.Forms;

public class EmployeeSyncResultForm : Form
{
    private readonly Button _btnOk = new() { Text = "Done", Width = 130 };

    public EmployeeSyncResultForm(EmployeeSyncResult result)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Employee Sync";
        Size = new Size(460, 460);
        MinimumSize = new Size(420, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Theme.Background;

        bool hasFailures = result.Failed > 0;

        var header = new HeaderBanner
        {
            Height = 84,
            Title = hasFailures ? "Sync Completed with Issues" : "Employee Sync Completed",
            Subtitle = $"{result.ErpEmployeeCount} ERP employee(s) processed"
        };
        if (hasFailures)
        {
            header.GradientStart = Color.FromArgb(180, 83, 9);
            header.GradientEnd = Theme.Warning;
        }

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24), BackColor = Theme.Background, AutoScroll = true };

        var card = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(20) };

        // Dock=Top children stack with the LAST-added control ending up at the
        // very top, so rows are added bottom row first, top row last.
        AddSummaryRow(card, "Failed", result.Failed, result.Failed > 0 ? Theme.Danger : Theme.TextSecondary);
        AddDivider(card);
        AddSummaryRow(card, "Skipped (already on machine)", result.Skipped, Theme.TextSecondary);
        AddDivider(card);
        AddSummaryRow(card, "Removed", result.Removed, Theme.TextSecondary);
        AddDivider(card);
        AddSummaryRow(card, "Created Successfully", result.CreatedNew, Theme.Success);

        body.Controls.Add(card);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = Theme.Surface };
        var topBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };

        Theme.StylePrimaryButton(_btnOk);
        _btnOk.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 14, 24, 0),
            WrapContents = false,
            AutoSize = false
        };
        buttonFlow.Controls.Add(_btnOk);

        footer.Controls.Add(buttonFlow);
        footer.Controls.Add(topBorder);

        AcceptButton = _btnOk;

        Controls.Add(body);
        Controls.Add(header);
        Controls.Add(footer);
    }

    private static void AddDivider(Control parent)
    {
        parent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });
    }

    private static void AddSummaryRow(Control parent, string label, int value, Color valueColor)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            ColumnCount = 2,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            Font = Theme.FontBody,
            ForeColor = Theme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };
        var val = new Label
        {
            Text = value.ToString("N0"),
            AutoSize = false,
            Width = 60,
            Dock = DockStyle.Fill,
            Font = Theme.FontBodyBold,
            ForeColor = valueColor,
            TextAlign = ContentAlignment.MiddleRight
        };

        row.Controls.Add(lbl, 0, 0);
        row.Controls.Add(val, 1, 0);

        parent.Controls.Add(row);
    }
}