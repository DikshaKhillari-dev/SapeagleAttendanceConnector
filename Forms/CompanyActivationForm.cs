using SapeagleAttendanceConnector.Models;
using SapeagleAttendanceConnector.Services;

namespace SapeagleAttendanceConnector.Forms;

public class CompanyActivationForm : Form
{
    private readonly ApiService _apiService;
    private readonly ConfigService _configService;

    private readonly TextBox _txtCompanyCode = new() { Dock = DockStyle.Top, Height = 32 };
    private readonly TextBox _txtActivationKey = new() { Dock = DockStyle.Top, Height = 32 };
    private readonly Button _btnVerify = new() { Text = "Verify & Activate" };
    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Top,
        Height = 0,
        AutoSize = false,
        Font = Theme.FontSmall,
        TextAlign = ContentAlignment.MiddleLeft,
        Visible = false
    };

    public CompanyActivationForm(ApiService apiService, ConfigService configService)
    {
        _apiService = apiService;
        _configService = configService;

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "Company Activation";
        MinimumSize = new Size(480, 420);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = Theme.Background;
        WindowState = FormWindowState.Maximized;

        var header = new HeaderBanner { Title = "Company Activation", Subtitle = "Connect this machine to your Sapeagle ERP account", Height = 92 };

        Theme.StyleTextBox(_txtCompanyCode);
        Theme.StyleTextBox(_txtActivationKey);
        Theme.StylePrimaryButton(_btnVerify);
        _btnVerify.Dock = DockStyle.Top;

        var lblCode = new Label
        {
            Text = "COMPANY CODE",
            Dock = DockStyle.Top,
            Height = 20,
            Font = Theme.FontSmallBold,
            ForeColor = Theme.TextSecondary
        };
        var lblKey = new Label
        {
            Text = "ACTIVATION KEY",
            Dock = DockStyle.Top,
            Height = 20,
            Font = Theme.FontSmallBold,
            ForeColor = Theme.TextSecondary
        };

        var card = new CardPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(24) };

        // Bottom-to-top add order: last item added ends up visually at the top.
        card.Controls.Add(_btnVerify);
        Spacer(card, 22);
        card.Controls.Add(_lblStatus);
        Spacer(card, 8);
        card.Controls.Add(_txtActivationKey);
        Spacer(card, 6);
        card.Controls.Add(lblKey);
        Spacer(card, 18);
        card.Controls.Add(_txtCompanyCode);
        Spacer(card, 6);
        card.Controls.Add(lblCode);

        var centered = new CenteredColumn(460) { Padding = new Padding(0, 40, 0, 40) };
        centered.Content = card;

        var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        scrollHost.Controls.Add(centered);

        Controls.Add(scrollHost);
        Controls.Add(header);

        _btnVerify.Click += BtnVerify_Click;
        AcceptButton = _btnVerify;
    }

    private static void Spacer(Control parent, int height)
    {
        parent.Controls.Add(new Panel { Dock = DockStyle.Top, Height = height, BackColor = Color.Transparent });
    }

    private void ShowStatus(string message, bool isError)
    {
        _lblStatus.Text = message;
        _lblStatus.ForeColor = isError ? Theme.Danger : Theme.TextSecondary;
        _lblStatus.Height = string.IsNullOrEmpty(message) ? 0 : 36;
        _lblStatus.Visible = !string.IsNullOrEmpty(message);
    }

    private async void BtnVerify_Click(object? sender, EventArgs e)
    {
        var code = _txtCompanyCode.Text.Trim();
        var key = _txtActivationKey.Text.Trim();

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(key))
        {
            ShowStatus("Please enter both Company Code and Activation Key.", isError: true);
            return;
        }

        _btnVerify.Enabled = false;
        ShowStatus("Verifying...", isError: false);

        Logger.Log($"[Activation] BtnVerify_Click: CompanyCode='{code}' ActivationKey='{key}'");

        var result = await _apiService.ActivateAsync(code, key);

        if (result.Success)
        {
            var config = _configService.Load();

            Logger.Log($"[Activation] Result: MachineId={result.MachineId}, MachineName='{result.MachineName}', " +
                       $"ComId={result.ComId}, CompanyName='{result.CompanyName}'. " +
                       $"Currently activated MachineIds in local config=[{string.Join(", ", config.Machines.Select(m => m.MachineId))}]");

            if (config.Machines.Any(m => m.MachineId == result.MachineId))
            {
                Logger.Log($"[Activation] MachineId={result.MachineId} already present in local config, rejecting duplicate add.");
                ShowStatus("This machine is already added.", isError: true);
                _btnVerify.Enabled = true;
                return;
            }

            config.Machines.Add(new ActivatedMachine
            {
                ComId = result.ComId,
                CompanyCode = code,
                CompanyName = result.CompanyName,
                MachineId = result.MachineId,
                MachineName = result.MachineName,
                ActivationKey = key,
                ActivatedOn = DateTime.Now
            });

            _configService.Save(config);
            Logger.Log($"[Activation] MachineId={result.MachineId} ('{result.MachineName}') added to local config. " +
                       $"Local config now has {config.Machines.Count} activated machine(s): [{string.Join(", ", config.Machines.Select(m => $"{m.MachineId}:{m.MachineName}"))}]");
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            Logger.Log($"[Activation] Activation failed for CompanyCode='{code}': {result.Message}");
            ShowStatus(result.Message, isError: true);
            _btnVerify.Enabled = true;
        }
    }
}