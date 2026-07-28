using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SapeagleAttendanceConnector.Forms;

internal static class Theme
{
    // ---- Palette : Microsoft/Windows blue + gold, premium corporate ----
    public static readonly Color Primary = Color.FromArgb(0, 120, 212);         // Windows Blue (#0078D4)
    public static readonly Color PrimaryDark = Color.FromArgb(0, 90, 158);      // Windows Blue - hover shade (#005A9E)
    public static readonly Color PrimaryDarker = Color.FromArgb(0, 69, 120);    // Windows Blue - pressed shade (#004578)
    public static readonly Color PrimaryLight = Color.FromArgb(222, 236, 249);  // Windows Blue - tint (#DEECF9)
    public static readonly Color Accent = Color.FromArgb(184, 134, 11);         // Gold 600

    public static readonly Color AccentLight = Color.FromArgb(250, 240, 214);   // Gold 50 (badges/pills)
    public static readonly Color AccentDark = Color.FromArgb(133, 96, 6);       // Gold 800 (text on AccentLight)

    public static readonly Color Background = Color.FromArgb(243, 245, 247);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(249, 250, 251);
    public static readonly Color Card = Surface;

    public static readonly Color Border = Color.FromArgb(224, 229, 234);
    public static readonly Color BorderStrong = Color.FromArgb(196, 205, 214);

    public static readonly Color TextPrimary = Color.FromArgb(20, 26, 33);
    public static readonly Color TextSecondary = Color.FromArgb(93, 104, 116);
    public static readonly Color TextMuted = Color.FromArgb(148, 158, 168);
    public static readonly Color TextOnPrimary = Color.White;

    public static readonly Color Danger = Color.FromArgb(220, 38, 38);
    public static readonly Color DangerLight = Color.FromArgb(254, 226, 226);
    public static readonly Color Success = Color.FromArgb(5, 150, 105);
    public static readonly Color SuccessLight = Color.FromArgb(209, 250, 229);
    public static readonly Color Warning = Color.FromArgb(217, 119, 6);
    public static readonly Color WarningLight = Color.FromArgb(254, 243, 199);

    // ---- Typography ----
    public static readonly Font FontTitle = new("Segoe UI", 19, FontStyle.Bold);
    public static readonly Font FontSubtitle = new("Segoe UI", 10.5F, FontStyle.Regular);
    public static readonly Font FontHeading = new("Segoe UI", 13, FontStyle.Bold);
    public static readonly Font FontBody = new("Segoe UI", 10, FontStyle.Regular);
    public static readonly Font FontBodyBold = new("Segoe UI", 10, FontStyle.Bold);
    public static readonly Font FontSmall = new("Segoe UI", 8.5F, FontStyle.Regular);
    public static readonly Font FontSmallBold = new("Segoe UI", 8.5F, FontStyle.Bold);
    public static readonly Font FontStat = new("Segoe UI", 26, FontStyle.Bold);
    public static readonly Font FontButton = new("Segoe UI", 10.5F, FontStyle.Bold);
    public static readonly Font FontButtonLarge = new("Segoe UI", 12F, FontStyle.Bold);

    // Larger variants used specifically for the employee-list rows/headers and the
    // department filter combo, where the default Body/SmallBold sizes were reading
    // too small.
    public static readonly Font FontListItem = new("Segoe UI", 11F, FontStyle.Regular);
    public static readonly Font FontColumnHeader = new("Segoe UI", 9.5F, FontStyle.Bold);
    public static readonly Font FontComboLarge = new("Segoe UI", 11F, FontStyle.Regular);

    // ---- Buttons ----
    public static void StylePrimaryButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = PrimaryDark;
        b.FlatAppearance.MouseDownBackColor = PrimaryDarker;
        b.BackColor = Primary;
        b.ForeColor = TextOnPrimary;
        b.Font = FontButton;
        b.Cursor = Cursors.Hand;
        b.Height = 40;
        b.UseVisualStyleBackColor = false;
        ApplyRoundedCorners(b, 8);
    }

    public static void StyleSecondaryButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = BorderStrong;
        b.FlatAppearance.MouseOverBackColor = SurfaceAlt;
        b.FlatAppearance.MouseDownBackColor = Border;
        b.BackColor = Surface;
        b.ForeColor = TextPrimary;
        b.Font = FontButton;
        b.Cursor = Cursors.Hand;
        b.Height = 40;
        b.UseVisualStyleBackColor = false;
        ApplyRoundedCorners(b, 8);
    }

    public static void StyleDangerOutlineButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.BorderColor = Color.FromArgb(252, 165, 165);
        b.FlatAppearance.MouseOverBackColor = DangerLight;
        b.BackColor = Surface;
        b.ForeColor = Danger;
        b.Font = FontButton;
        b.Cursor = Cursors.Hand;
        b.Height = 40;
        b.UseVisualStyleBackColor = false;
        ApplyRoundedCorners(b, 8);
    }

    /// <summary>Solid red fill, mirroring StylePrimaryButton's solid look but in the
    /// Danger palette - used for destructive actions like "Delete" so they read with
    /// the same visual weight as a solid primary/secondary action button.</summary>
    public static void StyleDangerButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(185, 28, 28);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 27, 27);
        b.BackColor = Danger;
        b.ForeColor = TextOnPrimary;
        b.Font = FontButton;
        b.Cursor = Cursors.Hand;
        b.Height = 40;
        b.UseVisualStyleBackColor = false;
        ApplyRoundedCorners(b, 8);
    }

    public static void StyleTextBox(TextBox t)
    {
        t.BorderStyle = BorderStyle.FixedSingle;
        t.Font = FontBody;
        t.ForeColor = TextPrimary;
        t.BackColor = Surface;
    }

    public static void StyleComboBox(ComboBox c)
    {
        c.FlatStyle = FlatStyle.Flat;
        c.Font = FontBody;
        c.ForeColor = TextPrimary;
        c.BackColor = Surface;
    }

    public static void StyleCheckBox(CheckBox c)
    {
        c.Font = FontBodyBold;
        c.ForeColor = TextPrimary;
        c.Cursor = Cursors.Hand;
        c.FlatStyle = FlatStyle.Flat;
        c.FlatAppearance.BorderSize = 0;
    }

    public static void StyleRadioButton(RadioButton r)
    {
        r.Font = FontBody;
        r.ForeColor = TextPrimary;
        r.Cursor = Cursors.Hand;
    }

    /// <summary>Clips a control to a rounded-rectangle Region, re-applied on resize so buttons stay rounded at any size.</summary>
    public static void ApplyRoundedCorners(Control c, int radius = 10)
    {
        void SetRegion()
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            c.Region = new Region(RoundedRect(new Rectangle(0, 0, c.Width, c.Height), radius));
        }
        c.Resize += (_, _) => SetRegion();
        SetRegion();
    }

    /// <summary>Rounded-rectangle GraphicsPath, used by CardPanel and other custom-drawn controls.</summary>
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();

        if (d <= 0 || d > bounds.Width || d > bounds.Height)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.StartFigure();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}