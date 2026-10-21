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
        Text = "AnnounceMe — напоминания о сайтах";
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
        };
        _list.Columns.Add("Сайт", 200);
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
        buttons.Controls.AddRange(new Control[] { add, _edit, _delete, _test });

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

        Controls.Add(_list);
        Controls.Add(buttons);
        Controls.Add(bottom);

        RefreshList();
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

            var item = new ListViewItem(string.IsNullOrWhiteSpace(s.Name) ? s.Host : s.Name, imageIndex) { Tag = s };
            item.SubItems.Add(s.ScheduleText());
            item.SubItems.Add(next);
            item.SubItems.Add(string.IsNullOrWhiteSpace(s.Title) ? s.Text : $"{s.Title}. {s.Text}");
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
        Notifier.Unregister(s.Id);
        if (s.IconFile.StartsWith(Store.IconsDir)) try { File.Delete(s.IconFile); } catch { }
        RefreshList();
    }
}
