using System.Diagnostics;

namespace AstromechDesk;

sealed class ControlPanel : Form
{
    private static readonly Color Background = Color.FromArgb(21, 12, 43);
    private static readonly Color Surface = Color.FromArgb(42, 23, 75);
    private static readonly Color Foreground = Color.FromArgb(249, 231, 255);
    private static readonly Color Muted = Color.FromArgb(209, 168, 239);
    private static readonly Color Accent = Color.FromArgb(88, 248, 224);
    private readonly DroidOverlay droid = new();
    private readonly List<Rule> rules = [];
    private readonly CheckBox fileTooltips = new() { Text = "Filename tooltips", Checked = true, AutoSize = true };
    private readonly CheckBox autoMove = new() { Text = "Auto-move matches", AutoSize = true };
    private readonly CheckBox watch = new() { Text = "Watch Desktop", AutoSize = true };
    private readonly CheckBox chaseFiles = new() { Text = "Chase matching files", Checked = true, AutoSize = true };
    private readonly CheckBox chaseMouse = new() { Text = "Random mouse chases", Checked = true, AutoSize = true };
    private readonly ComboBox chaseLength = new() { Width = 108, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ScanOverlay laser = new();
    private readonly System.Windows.Forms.Timer watcher = new() { Interval = 3500 };
    private readonly HashSet<Candidate> observed = [];
    private ApplicationContext? session;
    private NotifyIcon? tray;
    private Icon? appIcon;
    private ContextMenuStrip? trayMenu;
    private ToolStripMenuItem? trayScan;
    private bool exiting;
    private Candidate[] candidates = [];
    private readonly TextBox extension = new() { Width = 96, PlaceholderText = ".pdf", MaxLength = 32 };
    private readonly TextBox destination = new() { Width = 370, ReadOnly = true, PlaceholderText = "Destination folder" };
    private readonly DataGridView ruleGrid = MakeGrid();
    private readonly DataGridView fileGrid = MakeGrid();
    private readonly CheckBox sound = new() { Text = "Droid sounds", Checked = true, AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button scan = MakeButton("Scan Desktop", true);
    private readonly Button move = MakeButton("Move checked");
    private readonly Button saveRule = MakeButton("Save rule");
    private readonly Button remove = MakeButton("Remove rule");
    private readonly Button browse = MakeButton("Browse…");
    private bool busy;

    public ControlPanel()
    {
        Text = "Neo Nibbler";
        BackColor = Background;
        ForeColor = Foreground;
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(800, 625);
        MinimumSize = new Size(760, 575);
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 22, 10), ColumnCount = 1, RowCount = 10 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(layout);

        var title = new Label { Text = "Neo Nibbler", Font = new Font("Segoe UI", 25, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(Heading("Extension rules"), 0, 1);
        foreach (TextBox input in new[] { extension, destination })
        {
            input.BackColor = Surface;
            input.ForeColor = Foreground;
            input.BorderStyle = BorderStyle.FixedSingle;
            input.Margin = new Padding(0, 7, 7, 0);
        }
        layout.Controls.Add(Row(extension, destination, browse, saveRule), 0, 2);

        ruleGrid.ReadOnly = true;
        ruleGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Extension", Width = 110 });
        ruleGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Destination", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        layout.Controls.Add(ruleGrid, 0, 3);
        layout.Controls.Add(Row(remove, Heading("Scan results")), 0, 4);

        fileGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Move", Width = 60 });
        fileGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "File", FillWeight = 40, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        fileGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Destination / result", FillWeight = 60, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (DataGridViewColumn column in fileGrid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        foreach (DataGridViewColumn column in ruleGrid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        layout.Controls.Add(fileGrid, 0, 5);
        var overlayToggle = MakeButton("Hide droid");
        layout.Controls.Add(Row(scan, move, overlayToggle, sound), 0, 6);
        sound.Margin = new Padding(13, 12, 0, 0);
        layout.Controls.Add(status, 0, 7);
        layout.Controls.Add(Row(fileTooltips, autoMove, watch), 0, 8);
        foreach (var option in new[] { fileTooltips, autoMove, watch })
            option.Margin = new Padding(0, 8, 14, 0);

        chaseLength.Items.AddRange(["5 seconds", "10 seconds"]);
        chaseLength.SelectedIndex = 0;
        chaseLength.BackColor = Surface;
        chaseLength.ForeColor = Foreground;
        chaseLength.FlatStyle = FlatStyle.Flat;
        chaseLength.Margin = new Padding(0, 4, 0, 0);
        foreach (var option in new[] { chaseFiles, chaseMouse })
            option.Margin = new Padding(0, 7, 14, 0);
        layout.Controls.Add(Row(chaseFiles, chaseMouse, Heading("Duration"), chaseLength), 0, 9);
        VisibleChanged += (_, _) => droid.SettingsVisible = Visible;

        status.Text = "Add an extension rule, then scan your Desktop.";
        try
        {
            Settings settings = Sorting.Load();
            rules.AddRange(settings.Rules);
            sound.Checked = settings.Sound;
            fileTooltips.Checked = settings.FileTooltips;
            autoMove.Checked = settings.AutoMove;
            watch.Checked = settings.WatchDesktop;
            chaseFiles.Checked = settings.ChaseFiles;
            chaseMouse.Checked = settings.ChaseMouse;
            chaseLength.SelectedIndex = settings.MouseChaseSeconds == 10 ? 1 : 0;
        }
        catch (Exception ex) { status.Text = "Could not load settings. " + ex.Message; }

        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Choose where matching files should go" };
            if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
        };
        saveRule.Click += (_, _) => SaveRule();
        extension.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            SaveRule();
        };
        remove.Click += (_, _) =>
        {
            int index = ruleGrid.CurrentCell?.RowIndex ?? -1;
            if (index >= 0) ChangeRules(rules.Where((_, position) => position != index).ToArray());
        };
        ruleGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            extension.Text = rules[e.RowIndex].Extension;
            destination.Text = rules[e.RowIndex].Folder;
        };
        void SavePreferences()
        {
            ApplyChasePreferences();
            try
            {
                Sorting.Save(CurrentSettings(rules.ToArray()));
                observed.Clear();
                watcher.Enabled = watch.Checked;
            }
            catch (Exception ex) { status.Text = "Could not save settings. " + ex.Message; }
        }

        foreach (var option in new[] { sound, fileTooltips, autoMove, watch, chaseFiles, chaseMouse })
            option.CheckedChanged += (_, _) => SavePreferences();
        chaseLength.SelectedIndexChanged += (_, _) => SavePreferences();
        ApplyChasePreferences();
        watcher.Tick += async (_, _) => await ScanAsync(background: true);
        scan.Click += async (_, _) => await ScanAsync();
        move.Click += async (_, _) => await MoveAsync();
        overlayToggle.Click += (_, _) =>
        {
            if (droid.Visible) droid.Hide();
            else ShowDroid();
            overlayToggle.Text = droid.Visible ? "Hide droid" : "Show droid";
        };
        droid.OpenControls += ShowSettings;
        droid.ScanRequested += async () => await ScanAsync();
        FormClosing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true;
            Hide();
        };
        RefreshRules();
        UpdateButtons();
    }

    public ApplicationContext StartInTray()
    {
        session = new ApplicationContext();
        using var iconStream = typeof(ControlPanel).Assembly.GetManifestResourceStream("AstromechDesk.Astromech.ico");
        appIcon = new Icon(iconStream ?? throw new InvalidOperationException("Missing application icon."));
        Icon = appIcon;
        trayMenu = new ContextMenuStrip();
        trayScan = new ToolStripMenuItem("Scan Desktop");
        trayScan.Click += async (_, _) => await ScanAsync();
        trayMenu.Items.Add(trayScan);
        trayMenu.Items.Add("Settings", null, (_, _) => ShowSettings());
        trayMenu.Items.Add("About me", null, (_, _) => MessageBox.Show(
            "Fahad Snafee\n\nI’m an ICT professional based in Basra, Iraq. I manage IT projects and support networks, systems, and cloud services, including Microsoft 365. Neo Nibbler brings a little personality to everyday file sorting.\n\nWebsite: basrasite.co\nInstagram: @fahadsnafee",
            "About me", MessageBoxButtons.OK, MessageBoxIcon.Information));
        trayMenu.Items.Add("Website · basrasite.co", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://basrasite.co") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open the website"); }
        });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) =>
        {
            if (busy) return;
            exiting = true;
            watcher.Stop();
            if (tray is not null) tray.Visible = false;
            session?.ExitThread();
        });
        trayMenu.Opening += (_, _) => trayScan.Enabled = !busy;
        tray = new NotifyIcon { Icon = appIcon, Text = "Neo Nibbler", ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
        watcher.Enabled = watch.Checked;
        ShowDroid();
        return session;
    }

    private void ShowSettings()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private Settings CurrentSettings(Rule[] entries) =>
        new(entries, sound.Checked, fileTooltips.Checked, autoMove.Checked, watch.Checked,
            chaseFiles.Checked, chaseMouse.Checked, chaseLength.SelectedIndex == 1 ? 10 : 5);

    private void ApplyChasePreferences()
    {
        droid.MouseChaseEnabled = chaseMouse.Checked;
        droid.MouseChaseSeconds = chaseLength.SelectedIndex == 1 ? 10 : 5;
        chaseLength.Enabled = chaseMouse.Checked && !busy;
    }

    private void ShowDroid()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        droid.Location = new Point(area.Right - droid.Width - 16, area.Bottom - droid.Height - 16);
        droid.Show();
    }

    private void SaveRule()
    {
        try
        {
            Rule rule = Sorting.CreateRule(extension.Text, destination.Text);
            ChangeRules(rules.Where(r => !string.Equals(r.Extension, rule.Extension, StringComparison.OrdinalIgnoreCase))
                .Append(rule).OrderBy(r => r.Extension, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    private void ChangeRules(Rule[] updated)
    {
        try
        {
            Sorting.Save(CurrentSettings(updated));
            observed.Clear();
            rules.Clear();
            rules.AddRange(updated);
            RefreshRules();
            candidates = [];
            fileGrid.Rows.Clear();
            status.Text = "Rules saved. Scan your Desktop to refresh the preview.";
            UpdateButtons();
        }
        catch (Exception ex) { status.Text = "Could not save rules. " + ex.Message; }
    }

    private void RefreshRules()
    {
        ruleGrid.Rows.Clear();
        foreach (Rule rule in rules) ruleGrid.Rows.Add(rule.Extension, rule.Folder);
    }

    private async Task ScanAsync(bool background = false)
    {
        if (busy) return;
        if (rules.Count == 0)
        {
            status.Text = "Add an extension rule in Settings before scanning.";
            if (!background && !Visible) tray?.ShowBalloonTip(3000, "Neo Nibbler", status.Text, ToolTipIcon.Info);
            return;
        }

        SetBusy(true);
        var preferences = CurrentSettings(rules.ToArray());
        try
        {
            var (files, found) = await Task.Run(() =>
            {
                string desktop = Sorting.Desktop;
                string[] files = Directory.GetFiles(desktop);
                return (files, Sorting.ScanFiles(files, preferences.Rules));
            });

            observed.IntersectWith(found);
            var pending = background ? found.Where(item => !observed.Contains(item)).ToArray() : found;
            if (background && pending.Length == 0) return;

            status.Text = "Scanning your Desktop…";
            droid.BeginScan(preferences.Sound);
            if (droid.Visible)
                await laser.SweepAsync(() => droid.EyePosition, Screen.AllScreens);

            var positions = (preferences.FileTooltips || preferences.ChaseFiles) && droid.Visible && pending.Length > 0
                ? await Task.Run(() => DesktopIcons.Locate(files)) : null;
            var moved = new List<MoveResult>();
            foreach (var item in pending)
            {
                if (positions is not null && droid.Visible)
                {
                    bool located = positions.TryGetValue(item.Name, out var bounds);
                    Point eye = droid.EyePosition;
                    Rectangle area = Screen.FromPoint(eye).WorkingArea;
                    Point target = located ? new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)
                        : new Point(Math.Max(area.Left + 16, eye.X - 120), Math.Max(area.Top + 16, eye.Y - 90));
                    if (located && preferences.ChaseFiles)
                    {
                        laser.Hide();
                        await droid.ChaseFileAsync(target);
                    }
                    if (droid.Visible && (located || preferences.FileTooltips))
                    {
                        laser.Aim(droid.EyePosition, target, preferences.FileTooltips ? item.Name : null,
                            located ? bounds : null);
                        await Task.Delay(preferences.FileTooltips ? 850 : 180);
                    }
                }

                if (preferences.AutoMove)
                {
                    var result = await Task.Run(() => Sorting.Move(item));
                    moved.Add(result);
                    // A failed file can be retried with a manual scan or after it changes.
                }
                observed.Add(item);
            }

            if (preferences.AutoMove)
            {
                candidates = [];
                fileGrid.Rows.Clear();
                foreach (var result in moved)
                    fileGrid.Rows[fileGrid.Rows.Add(false, result.Name, result.Moved ? result.Outcome : "Skipped · " + result.Outcome)]
                        .Cells[0].ReadOnly = true;
                status.Text = $"Moved {moved.Count(result => result.Moved)} of {moved.Count} configured matches.";
            }
            else
            {
                candidates = found;
                fileGrid.Rows.Clear();
                foreach (Candidate item in candidates) fileGrid.Rows.Add(true, item.Name, item.Folder);
                status.Text = candidates.Length == 0 ? "No matches with a configured destination."
                    : $"{candidates.Length} matched. Uncheck any files you want to keep on the Desktop.";
            }
        }
        catch (Exception ex) { status.Text = "Scan failed. " + ex.Message; }
        finally
        {
            if (laser.Visible) laser.Hide();
            droid.EndScanAnimation();
            SetBusy(false);
        }
    }

    private async Task MoveAsync()
    {
        fileGrid.EndEdit();
        Candidate[] selected = candidates.Where((_, i) => fileGrid.Rows[i].Cells[0].Value is true).ToArray();
        if (selected.Length == 0)
        {
            status.Text = "Check at least one file to move.";
            return;
        }

        SetBusy(true);
        status.Text = "Moving checked files…";
        droid.BeginScan(sound.Checked);
        try
        {
            MoveResult[] results = await Task.Run(() => selected.Select(Sorting.Move).ToArray());
            candidates = [];
            fileGrid.Rows.Clear();
            foreach (MoveResult result in results)
                fileGrid.Rows[fileGrid.Rows.Add(false, result.Name, result.Moved ? result.Outcome : "Skipped · " + result.Outcome)]
                    .Cells[0].ReadOnly = true;
            int count = results.Count(result => result.Moved);
            status.Text = $"Moved {count} of {results.Length} checked files. Scan again to refresh.";
        }
        catch (Exception ex)
        {
            candidates = [];
            status.Text = "The move was interrupted. Scan again to refresh. " + ex.Message;
        }
        finally
        {
            droid.EndScan(sound.Checked);
            SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        busy = value;
        sound.Enabled = fileTooltips.Enabled = autoMove.Enabled = watch.Enabled = chaseFiles.Enabled =
            chaseMouse.Enabled = !value;
        chaseLength.Enabled = !value && chaseMouse.Checked;
        extension.Enabled = browse.Enabled = saveRule.Enabled = ruleGrid.Enabled = fileGrid.Enabled = !value;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        scan.Enabled = !busy;
        remove.Enabled = !busy && rules.Count > 0;
        move.Enabled = !busy && candidates.Length > 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            watcher.Dispose();
            tray?.Dispose();
            trayMenu?.Dispose();
            laser.Dispose();
            droid.Dispose();
            appIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Label Heading(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Accent, Margin = new Padding(8, 8, 0, 0)
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        row.Controls.AddRange(controls);
        return row;
    }

    private static Button MakeButton(string text, bool primary = false) => new()
    {
        Text = text, AutoSize = true, Height = 32, FlatStyle = FlatStyle.Flat,
        BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Foreground,
        Padding = new Padding(7, 0, 7, 0), Margin = new Padding(0, 4, 8, 0),
        FlatAppearance = { BorderColor = Color.FromArgb(236, 82, 171), MouseOverBackColor = Color.FromArgb(123, 72, 163) },
        UseVisualStyleBackColor = false
    };

    private static DataGridView MakeGrid() => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Surface, BorderStyle = BorderStyle.None,
        GridColor = Background, RowHeadersVisible = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, EnableHeadersVisualStyles = false,
        ColumnHeadersHeight = 30,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Background, ForeColor = Accent },
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Surface, ForeColor = Foreground,
            SelectionBackColor = Color.FromArgb(113, 40, 131), SelectionForeColor = Color.White
        }
    };
}
