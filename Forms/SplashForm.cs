namespace SapeagleAttendanceConnector.Forms;

public class SplashForm : Form
{
    public SplashForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Sapeagle Attendance Connector";
        Size = new Size(420, 220);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.White;

        Controls.Add(new Label
        {
            Text = "Sapeagle Attendance Connector",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 80
        });

        Controls.Add(new Label
        {
            Text = "Starting...",
            Font = new Font("Segoe UI", 9),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Bottom,
            Height = 40
        });
    }
}