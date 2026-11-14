using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;

namespace AstromechDesk;

sealed class DroidOverlay : Form
{
    private static readonly Color Key = Color.Fuchsia;
    private readonly System.Windows.Forms.Timer frames = new() { Interval = 33 };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly MemoryStream chirpData = MakeChirp();
    private readonly SoundPlayer chirp;
    private double effectStart = -10;
    private double effectUntil = -10;
    private bool scanning;
    private bool scanSound;
    private Point dragStart;
    private Point pressPosition;
    private bool dragging;
    private double lastClick = -10;
    private Point travelStart;
    private Point travelEnd;
    private double travelStarted;
    private double chaseUntil;
    private double nextChase;
    private bool traveling;
    private int facing = 1;
    private float stride;
    private float bounce;
    private float stretch = 1;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool MouseChaseEnabled { get; set; } = true;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int MouseChaseSeconds { get; set; } = 5;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool SettingsVisible { get; set; }

    public event Action? OpenControls;
    public event Action? ScanRequested;

    public DroidOverlay()
    {
        chirp = new SoundPlayer(chirpData);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = Key;
        TransparencyKey = Key;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(176, 192);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        ScheduleChase();
        frames.Tick += (_, _) => AdvanceAnimation();
        frames.Start();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var style = base.CreateParams;
            style.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return style;
        }
    }

    public void BeginScan(bool sound)
    {
        StopMotion();
        effectStart = clock.Elapsed.TotalSeconds;
        scanning = true;
        scanSound = sound;
        Play(sound);
    }

    public Point EyePosition => PointToScreen(new Point(facing > 0 ? 107 : 69, 63 + (int)bounce));

    public async Task ChaseFileAsync(Point target)
    {
        if (!Visible || Capture) return;
        travelStart = Location;
        travelEnd = Nearby(target);
        travelStarted = clock.Elapsed.TotalSeconds;
        traveling = true;
        while (traveling && Visible) await Task.Delay(30);
    }

    private Point Nearby(Point target)
    {
        Rectangle area = Screen.FromPoint(target).WorkingArea;
        int x = target.X + 35;
        if (x + Width > area.Right) x = target.X - Width - 35;
        return new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(target.Y - Height / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    private void ScheduleChase() => nextChase = clock.Elapsed.TotalSeconds + Random.Shared.Next(25, 61);

    private void StopMotion()
    {
        traveling = false;
        chaseUntil = 0;
        stride = 0;
        stretch = 1;
        ScheduleChase();
    }

    private void AdvanceAnimation()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (!Visible)
        {
            if (traveling || chaseUntil > 0) StopMotion();
            return;
        }
        if (!MouseChaseEnabled || SettingsVisible || scanning || Capture)
        {
            if (chaseUntil > 0) StopMotion();
        }
        else if (chaseUntil == 0 && !traveling && now > nextChase)
            chaseUntil = now + MouseChaseSeconds;

        if (chaseUntil > 0 && now >= chaseUntil) StopMotion();
        Point previous = Location;
        if (traveling)
        {
            double progress = Math.Clamp((now - travelStarted) / .56, 0, 1);
            double ease = progress * progress * (3 - 2 * progress);
            Location = new Point((int)Math.Round(travelStart.X + (travelEnd.X - travelStart.X) * ease),
                (int)Math.Round(travelStart.Y + (travelEnd.Y - travelStart.Y) * ease));
            if (progress >= 1) traveling = false;
        }
        else if (chaseUntil > 0)
        {
            Point cursor = Cursor.Position;
            Point target = Nearby(cursor);
            double dx = target.X - Location.X;
            double dy = target.Y - Location.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > 3)
            {
                double step = Math.Min(distance, 17);
                Location = new Point(Location.X + (int)Math.Round(dx * step / distance),
                    Location.Y + (int)Math.Round(dy * step / distance));
            }
        }

        bool moving = previous != Location;
        if (moving && previous.X != Location.X) facing = Location.X > previous.X ? 1 : -1;
        stride = moving || chaseUntil > 0 ? (float)Math.Sin(now * 30) : 0;
        bounce = moving || chaseUntil > 0 ? -Math.Abs(stride) * 11 : (float)Math.Sin(now * 3) * 2;
        stretch = 1 + stride * .07f;
        Invalidate();
    }

    public void EndScanAnimation()
    {
        if (scanning) EndScan(scanSound);
    }

    public void EndScan(bool sound)
    {
        scanning = false;
        effectUntil = Math.Max(clock.Elapsed.TotalSeconds + .15, effectStart + 1.65);
        Play(sound);
    }

    private void Play(bool enabled)
    {
        if (!enabled) return;
        try { chirp.Play(); }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException) { }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        StopMotion();
        if (e.Button == MouseButtons.Left)
        {
            dragStart = e.Location;
            pressPosition = Cursor.Position;
            dragging = false;
            Capture = true;
        }
        if (e.Button == MouseButtons.Right) OpenControls?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Capture || e.Button != MouseButtons.Left) return;
        Point mouse = Cursor.Position;
        if (!dragging && Math.Abs(mouse.X - pressPosition.X) < SystemInformation.DragSize.Width / 2 &&
            Math.Abs(mouse.Y - pressPosition.Y) < SystemInformation.DragSize.Height / 2) return;
        dragging = true;
        Location = new Point(mouse.X - dragStart.X, mouse.Y - dragStart.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        Capture = false;
        if (dragging) { dragging = false; return; }
        double now = clock.Elapsed.TotalSeconds;
        bool repeat = (now - lastClick) * 1000 <= SystemInformation.DoubleClickTime;
        lastClick = now;
        if (!repeat) ScanRequested?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        double now = clock.Elapsed.TotalSeconds;
        bool beam = scanning || now < effectUntil;
        g.SmoothingMode = SmoothingMode.None; // Keep the transparency key out of silhouette pixels.
        if (chaseUntil > 0)
        {
            using var streak = new Pen(Color.FromArgb(255, 110, 178), 3);
            for (int i = 0; i < 3; i++)
            {
                int x = facing > 0 ? 8 : 144;
                int y = 100 + i * 13 + (int)(stride * 5);
                g.DrawLine(streak, x, y, x + 16 + i * 4, y - 3);
            }
        }
        if (facing < 0) { g.TranslateTransform(176, 0); g.ScaleTransform(-1, 1); }
        var ink = Color.FromArgb(30, 12, 63);
        var purple = Color.FromArgb(119, 54, 215);
        var pink = Color.FromArgb(255, 73, 165);
        var cyan = Color.FromArgb(75, 245, 232);
        var peach = Color.FromArgb(255, 156, 111);
        using var outline = new Pen(ink, 5) { LineJoin = LineJoin.Round };
        using var dark = new SolidBrush(ink);
        using var turquoise = new SolidBrush(cyan);
        using var rose = new SolidBrush(pink);
        using var sunset = new SolidBrush(peach);
        using var lilac = new SolidBrush(Color.FromArgb(196, 135, 255));
        using var shell = new LinearGradientBrush(new Rectangle(38, 26, 108, 123), pink, purple, 90);
        using var visor = new SolidBrush(Color.FromArgb(43, 23, 83));
        void Oval(Brush color, float x, float y, float width, float height)
        {
            g.FillEllipse(color, x, y, width, height);
            g.DrawEllipse(outline, x, y, width, height);
        }

        void Limb(PointF start, PointF end, Brush color, bool boot)
        {
            var joint = new PointF((start.X + end.X) / 2 - 8, (start.Y + end.Y) / 2);
            using var outer = new Pen(ink, boot ? 13 : 10) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var inner = new Pen(color, boot ? 7 : 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLines(outer, [start, joint, end]);
            g.DrawLines(inner, [start, joint, end]);
            if (boot) Oval(color, end.X - 12, end.Y - 6, 31, 15);
            else Oval(color, end.X - 8, end.Y - 8, 17, 17);
        }

        float step = stride * 30;
        float lift = Math.Max(0, stride) * 15;
        Limb(new(80, 135 + bounce), new(77 - step, 175 - lift), lilac, true);
        Limb(new(108, 135 + bounce), new(112 + step, 175 - Math.Max(0, -stride) * 15), turquoise, true);
        Limb(new(112, 107 + bounce), new(141 - step * .55f, 126 + bounce), lilac, false);

        g.TranslateTransform(88, 112 + bounce);
        g.ScaleTransform(stretch, 1 / stretch);
        g.TranslateTransform(-88, -112);
        using var aerial = new Pen(ink, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(aerial, 73, 36, 62 + step * .35f, 15);
        Oval(sunset, 54 + step * .35f, 7, 16, 15);
        Oval(shell, 55, 83, 72, 62);
        Oval(turquoise, 76, 106, 35, 22);
        using var meter = new Pen(ink, 2);
        g.DrawLines(meter, [new Point(81, 118), new(88, 118), new(92, 112), new(97, 122), new(102, 117), new(106, 117)]);

        Oval(shell, 39, 28, 105, 70);
        Oval(lilac, 31, 51, 23, 30);
        Oval(visor, 56, 44, 84, 41);
        using var eye = new SolidBrush(beam ? peach : cyan);
        bool blink = now % 4.8 < .13;
        g.FillEllipse(eye, 99, blink ? 62 : 54, 16, blink ? 3 : 18);
        g.FillEllipse(eye, 73, blink ? 62 : 56, 12, blink ? 3 : 15);
        if (!blink)
        {
            g.FillEllipse(dark, 106, 59, 7, 9);
            g.FillEllipse(dark, 78, 60, 5, 7);
        }
        using var smile = new Pen(pink, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(smile, 87, 68, 15, 9, 10, 160);
        Limb(new(60, 107), new(33 + step * .55f, 122 - stride * 8), rose, false);
    }

    private static MemoryStream MakeChirp()
    {
        const int rate = 22050;
        const int samples = 10584;
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double time = (double)i / rate;
            double beat = (time * 8) % 1;
            int note = (int)(time * 8);
            double frequency = 680 + 140 * note + 330 * Math.Sin(beat * Math.PI);
            double envelope = Math.Min(1, beat * 12) * Math.Clamp((.88 - beat) * 8, 0, 1);
            phase += 2 * Math.PI * frequency / rate;
            writer.Write((short)(4800 * envelope * (Math.Sin(phase) + .21 * Math.Sin(2 * phase))));
        }
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            frames.Dispose();
            chirp.Dispose();
            chirpData.Dispose();
        }
        base.Dispose(disposing);
    }
}
