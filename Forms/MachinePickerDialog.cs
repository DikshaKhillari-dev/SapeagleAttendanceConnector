using SapeagleAttendanceConnector.Models;

namespace SapeagleAttendanceConnector.Forms;

public class MachinePickerDialog : Form
{
    private readonly ComboBox _combo = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _btnOk = new() { Text = "OK", DialogResult = DialogResult.OK };
    private readonly Button _btnCancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };

    public MachineConfig? SelectedMachine { get; private set; }

    public MachinePickerDialog(List<MachineConfig> machines)
    {
        Text = "Select Machine";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 120);

        _combo.DataSource = machines;
        _combo.DisplayMember = nameof(MachineConfig.MachineName);
        _combo.ValueMember = nameof(MachineConfig.Id);
        _combo.Margin = new Padding(16);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttonPanel.Controls.Add(_btnOk);
        buttonPanel.Controls.Add(_btnCancel);

        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        host.Controls.Add(_combo);

        Controls.Add(host);
        Controls.Add(buttonPanel);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        _btnOk.Click += (_, _) => SelectedMachine = _combo.SelectedItem as MachineConfig;
    }
}