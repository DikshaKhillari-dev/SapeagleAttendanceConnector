using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SapeagleAttendanceConnector.Forms;

/// <summary>A plain Panel with double buffering turned on. Used for spots where a
/// child Label's text changes at runtime (e.g. status messages) - without this, fast
/// consecutive text updates on a non-buffered ancestor can leave stale pixels behind
/// from the previous string, making the new text look garbled/overlapped.</summary>
internal class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }
}

/// <summary>A flat white "card" surface with rounded corners and a soft border, used
/// throughout the app to group related content instead of loose floating controls.</summary>
internal class CardPanel : Panel
{
    public int CornerRadius { get; set; } = 16;
    public Color BorderColor { get; set; } = Theme.Border;
    public Color SurfaceColor { get; set; } = Theme.Surface;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = SurfaceColor;
        Padding = new Padding(18);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Paint with the *effective* background behind us so the rounded corners blend
        // seamlessly. We can't just trust the immediate Parent.BackColor: several of our
        // layout containers (TableLayoutPanel wrappers, CenteredColumn's inner host, etc.)
        // are deliberately Color.Transparent, and clearing to a transparent colour paints
        // solid white on a normal (non-layered) control instead of showing what's really
        // behind it — which made card corners look flat/sharp instead of smoothly rounded.
        e.Graphics.Clear(GetEffectiveBackColor());
    }

    private Color GetEffectiveBackColor()
    {
        var c = Parent;
        while (c != null)
        {
            if (c.BackColor.A > 0 && c.BackColor != Color.Transparent)
                return c.BackColor;
            c = c.Parent;
        }
        return Theme.Background;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, CornerRadius);
        using var backBrush = new SolidBrush(SurfaceColor);
        e.Graphics.FillPath(backBrush, path);
        using var pen = new Pen(BorderColor, 1);
        e.Graphics.DrawPath(pen, path);
        base.OnPaint(e);
    }
}

/// <summary>Small metric tile - big number + caption - used for quick-glance stats
/// like "Machine Employees" / "ERP Employees" counts.</summary>
internal class StatCard : CardPanel
{
    private readonly Label _lblValue = new()
    {
        Dock = DockStyle.Top,
        Height = 48,
        Font = Theme.FontStat,
        ForeColor = Theme.TextPrimary,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
    };

    private readonly Label _lblCaption = new()
    {
        Dock = DockStyle.Top,
        Height = 22,
        Font = Theme.FontSmallBold,
        ForeColor = Theme.TextSecondary,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private readonly Panel _accentDot = new()
    {
        Dock = DockStyle.Top,
        Height = 4,
        Margin = new Padding(0, 0, 0, 10)
    };

    public string Value
    {
        get => _lblValue.Text;
        set => _lblValue.Text = value;
    }

    public string Caption
    {
        get => _lblCaption.Text;
        set => _lblCaption.Text = value.ToUpperInvariant();
    }

    public Color AccentColor
    {
        get => _accentDot.BackColor;
        set => _accentDot.BackColor = value;
    }

    private readonly Label _lblHint = new()
    {
        Dock = DockStyle.Top,
        Height = 18,
        Font = Theme.FontSmallBold,
        ForeColor = Theme.Accent,
        TextAlign = ContentAlignment.MiddleLeft,
        Visible = false,
        Text = "View names \u2192"
    };

    /// <summary>When set, the card becomes clickable and shows a small "View names" hint
    /// that raises this handler (e.g. to pop up the full list of names behind a count).</summary>
    public Action? OnViewDetails
    {
        set
        {
            _lblHint.Visible = value != null;
            Cursor = value != null ? Cursors.Hand : Cursors.Default;
            _lblHint.Cursor = Cursors.Hand;

            _lblHint.Click -= HintClickHandler;
            Click -= HintClickHandler;
            if (value != null)
            {
                _clickHandler = value;
                _lblHint.Click += HintClickHandler;
                Click += HintClickHandler;
            }
            PositionContent();
        }
    }

    private Action? _clickHandler;
    private void HintClickHandler(object? sender, EventArgs e) => _clickHandler?.Invoke();

    private readonly Panel _iconChip = new() { Size = new Size(30, 30), Visible = false };
    private readonly Label _lblIcon = new()
    {
        Dock = DockStyle.Fill,
        Font = new Font("Segoe MDL2 Assets", 13F),
        ForeColor = Theme.Accent,
        TextAlign = ContentAlignment.MiddleCenter
    };

    public string IconGlyph
    {
        get => _lblIcon.Text;
        set
        {
            _lblIcon.Text = value;
            _iconChip.Visible = !string.IsNullOrEmpty(value);
            _accentDot.Visible = string.IsNullOrEmpty(value);
            PositionContent();
        }
    }

    public Color IconBackColor
    {
        get => _iconChip.BackColor;
        set => _iconChip.BackColor = value;
    }

    public Color IconForeColor
    {
        get => _lblIcon.ForeColor;
        set => _lblIcon.ForeColor = value;
    }

    public StatCard()
    {
        Padding = new Padding(18, 16, 18, 18);
        CornerRadius = 16;

        // These four labels used to rely on Dock = DockStyle.Top stacking, whose visual
        // top-to-bottom order depends on the order controls are added to the collection
        // (and is easy to get backwards). That ambiguity is exactly what caused the
        // value ("35") to render clipped/overlapping the caption below it. Positioning
        // them explicitly, in the exact order we want (value -> accent bar -> caption ->
        // optional hint), removes that ambiguity entirely.
        _lblValue.Dock = DockStyle.None;
        _accentDot.Dock = DockStyle.None;
        _lblCaption.Dock = DockStyle.None;
        _lblHint.Dock = DockStyle.None;

        // Grow to the font's real measured height (plus a little breathing room)
        // rather than trusting a hardcoded pixel value - on some DPI settings the
        // fixed height was shorter than the rendered "26pt bold" digits, clipping
        // the top/bottom of the stat number.
        _lblValue.Height = Math.Max(_lblValue.Height, TextRenderer.MeasureText("0", Theme.FontStat).Height + 8);
        _lblCaption.Height = Math.Max(_lblCaption.Height, TextRenderer.MeasureText("0", Theme.FontSmallBold).Height + 4);

        Controls.Add(_lblValue);
        Controls.Add(_accentDot);
        Controls.Add(_lblCaption);
        Controls.Add(_lblHint);

        AccentColor = Theme.Primary;
        _iconChip.Controls.Add(_lblIcon);
        Theme.ApplyRoundedCorners(_iconChip, 8);
        Controls.Add(_iconChip);
        _iconChip.BringToFront();
        Resize += (_, _) => PositionContent();
        PositionContent();
    }

    private void PositionContent()
    {
        if (Width <= 0 || Height <= 0) return;

        int reservedRight = _iconChip.Visible ? _iconChip.Width + 8 : 0;
        int contentWidth = Math.Max(10, Width - Padding.Left - Padding.Right - reservedRight);
        int left = Padding.Left;
        int y = Padding.Top;

        _lblValue.Location = new Point(left, y);
        _lblValue.Size = new Size(contentWidth, _lblValue.Height);
        y += _lblValue.Height;

        _accentDot.Location = new Point(left, y);
        _accentDot.Size = new Size(Math.Min(40, contentWidth), _accentDot.Height);
        y += _accentDot.Height + _accentDot.Margin.Bottom;

        _lblCaption.Location = new Point(left, y);
        _lblCaption.Size = new Size(contentWidth, _lblCaption.Height);
        y += _lblCaption.Height;

        if (_lblHint.Visible)
        {
            _lblHint.Location = new Point(left, y);
            _lblHint.Size = new Size(contentWidth, _lblHint.Height);
        }

        PositionIconChip();
    }

    private void PositionIconChip()
    {
        _iconChip.Location = new Point(Width - Padding.Right - _iconChip.Width, Padding.Top);
    }
}

internal class HeaderBanner : Panel
{
    private string _title = string.Empty;
    private string _subtitle = string.Empty;

    public string Title
    {
        get => _title;
        set { _title = value ?? string.Empty; Invalidate(); }
    }

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value ?? string.Empty; Invalidate(); }
    }

    /// <summary>Left-to-right gradient start colour. Defaults to the app's primary indigo.</summary>
    public Color GradientStart { get; set; } = Theme.PrimaryDark;

    /// <summary>Left-to-right gradient end colour. Defaults to the app's primary indigo.</summary>
    public Color GradientEnd { get; set; } = Theme.Primary;

    private const int LeftPad = 28;
    private const int RightPad = 20;
    private const int TextGap = 4;
    private static readonly Color SubtitleColor = Color.FromArgb(224, 222, 253);

    public HeaderBanner()
    {
        Dock = DockStyle.Top;
        Height = 92;
        BackColor = Theme.Primary;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Everything (gradient background + title + subtitle) is drawn in this single
        // pass, so there's no separate child-control repaint that can race against the
        // background fill and leave stale/ghosted glyphs behind.
        using var brush = new LinearGradientBrush(
            ClientRectangle.Width > 0 && ClientRectangle.Height > 0 ? ClientRectangle : new Rectangle(0, 0, 1, 1),
            GradientStart,
            GradientEnd,
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        // Title/subtitle are measured and stacked at paint time (instead of using fixed
        // pixel rectangles) so that DPI scaling or font changes never push the subtitle
        // up into the title (or vice-versa) — the previous hardcoded rects caused exactly
        // that overlap on higher-DPI displays where the rendered text is taller than the
        // rect it was pinned into.
        bool hasTitle = !string.IsNullOrEmpty(_title);
        bool hasSubtitle = !string.IsNullOrEmpty(_subtitle);
        int maxTextWidth = Math.Max(20, Width - LeftPad - RightPad);

        Size titleSize = hasTitle
            ? TextRenderer.MeasureText(e.Graphics, _title, Theme.FontTitle, new Size(maxTextWidth, int.MaxValue), TextFormatFlags.NoPrefix)
            : Size.Empty;
        Size subtitleSize = hasSubtitle
            ? TextRenderer.MeasureText(e.Graphics, _subtitle, Theme.FontSubtitle, new Size(maxTextWidth, int.MaxValue), TextFormatFlags.NoPrefix)
            : Size.Empty;

        int blockHeight = titleSize.Height + (hasSubtitle ? TextGap + subtitleSize.Height : 0);
        int y = Math.Max(8, (Height - blockHeight) / 2);

        if (hasTitle)
        {
            var titleRect = new Rectangle(LeftPad, y, maxTextWidth, titleSize.Height);
            TextRenderer.DrawText(e.Graphics, _title, Theme.FontTitle, titleRect, Theme.TextOnPrimary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += titleSize.Height + TextGap;
        }

        if (hasSubtitle)
        {
            var subtitleRect = new Rectangle(LeftPad, y, maxTextWidth, subtitleSize.Height);
            TextRenderer.DrawText(e.Graphics, _subtitle, Theme.FontSubtitle, subtitleRect, SubtitleColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        base.OnPaint(e);
    }
}

/// <summary>A small filled circle used as a quick-glance status indicator.</summary>
internal class DotIndicator : Panel
{
    public DotIndicator()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillEllipse(brush, new Rectangle(0, 0, Width - 1, Height - 1));
    }
}

/// <summary>Small rounded pill for status labels — e.g. "Connected", "Active".</summary>
internal class Badge : Panel
{
    private readonly Label _lbl = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = Theme.FontSmallBold
    };

    public new string Text
    {
        get => _lbl.Text;
        set { _lbl.Text = value; UpdateSize(); }
    }

    public Color PillBackColor { get; set; } = Theme.AccentLight;

    public Color PillForeColor
    {
        get => _lbl.ForeColor;
        set => _lbl.ForeColor = value;
    }

    public Badge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                  ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Padding = new Padding(14, 6, 14, 6);
        PillForeColor = Theme.AccentDark;
        Controls.Add(_lbl);
    }

    private void UpdateSize()
    {
        var textSize = TextRenderer.MeasureText(_lbl.Text, _lbl.Font);
        Size = new Size(textSize.Width + Padding.Left + Padding.Right,
                         textSize.Height + Padding.Top + Padding.Bottom);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundedRect(rect, Height / 2);
        using var brush = new SolidBrush(PillBackColor);
        e.Graphics.FillPath(brush, path);
        base.OnPaint(e);
    }
}

/// <summary>Centers a fixed-max-width column of content inside a Dock.Fill container,
/// so forms stay readable and elegant even when the window is maximized on a wide screen.
/// On smaller/lower-resolution laptop screens the column width shrinks to fit instead of
/// overflowing the visible area (which is what caused content to look "cut off").</summary>
internal class CenteredColumn : TableLayoutPanel
{
    private readonly int _maxWidth;
    private const int MinWidth = 320;
    private const int SideMargin = 16;

    public Control Content
    {
        set
        {
            var host = (Panel)GetControlFromPosition(1, 0)!;
            host.Controls.Clear();
            if (value != null)
            {
                value.Dock = DockStyle.Fill;
                host.Controls.Add(value);
            }
        }
    }

    public CenteredColumn(int maxWidth)
    {
        _maxWidth = maxWidth;

        Dock = DockStyle.Fill;
        ColumnCount = 3;
        RowCount = 1;
        BackColor = Color.Transparent;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, maxWidth));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var spacerLeft = new Panel { Dock = DockStyle.Fill };
        var host = new Panel { Dock = DockStyle.Fill };
        var spacerRight = new Panel { Dock = DockStyle.Fill };

        Controls.Add(spacerLeft, 0, 0);
        Controls.Add(host, 1, 0);
        Controls.Add(spacerRight, 2, 0);

        Resize += (_, _) => AdjustColumnWidth();
        AdjustColumnWidth();
    }

    private void AdjustColumnWidth()
    {
        if (Width <= 0) return;

        // Shrink the fixed-width column to fit whenever the available width (laptop
        // screen, smaller window, different DPI scaling, etc.) is narrower than the
        // ideal max width, instead of letting it overflow and clip content.
        int available = Width - SideMargin;
        int target = Math.Max(MinWidth, Math.Min(_maxWidth, available));

        if (ColumnStyles[1].Width != target)
            ColumnStyles[1] = new ColumnStyle(SizeType.Absolute, target);
    }
}