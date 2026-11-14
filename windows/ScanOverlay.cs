namespace AstromechDesk;

sealed class ScanOverlay : Form
{
    private Point eye;
    private Point target;
    private Rectangle? icon;
    private string? caption;

    public ScanOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = TransparencyKey = Color.Fuchsia;
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var style = base.CreateParams;
            style.ExStyle |= 0x08000000 | 0x20 | 0x80;
            return style;
        }
    }

    public void Aim(Point origin, Point point, string? label = null, Rectangle? iconBounds = null)
    {
        if (!Visible) { Bounds = SystemInformation.VirtualScreen; Show(); }
        eye = PointToClient(origin);
        target = PointToClient(point);
        icon = iconBounds is Rectangle bounds ? RectangleToClient(bounds) : null;
        caption = label;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        using var glow = new Pen(Color.FromArgb(92, 12, 43), 7);
        using var red = new Pen(Color.FromArgb(255, 39, 93), 3);
        using var core = new Pen(Color.FromArgb(255, 188, 209));
        g.DrawLine(glow, eye, target);
        g.DrawLine(red, eye, target);
        g.DrawLine(core, eye, target);
        g.DrawRectangle(red, target.X - 8, target.Y - 8, 16, 16);
        if (caption is null) return;

        var screen = RectangleToClient(Screen.FromPoint(PointToScreen(target)).WorkingArea);
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
        int width = Math.Min(TextRenderer.MeasureText(caption, font).Width + 22, Math.Min(540, screen.Width));
        int x = Math.Clamp(target.X - width / 2, screen.Left, screen.Right - width);
        int top = Math.Min((icon?.Bottom ?? target.Y + 12) + 6, screen.Bottom - 34);
        var card = new Rectangle(x, top, width, 32);
        using var panel = new SolidBrush(Color.FromArgb(15, 22, 32));
        using var rim = new Pen(Color.FromArgb(74, 241, 229));
        g.FillRectangle(panel, card);
        g.DrawRectangle(rim, card);
        card.Inflate(-8, 0);
        TextRenderer.DrawText(g, caption, font, card, Color.WhiteSmoke, flags);
    }

    public async Task SweepAsync(Func<Point> origin, Screen[] screens)
    {
        foreach (Screen screen in screens)
        {
            Rectangle area = screen.Bounds;
            for (int i = 0; i <= 38; i++)
            {
                double progress = i / 38d;
                double across = (Math.Sin(progress * Math.PI * 7 - Math.PI / 2) + 1) / 2;
                Aim(origin(), new Point(area.Left + 4 + (int)((area.Width - 9) * across),
                    area.Top + 4 + (int)((area.Height - 9) * progress)));
                await Task.Delay(19);
            }
        }
    }
}
