using Microsoft.Win32;

namespace AnnounceMe;

public class TrayContext : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _timer;
    readonly ToolStripMenuItem _pauseItem;
    readonly Control _invoker = new();
    MainForm _main;
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

        _pauseItem = new ToolStripMenuItem("Пауза уведомлений", null, (_, _) => TogglePause())
        {
            Checked = Store.Data.Paused,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Открыть список сайтов", null, (_, _) => ShowMain());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => Exit());

        _tray = new NotifyIcon
        {
            Icon = AppIcon,
            Text = "AnnounceMe — напоминания о сайтах",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

        _timer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _timer.Tick += async (_, _) => await CheckAsync();
        _timer.Start();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;

        Autostart.Refresh();
        if (!startHidden) ShowMain();
        _ = CheckAsync();
    }

    void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) _invoker.BeginInvoke(async () => await CheckAsync());
    }

    void OnTimeChanged(object sender, EventArgs e) => _invoker.BeginInvoke(async () => await CheckAsync());

    async Task CheckAsync()
    {
        if (_checking || Store.Data.Paused) return;
        _checking = true;
        try
        {
            var now = DateTime.Now;
            foreach (var e in Schedule.Due(Store.Data, now))
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
        _main?.RefreshList();
    }

    public void InvokeShowMain() => _invoker.BeginInvoke(ShowMain);

    void ShowMain()
    {
        if (_main == null || _main.IsDisposed)
        {
            _main = new MainForm(this);
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

    void TogglePause()
    {
        Store.Data.Paused = !Store.Data.Paused;
        _pauseItem.Checked = Store.Data.Paused;
        if (!Store.Data.Paused)
        {
            // Don't flood with everything that came due during the pause.
            foreach (var s in Store.Data.Sites) s.LastFiredUtc = DateTime.UtcNow;
        }
        Store.Save();
        _main?.RefreshList();
    }

    void Exit()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _main?.Dispose();
        ExitThread();
    }
}
