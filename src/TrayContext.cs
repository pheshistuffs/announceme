using Microsoft.Win32;

namespace AnnounceMe;

public class TrayContext : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;
    readonly ToolStripMenuItem _pauseItem, _quickItem;
    readonly Control _invoker = new();
    readonly GlobalHotkey _hotkey = new();
    Form _main;
    bool _classicUi;
    QuickForm _quick;
    bool _checking;

    public static Icon AppIcon { get; } = LoadAppIcon();

    static Icon LoadAppIcon()
    {
        using var s = typeof(TrayContext).Assembly.GetManifestResourceStream("app.ico");
        return s != null ? new Icon(s) : SystemIcons.Information;
    }

    public TrayContext(bool startHidden)
    {
        _invoker.CreateControl();

        _pauseItem = new ToolStripMenuItem("Пауза уведомлений о сайтах", null, (_, _) => TogglePause())
        {
            Checked = Store.Data.Paused,
        };
        _quickItem = new ToolStripMenuItem("Быстрое напоминание…", null, (_, _) => ShowQuick());
        var menu = new ContextMenuStrip();
        menu.Items.Add(_quickItem);
        menu.Items.Add("Открыть список", null, (_, _) => ShowMain());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "AnnounceMe — напоминания",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

        _hotkey.Pressed += ShowQuick;
        ApplyHotkey();

        _timer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;

        Autostart.Refresh();
        foreach (var site in Store.Data.Sites.ToList()) Notifier.Prepare(site);
        if (!startHidden) ShowMain();
        _ = CheckAsync();
    }

    // Registers the hotkey from settings. Returns false when another program holds it.
    public bool ApplyHotkey()
    {
        var keys = GlobalHotkey.Parse(Store.Data.QuickHotkey);
        bool ok = _hotkey.Set(keys);
        _quickItem.ShortcutKeyDisplayString = ok && keys != Keys.None ? GlobalHotkey.Format(keys) : null;
        return ok;
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _invoker.BeginInvoke(async () => await CheckAsync());
    }

    void OnTimeChanged(object sender, EventArgs e) => _invoker.BeginInvoke(async () => await CheckAsync());

    async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        try
        {
            var dueReminders = Store.Data.Reminders.Where(r => r.AtUtc <= DateTime.UtcNow).ToList();
            if (dueReminders.Count > 0)
            {
                foreach (var r in dueReminders) Store.Data.Reminders.Remove(r);
                Store.Save();
                foreach (var r in dueReminders) ShowReminder(r);
                RefreshMain();
            }

            if (Store.Data.Paused) return;
            foreach (var e in Schedule.Due(Store.Data, DateTime.Now))
            {
                e.LastFiredUtc = DateTime.UtcNow;
                Store.Save();
                await ShowNotificationAsync(e);
            }
        }
        finally
        {
            _checking = false;
        }
    }

    void ShowReminder(Reminder r)
    {
        try
        {
            Notifier.ShowReminder(r);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(5000, r.Text, $"Не удалось показать уведомление: {ex.Message}", ToolTipIcon.Warning);
        }
    }

    public async Task ShowNotificationAsync(SiteEntry e)
    {
        try
        {
            await Notifier.ShowAsync(e);
        }
        catch (Exception ex)
        {
            _tray.ShowBalloonTip(5000, "Не удалось показать уведомление", $"{e.Name}: {ex.Message}", ToolTipIcon.Error);
        }
        RefreshMain();
    }

    public void AddReminder(DateTime atLocal, string text)
    {
        Store.Data.Reminders.Add(new Reminder { AtUtc = atLocal.ToUniversalTime(), Text = text });
        Store.Data.Reminders.Sort((a, b) => a.AtUtc.CompareTo(b.AtUtc));
        Store.Save();
        RefreshMain();
    }

    // Commands from a second launch of the exe or from notification buttons (announceme:...).
    public void InvokeCommand(string command) => _invoker.BeginInvoke(() => HandleCommand(command));

    void HandleCommand(string command)
    {
        if (!Uri.TryCreate(command, UriKind.Absolute, out var u) || u.Scheme != "announceme")
        {
            ShowMain();
            return;
        }
        var path = u.AbsolutePath.Trim('/').ToLowerInvariant();
        switch (path)
        {
            case "noop":
                break;
            case "quick":
                ShowQuick();
                break;
            case "open":
                var id = System.Web.HttpUtility.ParseQueryString(u.Query)["id"];
                var entry = Store.Data.Sites.FirstOrDefault(x => x.Id == id);
                if (entry != null) Notifier.Launch(entry);
                break;
            case "snooze":
                var q = System.Web.HttpUtility.ParseQueryString(u.Query);
                int min = int.TryParse(q["min"], out var m) && m > 0 ? m : 5;
                AddReminder(DateTime.Now.AddMinutes(min), q["text"] ?? "Напоминание");
                break;
            default:
                ShowMain();
                break;
        }
    }

    public void ShowQuick()
    {
        if (_quick == null || _quick.IsDisposed)
            _quick = new QuickForm(this);
        _quick.ShowAndFocus();
    }

    void RefreshMain()
    {
        if (_main is IMainView v && !_main.IsDisposed) v.RefreshAll();
    }

    static bool WebViewAvailable()
    {
        try
        {
            return !string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch
        {
            return false;
        }
    }

    // Used when the new window can't start: shows the plain one instead.
    public void FallbackToClassic()
    {
        _classicUi = true;
        var old = _main;
        _main = null;
        old?.Dispose();
        ShowMain();
    }

    public void ShowMain()
    {
        if (_main == null || _main.IsDisposed)
        {
            _main = !_classicUi && WebViewAvailable() ? new DashboardForm(this) : new MainForm(this);
            _main.FormClosing += (_, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                _main.Hide();
                if (!Store.Data.TrayHintShown)
                {
                    Store.Data.TrayHintShown = true;
                    Store.Save();
                    _tray.ShowBalloonTip(4000, "AnnounceMe работает в фоне",
                        "Уведомления будут приходить по расписанию. Значок — в области уведомлений рядом с часами.",
                        ToolTipIcon.Info);
                }
            };
        }
        _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
    }

    void TogglePause() => SetPaused(!Store.Data.Paused);

    public void SetPaused(bool paused)
    {
        if (paused == Store.Data.Paused) return;
        Store.Data.Paused = paused;
        _pauseItem.Checked = Store.Data.Paused;
        if (!Store.Data.Paused)
        {
            // Don't flood with everything that came due during the pause.
            foreach (var s in Store.Data.Sites) s.LastFiredUtc = DateTime.UtcNow;
        }
        Store.Save();
        RefreshMain();
    }

    void Exit()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        _timer.Stop();
        _hotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _main?.Dispose();
        _quick?.Dispose();
        ExitThread();
    }
}
