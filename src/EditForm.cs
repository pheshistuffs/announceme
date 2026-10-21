namespace AnnounceMe;

public class EditForm : Form
{
    public SiteEntry Entry { get; }

    readonly TrayContext _ctx;
    readonly TextBox _url, _name, _title, _text, _times;
    readonly PictureBox _icon;
    readonly Label _iconStatus;
    readonly CheckBox[] _days;
    readonly CheckBox _enabled;
    Bitmap _pendingIcon;

    public EditForm(SiteEntry entry, bool isNew, TrayContext ctx)
    {
        Entry = entry;
        _ctx = ctx;

        Text = isNew ? "Новое напоминание" : "Напоминание";
        Icon = TrayContext.AppIcon;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 440));

        _url = new TextBox
        {
            Text = entry.Url,
            Dock = DockStyle.Fill,
            PlaceholderText = "сайт, steam://… или программа; можно оставить пустым",
        };
        _url.Leave += (_, _) => OnUrlLeave();
        var pickProgram = new Button { Text = "Программа…", AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
        pickProgram.Click += (_, _) => PickProgram();
        var urlRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        urlRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        urlRow.Controls.Add(_url, 0, 0);
        urlRow.Controls.Add(pickProgram, 1, 0);
        _name = new TextBox { Text = entry.Name, Dock = DockStyle.Fill, PlaceholderText = "chess.com, Genshin Impact, Тренировка…" };
        _title = new TextBox { Text = entry.Title, Dock = DockStyle.Fill, PlaceholderText = "необязательно" };
        _text = new TextBox
        {
            Text = entry.Text,
            Dock = DockStyle.Fill,
            Multiline = true,
            Height = 64,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Обновились ежедневные задачи по шахматам",
        };

        _icon = new PictureBox
        {
            Size = new Size(48, 48),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
        };
        if (File.Exists(entry.IconFile))
        {
            using var src = Image.FromFile(entry.IconFile);
            _icon.Image = new Bitmap(src);
        }
        var fetch = new Button { Text = "Загрузить с сайта", AutoSize = true };
        fetch.Click += async (_, _) => await FetchIconAsync();
        var pick = new Button { Text = "Выбрать файл…", AutoSize = true };
        pick.Click += (_, _) => PickIcon();
        _iconStatus = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Left };
        var iconRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        iconRow.Controls.AddRange(new Control[] { _icon, fetch, pick, _iconStatus });

        _days = Schedule.WeekOrder.Select(d => new CheckBox
        {
            Text = Schedule.ShortName(d),
            Tag = d,
            AutoSize = true,
            Checked = entry.Days.Contains(d),
        }).ToArray();
        var everyDay = new LinkLabel { Text = "каждый день", AutoSize = true, Margin = new Padding(8, 5, 0, 0) };
        everyDay.LinkClicked += (_, _) => { foreach (var c in _days) c.Checked = true; };
        var weekdays = new LinkLabel { Text = "будни", AutoSize = true, Margin = new Padding(8, 5, 0, 0) };
        weekdays.LinkClicked += (_, _) =>
        {
            foreach (var c in _days)
                c.Checked = (DayOfWeek)c.Tag is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        };
        var daysRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        daysRow.Controls.AddRange(_days);
        daysRow.Controls.Add(everyDay);
        daysRow.Controls.Add(weekdays);

        _times = new TextBox
        {
            Text = string.Join(", ", entry.Times),
            Dock = DockStyle.Fill,
            PlaceholderText = "09:00, 18:30",
        };
        _enabled = new CheckBox { Text = "Включено", AutoSize = true, Checked = entry.Enabled };

        AddRow(grid, "Ссылка:", urlRow);
        AddRow(grid, "Название:", _name);
        AddRow(grid, "Заголовок:", _title);
        AddRow(grid, "Текст:", _text);
        AddRow(grid, "Иконка:", iconRow);
        AddRow(grid, "Дни:", daysRow);
        AddRow(grid, "Время:", _times);
        AddRow(grid, "", new Label
        {
            Text = "Можно несколько через запятую, например: 09:00, 18:30",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        });
        AddRow(grid, "", _enabled);

        var preview = new Button { Text = "Показать пример", AutoSize = true };
        preview.Click += async (_, _) => await PreviewAsync();
        var ok = new Button { Text = "Сохранить", AutoSize = true };
        ok.Click += (_, _) => Save();
        var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 0),
        };
        buttons.Controls.AddRange(new Control[] { cancel, ok, preview });
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 2);
        grid.RowCount++;

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;

        if (isNew) Shown += (_, _) => _url.Focus();
    }

    static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        var row = grid.RowCount;
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = new Padding(3, 7, 8, 3),
        }, 0, row);
        grid.Controls.Add(control, 1, row);
        grid.RowCount++;
    }

    // Web addresses get https:// added; links with a scheme and paths on disk stay as they are.
    static string NormalizeUrl(string s)
    {
        s = s.Trim().Trim('"');
        if (s.Length == 0) return s;
        if (s.Contains("://") || IsPath(s)) return s;
        return "https://" + s;
    }

    static bool IsPath(string s) => s.Length > 2 && (s[1] == ':' || s.StartsWith(@"\\"));

    static bool IsWeb(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");

    async void OnUrlLeave()
    {
        var url = NormalizeUrl(_url.Text);
        if (url.Length == 0) return;
        _url.Text = url;
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            if (IsWeb(url))
            {
                var host = new Uri(url).Host;
                _name.Text = host.StartsWith("www.") ? host[4..] : host;
            }
            else if (IsPath(url))
                _name.Text = Path.GetFileNameWithoutExtension(url);
        }
        if (_icon.Image == null && (IsWeb(url) || IsPath(url) || !string.IsNullOrEmpty(Entry.IconSite)))
            await FetchIconAsync();
    }

    void PickProgram()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Программа или игра",
            Filter = "Программы и ярлыки|*.exe;*.lnk;*.url|Все файлы|*.*",
            DereferenceLinks = false,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _url.Text = dlg.FileName;
        _name.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        var bmp = FaviconFetcher.FromFile(dlg.FileName);
        if (bmp != null)
        {
            SetPendingIcon(bmp);
            _iconStatus.Text = "из программы";
        }
    }

    async Task FetchIconAsync()
    {
        var url = NormalizeUrl(_url.Text);
        if (url.Length == 0 && string.IsNullOrEmpty(Entry.IconSite))
        {
            _iconStatus.Text = "нет ссылки, выберите файл";
            return;
        }
        _iconStatus.Text = "загружаю…";
        var bmp = await FaviconFetcher.FetchForAsync(url, Entry.IconSite);
        if (IsDisposed) { bmp?.Dispose(); return; }
        if (bmp == null)
        {
            _iconStatus.Text = "не нашлась, выберите файл";
            return;
        }
        SetPendingIcon(bmp);
        _iconStatus.Text = $"{bmp.Width}×{bmp.Height}";
    }

    void PickIcon()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Иконка для уведомления",
            Filter = "Изображения|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var bmp = FaviconFetcher.Decode(File.ReadAllBytes(dlg.FileName));
        if (bmp == null)
        {
            MessageBox.Show(this, "Не удалось прочитать картинку.", "Иконка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SetPendingIcon(bmp);
        _iconStatus.Text = $"{bmp.Width}×{bmp.Height}";
    }

    void SetPendingIcon(Bitmap bmp)
    {
        _pendingIcon?.Dispose();
        _pendingIcon = bmp;
        var old = _icon.Image;
        _icon.Image = new Bitmap(bmp);
        old?.Dispose();
    }

    // Copies the fields into Entry. Returns an error message, or null when valid.
    string Apply()
    {
        var url = NormalizeUrl(_url.Text);
        if (url.Length > 0 && !IsPath(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
            return "Не понимаю ссылку. Пример: https://www.chess.com/ или steam://rungameid/730";
        var name = _name.Text.Trim();
        if (name.Length == 0 && IsWeb(url))
        {
            var host = new Uri(url).Host;
            name = host.StartsWith("www.") ? host[4..] : host;
        }
        if (name.Length == 0 && IsPath(url)) name = Path.GetFileNameWithoutExtension(url);
        if (name.Length == 0) return "Укажите название, например «Тренировка» или «Genshin Impact»";

        var times = _times.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        var parsed = new List<string>();
        foreach (var t in times)
        {
            if (!TimeSpan.TryParse(t.Replace('.', ':'), out var ts) || ts < TimeSpan.Zero || ts >= TimeSpan.FromDays(1))
                return $"Не понимаю время «{t}». Пишите как 09:00";
            parsed.Add(ts.ToString(@"hh\:mm"));
        }
        if (parsed.Count == 0) return "Укажите хотя бы одно время, например 09:00";

        var days = _days.Where(c => c.Checked).Select(c => (DayOfWeek)c.Tag).ToList();
        if (days.Count == 0) return "Отметьте хотя бы один день";


        bool scheduleChanged = !Entry.Times.SequenceEqual(parsed) || !Entry.Days.OrderBy(d => d).SequenceEqual(days.OrderBy(d => d));

        Entry.Url = url;
        Entry.Title = _title.Text.Trim();
        Entry.Text = _text.Text.Trim();
        Entry.Times = parsed.Distinct().ToList();
        Entry.Days = days;
        Entry.Enabled = _enabled.Checked;
        if (scheduleChanged) Entry.LastFiredUtc = DateTime.UtcNow;

        if (name != Entry.Name || _pendingIcon != null)
        {
            // New sender identity, so Windows doesn't show a cached name/icon.
            Entry.Rev++;
            Entry.Name = name;
            if (_pendingIcon != null)
            {
                FaviconFetcher.SaveIcon(Entry, _pendingIcon);
                _pendingIcon.Dispose();
                _pendingIcon = null;
            }
        }
        _name.Text = name;
        return null;
    }

    async Task PreviewAsync()
    {
        var err = Apply();
        if (err != null)
        {
            MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        await _ctx.ShowNotificationAsync(Entry);
    }

    void Save()
    {
        var err = Apply();
        if (err != null)
        {
            MessageBox.Show(this, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pendingIcon?.Dispose();
            _icon.Image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
