namespace AnnounceMe;

public class MainForm : Form
{
    readonly TrayContext _ctx;
    readonly ListView _list;
    readonly ImageList _icons;
    readonly Button _edit, _delete, _test;

    public MainForm(TrayContext ctx)
    {
        _ctx = ctx;
        Text = "AnnounceMe — напоминания";
        Icon = TrayContext.AppIcon;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 480);
        MinimumSize = new Size(640, 360);
        AutoScaleMode = AutoScaleMode.Dpi;

        _icons = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            SmallImageList = _icons,
            ShowItemToolTips = true,
        };
        _list.Columns.Add("Название", 200);
        _list.Columns.Add("Когда", 200);
        _list.Columns.Add("Следующее", 130);
        _list.Columns.Add("Текст уведомления", 300);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => EditSelected();

        var add = new Button { Text = "Добавить…", AutoSize = true };
        add.Click += (_, _) => Add();
        _edit = new Button { Text = "Изменить…", AutoSize = true };
        _edit.Click += (_, _) => EditSelected();
        _delete = new Button { Text = "Удалить", AutoSize = true };
        _delete.Click += (_, _) => DeleteSelected();
        _test = new Button { Text = "Показать сейчас", AutoSize = true };
        _test.Click += async (_, _) => { if (Selected is { } s) await _ctx.ShowNotificationAsync(s); };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(6),
        };
        var templates = new Button { Text = "Игры и шаблоны…", AutoSize = true };
        templates.Click += (_, _) => ShowTemplates();
        buttons.Controls.AddRange(new Control[] { add, templates, _edit, _delete, _test });

        var autostart = new CheckBox { Text = "Запускать вместе с Windows", AutoSize = true, Checked = Autostart.Enabled };
        autostart.CheckedChanged += (_, _) => Autostart.Enabled = autostart.Checked;
        var catchUp = new CheckBox
        {
            Text = "Показывать пропущенные (если компьютер был выключен, до 12 ч)",
            AutoSize = true,
            Checked = Store.Data.CatchUpMissed,
        };
        catchUp.CheckedChanged += (_, _) => { Store.Data.CatchUpMissed = catchUp.Checked; Store.Save(); };

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.TopDown,
        };
        bottom.Controls.AddRange(new Control[] { autostart, catchUp });

        var sitesPage = new TabPage("Ежедневные");
        sitesPage.Controls.Add(_list);
        sitesPage.Controls.Add(buttons);

        _reminders = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
        };
        _reminders.Columns.Add("Когда", 220);
        _reminders.Columns.Add("Текст", 520);
        _reminders.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) DeleteReminders(); };
        var newReminder = new Button { Text = "Новое…", AutoSize = true };
        newReminder.Click += (_, _) => _ctx.ShowQuick();
        var deleteReminder = new Button { Text = "Удалить", AutoSize = true };
        deleteReminder.Click += (_, _) => DeleteReminders();
        var reminderButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        reminderButtons.Controls.AddRange(new Control[] { newReminder, deleteReminder });
        var remindersPage = new TabPage("Быстрые напоминания");
        remindersPage.Controls.Add(_reminders);
        remindersPage.Controls.Add(reminderButtons);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(sitesPage);
        tabs.TabPages.Add(remindersPage);

        bottom.Controls.Add(BuildHotkeyRow());

        Controls.Add(tabs);
        Controls.Add(bottom);

        RefreshList();
        RefreshReminders();
        _clock.Tick += (_, _) => RefreshReminders();
        _clock.Start();
    }

    readonly ListView _reminders;
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 30_000 };

    Control BuildHotkeyRow()
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var box = new TextBox
        {
            Width = 160,
            ReadOnly = true,
            Text = Store.Data.QuickHotkey,
            PlaceholderText = "выключена",
            BackColor = SystemColors.Window,
        };
        var status = new Label { AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
        void Apply(string value)
        {
            Store.Data.QuickHotkey = value;
            Store.Save();
            box.Text = value;
            bool ok = _ctx.ApplyHotkey();
            status.ForeColor = ok ? SystemColors.GrayText : Color.Firebrick;
            status.Text = ok ? (value.Length > 0 ? "работает" : "") : "занята другой программой, выберите другую";
        }
        box.KeyDown += (_, e) =>
        {
            e.Handled = e.SuppressKeyPress = true;
            var key = e.KeyCode;
            if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            if (key is Keys.Back or Keys.Delete && e.Modifiers == Keys.None) { Apply(""); return; }
            if ((e.Modifiers & (Keys.Control | Keys.Alt)) == 0) return; // needs Ctrl or Alt
            Apply(GlobalHotkey.Format(e.Modifiers | key));
        };
        var reset = new LinkLabel { Text = "по умолчанию", AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
        reset.LinkClicked += (_, _) => Apply("Ctrl+Alt+N");
        row.Controls.Add(new Label
        {
            Text = "Горячая клавиша быстрого напоминания (нажмите сочетание в поле):",
            AutoSize = true,
            Margin = new Padding(3, 6, 6, 0),
        });
        row.Controls.AddRange(new Control[] { box, reset, status });
        Apply(Store.Data.QuickHotkey);
        return row;
    }

    public void RefreshReminders()
    {
        if (IsDisposed) return;
        var now = DateTime.Now;
        _reminders.BeginUpdate();
        _reminders.Items.Clear();
        foreach (var r in Store.Data.Reminders)
        {
            var at = r.AtUtc.ToLocalTime();
            var item = new ListViewItem(at > now ? QuickParser.Describe(at, now) : "сейчас") { Tag = r };
            item.SubItems.Add(r.Text);
            _reminders.Items.Add(item);
        }
        _reminders.EndUpdate();
    }

    void DeleteReminders()
    {
        var selected = _reminders.SelectedItems.Cast<ListViewItem>().Select(i => (Reminder)i.Tag).ToList();
        if (selected.Count == 0) return;
        foreach (var r in selected) Store.Data.Reminders.Remove(r);
        Store.Save();
        RefreshReminders();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _clock.Dispose();
        base.Dispose(disposing);
    }

    SiteEntry Selected => _list.SelectedItems.Count > 0 ? (SiteEntry)_list.SelectedItems[0].Tag : null;

    void UpdateButtons()
    {
        bool has = Selected != null;
        _edit.Enabled = _delete.Enabled = _test.Enabled = has;
    }

    public void RefreshList()
    {
        if (IsDisposed) return;
        var selectedId = Selected?.Id;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var img in _icons.Images.Cast<Image>().ToList()) img.Dispose();
        _icons.Images.Clear();

        var now = DateTime.Now;
        foreach (var s in Store.Data.Sites)
        {
            int imageIndex = -1;
            if (File.Exists(s.IconFile))
            {
                try
                {
                    using var src = Image.FromFile(s.IconFile);
                    _icons.Images.Add(FaviconFetcher.ToSquare(src, 32));
                    imageIndex = _icons.Images.Count - 1;
                }
                catch { }
            }
            string next;
            if (Store.Data.Paused) next = "на паузе";
            else if (!s.Enabled) next = "выключено";
            else next = Schedule.NextOccurrence(s, now) is { } n ? FormatNext(n, now) : "—";

            var item = new ListViewItem(s.DisplayName, imageIndex) { Tag = s };
            item.SubItems.Add(s.ScheduleText());
            item.SubItems.Add(next);
            var fullText = string.IsNullOrWhiteSpace(s.Title) ? s.Text : $"{s.Title}. {s.Text}";
            item.SubItems.Add(fullText);
            item.ToolTipText = fullText;
            if (!s.Enabled) item.ForeColor = SystemColors.GrayText;
            _list.Items.Add(item);
            if (s.Id == selectedId) item.Selected = true;
        }
        _list.EndUpdate();
        UpdateButtons();
    }

    static string FormatNext(DateTime n, DateTime now)
    {
        if (n.Date == now.Date) return $"сегодня {n:HH:mm}";
        if (n.Date == now.Date.AddDays(1)) return $"завтра {n:HH:mm}";
        return $"{Schedule.ShortName(n.DayOfWeek)} {n:dd.MM HH:mm}";
    }

    void Add()
    {
        using var dlg = new EditForm(new SiteEntry { Text = "" }, isNew: true, _ctx);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        Store.Data.Sites.Add(dlg.Entry);
        Store.Save();
        Notifier.Prepare(dlg.Entry);
        RefreshList();
    }

    void EditSelected()
    {
        var s = Selected;
        if (s == null) return;
        using var dlg = new EditForm(s.Clone(), isNew: false, _ctx);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var i = Store.Data.Sites.IndexOf(s);
        Store.Data.Sites[i] = dlg.Entry;
        Store.Save();
        Notifier.Prepare(dlg.Entry);
        RefreshList();
    }

    void DeleteSelected()
    {
        var s = Selected;
        if (s == null) return;
        if (MessageBox.Show(this, $"Удалить напоминание «{s.Name}»?", "Удаление",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Store.Data.Sites.Remove(s);
        Store.Save();
        CleanUp(s);
        RefreshList();
    }

    static void CleanUp(SiteEntry s)
    {
        Notifier.Remove(s);
        if (s.IconFile.StartsWith(Store.IconsDir))
        {
            try { File.Delete(s.IconFile); } catch { }
            try { File.Delete(Path.ChangeExtension(s.IconFile, ".ico")); } catch { }
        }
    }

    async void ShowTemplates()
    {
        List<SiteEntry> added;
        using (var dlg = new TemplatesForm())
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            var (a, removed) = dlg.Apply();
            added = a;
            foreach (var r in removed) CleanUp(r);
        }
        RefreshList();
        // Icons come from the games' sites; fill them in as they arrive.
        foreach (var e in added)
        {
            try { await FaviconFetcher.EnsureIconAsync(e); } catch { }
            Notifier.Prepare(e);
            RefreshList();
        }
    }
}
