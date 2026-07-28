using SapeagleAttendanceConnector.Models;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.Forms;

public class SettingsForm : Form
{
    public SettingsForm(ConfigService configService)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Settings";
        Size = new Size(360, 180);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        AutoScroll = true;
        WindowState = FormWindowState.Maximized;

        var btnDeactivate = new Button { Text = "Deactivate Company", Left = 20, Top = 20, Width = 200 };
        btnDeactivate.Click += (_, _) =>
        {
            if (MessageBox.Show("Deactivate this company? Need to restart the app.", "Confirm",
                MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                configService.Save(new CompanyConfig());
                Application.Exit();
            }
        };

        Controls.Add(btnDeactivate);
    }
}