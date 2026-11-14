using System.Diagnostics;
using System.Reflection;
using AstromechDesk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace NeoNibblerMac;

static class Program
{
    [STAThread]
    static void Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect()
        .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
}

sealed class App : Application
{
    private readonly List<Candidate> seen = [];
    private Settings settings = new([], true);
    private IClassicDesktopStyleApplicationLifetime desktop = null!;
    private Window droid = null!;
    private Window panel = null!;
    private Image mascot = null!;
    private StackPanel rules = null!;
    private StackPanel results = null!;
    private TextBlock status = null!;
    private TextBlock caption = null!;
    private Border callout = null!;
    private Window tooltip = null!;
    private Window laser = null!;
    private Beam beam = null!;
    private bool chasingFile;
    private readonly List<(Candidate File, CheckBox Selection)> matches = [];
    private bool busy;
    private bool dragging;
    private bool chasingMouse;
    private Point? pressPoint;
    private string folder = "";
    private string? soundPath;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double nextChase = 30;
    private double chaseUntil;
    private double lastClick = -10;

    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        string startup = "Add an extension rule, then scan your Desktop.";
        try { settings = Sorting.Load(); }
        catch (Exception ex) { startup = ex.Message; }
        using var stream = Resource("Droid.png");
        var bitmap = new Bitmap(stream);
        mascot = new Image { Source = bitmap, Width = 106, Height = 106, Cursor = new Cursor(StandardCursorType.Hand) };
        caption = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        callout = new Border { Child = caption, Background = Brush.Parse("#2A174B"), BorderBrush = Brush.Parse("#58F8E0"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6), IsVisible = false };
        droid = new Window
        {
            Title = "Neo Nibbler", Width = 240, Height = 160, CanResize = false, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Background = Brushes.Transparent,
            Content = mascot
        };
        tooltip = new Window
        {
            Width = 240, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
            ShowActivated = false, Topmost = true, SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Background = Brushes.Transparent,
            Content = callout
        };
        beam = new Beam();
        laser = new Window
        {
            CanResize = false, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            SystemDecorations = SystemDecorations.None, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background = Brushes.Transparent, Content = beam
        };
        mascot.PointerPressed += Press;
        mascot.PointerMoved += Drag;
        mascot.PointerReleased += Release;
        panel = BuildSettings(startup);
        var menu = new NativeMenu();
        AddMenu(menu, "Scan Desktop", async () => await Scan());
        AddMenu(menu, "Settings", ShowSettings);
        AddMenu(menu, "About me", () => ShowMessage("About me",
            "Fahad Snafee\nICT professional based in Basra, Iraq. I manage IT projects and support networks, systems, and Microsoft 365.\n\nWebsite: basrasite.co\nInstagram: @fahadsnafee"));
        AddMenu(menu, "Website · basrasite.co", () => OpenWebsite());
        AddMenu(menu, "Quit Neo Nibbler", () => { if (!busy) desktop.Shutdown(); });
        using var icon = Resource("Droid.png");
        TrayIcon.SetIcons(this, [new TrayIcon { Icon = new WindowIcon(icon), Menu = menu, ToolTipText = "Neo Nibbler" }]);
        droid.Show();
        if (droid.Screens.Primary is { } screen)
            droid.Position = new PixelPoint(screen.WorkingArea.Right - (int)(250 * screen.Scaling),
                screen.WorkingArea.Bottom - (int)(170 * screen.Scaling));
        var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        watch.Tick += async (_, _) => { if (settings.WatchDesktop) await Scan(background: true); };
        watch.Start();
        var animate = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        animate.Tick += (_, _) => Animate();
        animate.Start();
        desktop.Exit += (_, _) =>
        {
            animate.Stop();
            watch.Stop();
            bitmap.Dispose();
            if (soundPath is not null)
                try { File.Delete(soundPath); } catch (IOException) { }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static Stream Resource(string name) => Assembly.GetExecutingAssembly()
        .GetManifestResourceStream("NeoNibbler." + name) ?? throw new InvalidOperationException("Missing asset " + name);

    private static void AddMenu(NativeMenu menu, string title, Action action)
    {
        var item = new NativeMenuItem(title);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private Window BuildSettings(string startup)
    {
        var body = new StackPanel { Margin = new Thickness(22), Spacing = 9 };
        body.Children.Add(new TextBlock { Text = "Neo Nibbler", FontSize = 28, Foreground = Brush.Parse("#58F8E0") });
        var extension = new TextBox { Watermark = ".pdf", Width = 90 };
        var destination = new TextBlock { Text = "Choose a destination", TextWrapping = TextWrapping.Wrap };
        var choose = Button("Browse…", async () =>
        {
            var selected = await panel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                { Title = "Choose destination folder" });
            if (selected.FirstOrDefault()?.TryGetLocalPath() is { } path) destination.Text = folder = path;
        });
        var add = Button("Save rule", () =>
        {
            if (busy) return;
            try
            {
                var rule = Sorting.CreateRule(extension.Text ?? "", folder);
                Save(settings with { Rules = [.. settings.Rules.Where(r => r.Extension != rule.Extension), rule] });
                RefreshRules();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        });
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { extension, choose, add } });
        body.Children.Add(destination);
        rules = new StackPanel { Spacing = 5 };
        body.Children.Add(rules);
        body.Children.Add(Option("Droid sounds", settings.Sound, value => settings with { Sound = value }));
        body.Children.Add(Option("Filename tooltips", settings.FileTooltips, value => settings with { FileTooltips = value }));
        body.Children.Add(Option("Auto-move matches", settings.AutoMove, value => settings with { AutoMove = value }));
        body.Children.Add(Option("Watch Desktop", settings.WatchDesktop, value => settings with { WatchDesktop = value }));
        body.Children.Add(Option("Chase matching files", settings.ChaseFiles, value => settings with { ChaseFiles = value }));
        body.Children.Add(Option("Random mouse chases", settings.ChaseMouse, value => settings with { ChaseMouse = value }));
        body.Children.Add(Option("Use 10-second mouse chases", settings.MouseChaseSeconds == 10,
            value => settings with { MouseChaseSeconds = value ? 10 : 5 }));
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Button("Scan Desktop", async () => await Scan()), Button("Move checked", async () => await MoveChecked()) } });
        status = new TextBlock { Text = startup, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(status);
        results = new StackPanel { Spacing = 5 };
        body.Children.Add(results);
        var window = new Window { Title = "Neo Nibbler Settings", Width = 620, Height = 660,
            Background = Brush.Parse("#21122F"), Content = new ScrollViewer { Content = body } };
        window.Closing += (_, e) =>
        {
            if (e.IsProgrammatic) return;
            e.Cancel = true;
            window.Hide();
        };
        RefreshRules();
        return window;
    }

    private CheckBox Option(string title, bool initial, Func<bool, Settings> update)
    {
        var box = new CheckBox { Content = title, IsChecked = initial };
        box.Click += (_, _) => Save(update(box.IsChecked == true));
        return box;
    }

    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title };
        button.Click += (_, _) => action();
        return button;
    }

    private void Save(Settings value)
    {
        try { Sorting.Save(value); settings = value; seen.Clear(); matches.Clear(); results.Children.Clear(); }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private void RefreshRules()
    {
        rules.Children.Clear();
        foreach (var rule in settings.Rules)
        {
            rules.Children.Add(new TextBlock { Text = rule.Extension + "  →  " + rule.Folder, TextWrapping = TextWrapping.Wrap });
            rules.Children.Add(Button("Remove " + rule.Extension, () =>
            {
                if (busy) return;
                Save(settings with { Rules = settings.Rules.Where(r => r != rule).ToArray() });
                RefreshRules();
            }));
        }
    }

    private void ShowSettings() { panel.Show(); panel.Activate(); }

    private static void ShowMessage(string title, string message)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var script = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
        script.ArgumentList.Add("-e");
        script.ArgumentList.Add("on run argv\ndisplay dialog (item 1 of argv) with title (item 2 of argv) buttons {\"OK\"} default button \"OK\"\nend run");
        script.ArgumentList.Add(message);
        script.ArgumentList.Add(title);
        using var process = Process.Start(script);
    }

    private static void OpenWebsite()
    {
        using var process = Process.Start(new ProcessStartInfo("https://basrasite.co") { UseShellExecute = true });
    }

    private void Press(object? sender, PointerPressedEventArgs e)
    {
        chasingMouse = false;
        nextChase = clock.Elapsed.TotalSeconds + 25;
        var point = e.GetCurrentPoint(droid);
        if (point.Properties.IsRightButtonPressed) { ShowSettings(); return; }
        if (!point.Properties.IsLeftButtonPressed) return;
        pressPoint = e.GetPosition(droid);
        dragging = false;
        e.Pointer.Capture(mascot);
    }

    private void Drag(object? sender, PointerEventArgs e)
    {
        if (pressPoint is not Point start) return;
        var delta = e.GetPosition(droid) - start;
        if (!dragging && delta.X * delta.X + delta.Y * delta.Y < 25) return;
        dragging = true;
        var pointer = droid.PointToScreen(e.GetPosition(droid));
        droid.Position = new PixelPoint(pointer.X - (int)(start.X * droid.RenderScaling),
            pointer.Y - (int)(start.Y * droid.RenderScaling));
    }

    private async void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (pressPoint is null) return;
        pressPoint = null;
        e.Pointer.Capture(null);
        double now = clock.Elapsed.TotalSeconds;
        if (!dragging && now - lastClick > .5) { lastClick = now; await Scan(); }
    }

    private void Animate()
    {
        double now = clock.Elapsed.TotalSeconds;
        if (busy || panel.IsVisible || !settings.ChaseMouse || pressPoint is not null)
            chasingMouse = false;
        else if (!chasingMouse && now >= nextChase)
        {
            chasingMouse = true;
            chaseUntil = now + settings.MouseChaseSeconds;
            nextChase = chaseUntil + Random.Shared.Next(25, 61);
        }

        if (now >= chaseUntil) chasingMouse = false;
        if (chasingMouse && MacDesktop.Cursor() is { } pointer)
            StepToward(pointer, 16);
        bool running = chasingMouse || chasingFile;
        mascot.RenderTransform = new TranslateTransform(running ? Math.Sin(now * 30) * 4 : 0,
            running ? -Math.Abs(Math.Sin(now * 30)) * 10 : Math.Sin(now * 3) * 2);
    }

    private bool StepToward(PixelPoint target, double step)
    {
        var screen = droid.Screens.ScreenFromPoint(target);
        double scale = screen?.Scaling ?? droid.RenderScaling;
        var area = screen?.WorkingArea ?? new PixelRect(target.X, target.Y, 240, 160);
        int width = (int)(240 * scale), height = (int)(160 * scale);
        int x = target.X + 25;
        if (x + width > area.Right) x = target.X - width - 25;
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
        int y = Math.Clamp(target.Y - height / 3, area.Y, Math.Max(area.Y, area.Bottom - height));
        int dx = x - droid.Position.X, dy = y - droid.Position.Y;
        double distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
        if (distance < 3) return false;
        double fraction = Math.Min(1, step / distance);
        droid.Position = new PixelPoint(droid.Position.X + (int)Math.Round(dx * fraction),
            droid.Position.Y + (int)Math.Round(dy * fraction));
        return true;
    }

    private async Task Scan(bool background = false)
    {
        if (busy || (background && settings.Rules.Length == 0)) return;
        if (settings.Rules.Length == 0) { ShowSettings(); return; }
        busy = true;
        var options = settings;
        try
        {
            string path = Sorting.Desktop;
            if (string.IsNullOrEmpty(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            Candidate[] found = await Task.Run(() => Sorting.ScanFiles(Directory.EnumerateFiles(path), options.Rules));
            seen.RemoveAll(item => !found.Contains(item));
            var pending = background ? found.Where(item => !seen.Contains(item)).ToArray() : found;
            if (background && pending.Length == 0) return;
            Play(options.Sound);
            status.Text = "Scanning your Desktop…";
            results.Children.Clear();
            matches.Clear();
            await Sweep();
            laser.Hide();
            var positions = options.ChaseFiles || options.FileTooltips ? await MacDesktop.Icons() : [];
            foreach (var item in pending)
            {
                bool located = positions.TryGetValue(item.Name, out var point);
                if (located && options.ChaseFiles)
                {
                    chasingFile = true;
                    for (int step = 0; step < 30 && pressPoint is null && StepToward(point, 70); step++)
                        await Task.Delay(17);
                    chasingFile = false;
                }
                if (located) Aim(point);
                if (options.FileTooltips)
                {
                    caption.Text = item.Name;
                    callout.IsVisible = true;
                    PixelPoint anchor = located ? point : new PixelPoint(droid.Position.X + 120, droid.Position.Y + 95);
                    if (droid.Screens.ScreenFromPoint(anchor) is { } screen)
                    {
                        var area = screen.WorkingArea;
                        int width = (int)(240 * screen.Scaling);
                        tooltip.Position = new PixelPoint(
                            Math.Clamp(anchor.X - width / 2, area.X, Math.Max(area.X, area.Right - width)),
                            Math.Clamp(anchor.Y + 22, area.Y, Math.Max(area.Y, area.Bottom - 65)));
                    }
                    tooltip.Show();
                    await Task.Delay(750);
                    tooltip.Hide();
                }
                laser.Hide();
                if (options.AutoMove)
                {
                    var moved = await Task.Run(() => Sorting.Move(item));
                    results.Children.Add(new TextBlock { Text = moved.Name + " · " + moved.Outcome, TextWrapping = TextWrapping.Wrap });
                }
                if (!seen.Contains(item)) seen.Add(item);
            }
            if (!options.AutoMove)
                foreach (var item in found)
                {
                    var check = new CheckBox { Content = item.Name + "  →  " + item.Folder, IsChecked = true };
                    matches.Add((item, check));
                    results.Children.Add(check);
                }
            status.Text = found.Length == 0 ? "No matching files." : $"{found.Length} matching files.";
            Play(options.Sound);
        }
        catch (Exception ex) { status.Text = ex.Message; if (!background) ShowSettings(); }
        finally { laser.Hide(); tooltip.Hide(); chasingFile = false; busy = false; }
    }

    private async Task Sweep()
    {
        foreach (var screen in droid.Screens.All)
        {
            var area = screen.Bounds;
            for (int i = 0; i <= 36; i++)
            {
                double along = i / 36d;
                double across = (1 - Math.Cos(along * Math.PI * 7)) / 2;
                Aim(new PixelPoint(area.X + (int)((area.Width - 1) * across),
                    area.Y + (int)((area.Height - 1) * along)));
                await Task.Delay(20);
            }
        }
    }

    private void Aim(PixelPoint target)
    {
        var eye = mascot.PointToScreen(new Point(68, 53));
        int left = Math.Min(eye.X, target.X) - 8, top = Math.Min(eye.Y, target.Y) - 8;
        double scale = droid.Screens.ScreenFromPoint(new PixelPoint(left, top))?.Scaling ?? droid.RenderScaling;
        laser.Position = new PixelPoint(left, top);
        laser.Width = (Math.Abs(eye.X - target.X) + 16) / scale;
        laser.Height = (Math.Abs(eye.Y - target.Y) + 16) / scale;
        beam.From = new Point((eye.X - left) / scale, (eye.Y - top) / scale);
        beam.To = new Point((target.X - left) / scale, (target.Y - top) / scale);
        if (!laser.IsVisible)
        {
            laser.Show();
            if (laser.TryGetPlatformHandle() is { HandleDescriptor: "NSWindow" } handle)
                MacDesktop.MakeClickThrough(handle.Handle);
        }
        beam.InvalidateVisual();
    }

    private async Task MoveChecked()
    {
        if (busy || settings.AutoMove) return;
        busy = true;
        try
        {
            var selected = matches.Where(item => item.Selection.IsChecked == true).Select(item => item.File).ToArray();
            var moved = await Task.Run(() => selected.Select(Sorting.Move).ToArray());
            matches.Clear();
            results.Children.Clear();
            foreach (var item in moved)
                results.Children.Add(new TextBlock { Text = item.Name + " · " + item.Outcome, TextWrapping = TextWrapping.Wrap });
            status.Text = $"Moved {moved.Count(item => item.Moved)} of {moved.Length} checked files.";
        }
        finally { busy = false; }
    }

    private void Play(bool enabled)
    {
        if (!enabled || !OperatingSystem.IsMacOS()) return;
        try
        {
            if (soundPath is null)
            {
                string path = Path.Combine(Path.GetTempPath(), $"neo-nibbler-{Guid.NewGuid():N}.wav");
                using var input = Resource("Chirp.wav");
                using var output = File.Create(path);
                input.CopyTo(output);
                soundPath = path;
            }
            var info = new ProcessStartInfo("/usr/bin/afplay") { UseShellExecute = false };
            info.ArgumentList.Add(soundPath);
            using var process = Process.Start(info);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { status.Text = ex.Message; }
    }
}


sealed class Beam : Control
{
    private static readonly Pen Glow = new(Brush.Parse("#FF3388"), 4);
    private static readonly Pen Core = new(Brush.Parse("#FFD5ED"), 1);
    public Point From { get; set; }
    public Point To { get; set; }

    public override void Render(DrawingContext context)
    {
        context.DrawLine(Glow, From, To);
        context.DrawLine(Core, From, To);
        context.DrawEllipse(null, Glow, To, 5, 5);
    }
}
