using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AnnounceMe;

// The main window drawn in HTML (ui.html) inside WebView2. The page asks for things with
// {id, cmd, args} messages; replies come back as {type: "reply", id, ok, data, error} and
// every change of data is pushed as {type: "state", data}.
public class DashboardForm : Form, IMainView
{
    const string UiHost = "app.announceme";
    const string IconsHost = "icons.announceme";

    readonly TrayContext _ctx;
    readonly WebView2 _web;
    readonly Dictionary<string, Bitmap> _pendingIcons = new();
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 30_000 };
    bool _ready;
    readonly bool _glass;

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public DashboardForm(TrayContext ctx)
    {
        _ctx = ctx;
        Text = "AnnounceMe";
        Icon = TrayContext.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1200, 780);
        MinimumSize = new Size(980, 660);
        // On Windows 11 the frame between the cards is see-through (acrylic): the window is
        // painted black, which DWM turns into glass once the frame is extended over it.
        _glass = Environment.OSVersion.Version.Build >= 22621;
        BackColor = _glass ? Color.Black : Color.FromArgb(22, 22, 22);

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = _glass ? Color.Transparent : BackColor };
        Controls.Add(_web);

        _clock.Tick += (_, _) => PushState();
        Load += async (_, _) => await InitAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark title bar, to match the cards.
        int on = 1;
        DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
        if (_glass)
        {
            int acrylic = 3; // DWMWA_SYSTEMBACKDROP_TYPE = DWMSBT_TRANSIENTWINDOW
            DwmSetWindowAttribute(Handle, 38, ref acrylic, sizeof(int));
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    struct Margins { public int Left, Right, Top, Bottom; }

    async Task InitAsync()
    {
        try
        {
            var uiDir = Path.Combine(Store.Dir, "ui");
            Directory.CreateDirectory(uiDir);
            foreach (var name in new[] { "ui.html", "app.png" })
            {
                using var src = typeof(DashboardForm).Assembly.GetManifestResourceStream(name);
                using var f = File.Create(Path.Combine(uiDir, name));
                src.CopyTo(f);
            }

            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Store.Dir, "webview"));
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(UiHost, uiDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(IconsHost, Store.IconsDir, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += async (_, e) => await OnMessageAsync(e.WebMessageAsJson);
            // Links in the page open in the browser, never inside the app.
            core.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
            core.NavigationCompleted += (_, _) => { _ready = true; PushState(); };
            core.Navigate($"https://{UiHost}/ui.html{(_glass ? "?glass=1" : "")}");
            _clock.Start();
        }
        catch
        {
            BeginInvoke(_ctx.FallbackToClassic);
        }
    }

    public void RefreshAll() => PushState();

    void PushState()
    {
        if (!_ready || IsDisposed) return;
        Post(new { type = "state", data = BuildState() });
    }

    void Post(object o) => _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(o, Json));

    object BuildState()
    {
        var now = DateTime.Now;
        var sites = Store.Data.Sites.Select(s =>
        {
            DateTime? next = s.Enabled && !Store.Data.Paused ? Schedule.NextOccurrence(s, now) : null;
            return new
            {
                id = s.Id,
                name = s.DisplayName,
                rawName = s.Name,
                title = s.Title,
                text = s.Text,
                url = s.Url,
                icon = IconUrl(s),
                enabled = s.Enabled,
                days = Schedule.WeekOrder.Where(s.Days.Contains).Select(d => (int)d).ToList(),
                times = s.Times,
                schedule = s.ScheduleText(),
                kind = KindOf(s),
                next = next?.ToString("o"),
                nextText = next is { } n ? WhenText(n, now) : null,
            };
        }).ToList();

        var reminders = Store.Data.Reminders.Select(r =>
        {
            var at = r.AtUtc.ToLocalTime();
            return new
            {
                id = r.Id,
                text = r.Text,
                at = at.ToString("o"),
                created = r.CreatedUtc.ToLocalTime().ToString("o"),
                whenText = WhenText(at, now),
                inText = QuickParser.Describe(at, now),
            };
        }).ToList();

        var templates = Templates.All.Select(t => new
        {
            id = t.Id,
            group = t.Group,
            name = t.Name,
            text = t.Text,
            when = Templates.Describe(t),
            on = Store.Data.Sites.Any(t.Matches),
        }).ToList();

        return new
        {
            now = now.ToString("o"),
            sites,
            reminders,
            templates,
            settings = new
            {
                paused = Store.Data.Paused,
                autostart = Autostart.Enabled,
                catchUp = Store.Data.CatchUpMissed,
                hotkey = Store.Data.QuickHotkey,
            },
        };
    }

    static string IconUrl(SiteEntry s)
    {
        if (!File.Exists(s.IconFile) || !s.IconFile.StartsWith(Store.IconsDir)) return null;
        return $"https://{IconsHost}/{Uri.EscapeDataString(Path.GetFileName(s.IconFile))}";
    }

    static string KindOf(SiteEntry s)
    {
        var t = Templates.All.FirstOrDefault(x => x.Matches(s));
        if (t != null) return t.Group == Templates.Habits ? (string.IsNullOrEmpty(s.Url) ? "custom" : "web") : "game";
        if (string.IsNullOrWhiteSpace(s.Url)) return "custom";
        return s.IsWebLink ? "web" : "game";
    }

    static string WhenText(DateTime at, DateTime now)
    {
        if (at.Date == now.Date) return $"сегодня {at:HH:mm}";
        if (at.Date == now.Date.AddDays(1)) return $"завтра {at:HH:mm}";
        return $"{Schedule.ShortName(at.DayOfWeek)} {at:dd.MM}, {at:HH:mm}";
    }

    async Task OnMessageAsync(string json)
    {
        JsonNode msg;
        try { msg = JsonNode.Parse(json); } catch { return; }
        var id = msg?["id"]?.GetValue<int>() ?? 0;
        var cmd = msg?["cmd"]?.GetValue<string>();
        var args = msg?["args"] as JsonObject ?? new JsonObject();
        try
        {
            var data = await HandleAsync(cmd, args);
            Post(new { type = "reply", id, ok = true, data });
        }
        catch (UserError ex)
        {
            Post(new { type = "reply", id, ok = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            Post(new { type = "reply", id, ok = false, error = $"Что-то пошло не так: {ex.Message}" });
        }
    }

    class UserError : Exception
    {
        public UserError(string m) : base(m) { }
    }

    static string Str(JsonObject a, string key) => a[key]?.GetValue<string>() ?? "";
    static bool Bool(JsonObject a, string key) => a[key]?.GetValue<bool>() ?? false;
    static int Int(JsonObject a, string key) => a[key]?.GetValue<int>() ?? 0;

    SiteEntry Find(JsonObject a) =>
        Store.Data.Sites.FirstOrDefault(s => s.Id == Str(a, "id")) ?? throw new UserError("Напоминание не найдено");

    async Task<object> HandleAsync(string cmd, JsonObject a)
    {
        switch (cmd)
        {
            case "getState":
                return BuildState();

            case "saveSite":
            {
                var existing = Store.Data.Sites.FirstOrDefault(s => s.Id == Str(a, "id"));
                var entry = existing?.Clone() ?? new SiteEntry();
                FillEntry(entry, a);
                if (existing != null) Store.Data.Sites[Store.Data.Sites.IndexOf(existing)] = entry;
                else Store.Data.Sites.Add(entry);
                Store.Save();
                Notifier.Prepare(entry);
                PushState();
                if (!File.Exists(entry.IconFile))
                {
                    try { await FaviconFetcher.EnsureIconAsync(entry); } catch { }
                    Notifier.Prepare(entry);
                    PushState();
                }
                return null;
            }

            case "previewSite":
            {
                var existing = Store.Data.Sites.FirstOrDefault(s => s.Id == Str(a, "id"));
                var entry = existing?.Clone() ?? new SiteEntry();
                FillEntry(entry, a, keepPendingIcon: true);
                await _ctx.ShowNotificationAsync(entry);
                return null;
            }

            case "deleteSite":
            {
                var s = Find(a);
                Store.Data.Sites.Remove(s);
                Store.Save();
                CleanUp(s);
                PushState();
                return null;
            }

            case "toggleSite":
            {
                var s = Find(a);
                s.Enabled = Bool(a, "enabled");
                if (s.Enabled) s.LastFiredUtc = DateTime.UtcNow;
                Store.Save();
                PushState();
                return null;
            }

            case "testSite":
                await _ctx.ShowNotificationAsync(Find(a));
                return null;

            case "openSite":
                Notifier.Launch(Find(a));
                return null;

            case "fetchIcon":
            {
                var url = NormalizeUrl(Str(a, "url"));
                var existing = Store.Data.Sites.FirstOrDefault(s => s.Id == Str(a, "id"));
                var bmp = await FaviconFetcher.FetchForAsync(url, existing?.IconSite);
                if (bmp == null) throw new UserError("Иконка не нашлась, выберите файл");
                return Pending(bmp);
            }

            case "pickIcon":
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Иконка для уведомления",
                    Filter = "Изображения|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы|*.*",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                var bmp = FaviconFetcher.Decode(File.ReadAllBytes(dlg.FileName))
                          ?? throw new UserError("Не удалось прочитать картинку");
                return Pending(bmp);
            }

            case "pickProgram":
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Программа или игра",
                    Filter = "Программы и ярлыки|*.exe;*.lnk;*.url|Все файлы|*.*",
                    DereferenceLinks = false,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                var bmp = FaviconFetcher.FromFile(dlg.FileName);
                return new
                {
                    path = dlg.FileName,
                    name = Path.GetFileNameWithoutExtension(dlg.FileName),
                    icon = bmp != null ? Pending(bmp) : null,
                };
            }

            case "parseQuick":
            {
                var r = QuickParser.Parse(Str(a, "text"), DateTime.Now);
                return r == null ? null : new { when = QuickParser.Describe(r.At, DateTime.Now), text = r.Text };
            }

            case "addQuick":
            {
                var text = Str(a, "text").Trim();
                var minutes = Int(a, "minutes");
                if (minutes > 0)
                {
                    var parsed = QuickParser.Parse(text, DateTime.Now);
                    if (parsed != null) text = parsed.Text;
                    if (text.Length == 0) text = "Напоминание";
                    else text = char.ToUpper(text[0]) + text[1..];
                    _ctx.AddReminder(DateTime.Now.AddMinutes(minutes), text);
                }
                else
                {
                    var r = QuickParser.Parse(text, DateTime.Now)
                            ?? throw new UserError("Не вижу, когда напомнить. Добавьте «через 10 минут» или «в 18:30»");
                    _ctx.AddReminder(r.At, r.Text);
                }
                PushState();
                return null;
            }

            case "deleteReminder":
                Store.Data.Reminders.RemoveAll(r => r.Id == Str(a, "id"));
                Store.Save();
                PushState();
                return null;

            case "setPaused":
                _ctx.SetPaused(Bool(a, "value"));
                PushState();
                return null;

            case "setAutostart":
                Autostart.Enabled = Bool(a, "value");
                PushState();
                return null;

            case "setCatchUp":
                Store.Data.CatchUpMissed = Bool(a, "value");
                Store.Save();
                PushState();
                return null;

            case "setHotkey":
            {
                var keys = GlobalHotkey.Parse(Str(a, "value"));
                Store.Data.QuickHotkey = GlobalHotkey.Format(keys);
                Store.Save();
                bool ok = _ctx.ApplyHotkey();
                PushState();
                if (!ok) throw new UserError("Это сочетание занято другой программой, выберите другое");
                return null;
            }

            case "setTemplate":
            {
                var t = Templates.All.FirstOrDefault(x => x.Id == Str(a, "id")) ?? throw new UserError("Нет такого шаблона");
                if (Bool(a, "on"))
                {
                    if (!Store.Data.Sites.Any(t.Matches))
                    {
                        var e = t.ToEntry();
                        Store.Data.Sites.Add(e);
                        Store.Save();
                        PushState();
                        try { await FaviconFetcher.EnsureIconAsync(e); } catch { }
                        Notifier.Prepare(e);
                    }
                }
                else
                {
                    foreach (var e in Store.Data.Sites.Where(t.Matches).ToList())
                    {
                        Store.Data.Sites.Remove(e);
                        CleanUp(e);
                    }
                    Store.Save();
                }
                PushState();
                return null;
            }

            case "openExternal":
                OpenExternal(Str(a, "url"));
                return null;

            case "voice":
                BeginInvoke(async () =>
                {
                    Activate();
                    _web.Focus();
                    await Task.Delay(200);
                    VoiceTyping.Start();
                });
                return null;

            case "classic":
                BeginInvoke(_ctx.FallbackToClassic);
                return null;

            default:
                throw new UserError($"Неизвестная команда {cmd}");
        }
    }

    object Pending(Bitmap bmp)
    {
        var token = Guid.NewGuid().ToString("N");
        _pendingIcons[token] = bmp;
        using var small = FaviconFetcher.ToSquare(bmp, 96);
        using var ms = new MemoryStream();
        small.Save(ms, ImageFormat.Png);
        return new { token, dataUrl = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray()), size = $"{bmp.Width}×{bmp.Height}" };
    }

    // Validates the editor's fields and copies them into the entry.
    void FillEntry(SiteEntry entry, JsonObject a, bool keepPendingIcon = false)
    {
        var url = NormalizeUrl(Str(a, "url"));
        if (url.Length > 0 && !IsPath(url) && !Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new UserError("Не понимаю ссылку. Пример: https://www.chess.com/ или steam://rungameid/730");

        var name = Str(a, "name").Trim();
        if (name.Length == 0 && Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            name = u.Host.StartsWith("www.") ? u.Host[4..] : u.Host;
        if (name.Length == 0 && IsPath(url)) name = Path.GetFileNameWithoutExtension(url);
        if (name.Length == 0) throw new UserError("Укажите название, например «Тренировка» или «Genshin Impact»");

        var parsed = new List<string>();
        foreach (var t in Str(a, "times").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TimeSpan.TryParse(t.Replace('.', ':'), out var ts) || ts < TimeSpan.Zero || ts >= TimeSpan.FromDays(1))
                throw new UserError($"Не понимаю время «{t}». Пишите как 09:00");
            parsed.Add(ts.ToString(@"hh\:mm"));
        }
        if (parsed.Count == 0) throw new UserError("Укажите хотя бы одно время, например 09:00");
        parsed = parsed.Distinct().OrderBy(x => x).ToList();

        var days = (a["days"] as JsonArray)?.Select(n => (DayOfWeek)n.GetValue<int>()).Distinct().ToList() ?? new();
        if (days.Count == 0) throw new UserError("Отметьте хотя бы один день");

        bool scheduleChanged = !entry.Times.SequenceEqual(parsed) ||
                               !entry.Days.OrderBy(d => d).SequenceEqual(days.OrderBy(d => d));

        entry.Url = url;
        entry.Title = Str(a, "title").Trim();
        entry.Text = Str(a, "text").Trim();
        entry.Times = parsed;
        entry.Days = days;
        entry.Enabled = a["enabled"]?.GetValue<bool>() ?? true;
        if (scheduleChanged) entry.LastFiredUtc = DateTime.UtcNow;

        var token = Str(a, "iconToken");
        _pendingIcons.TryGetValue(token, out var icon);
        if (name != entry.Name || icon != null)
        {
            // A new sender identity, so Windows doesn't show a cached name or icon.
            entry.Rev++;
            entry.Name = name;
            if (icon != null)
            {
                FaviconFetcher.SaveIcon(entry, icon);
                if (!keepPendingIcon)
                {
                    _pendingIcons.Remove(token);
                    icon.Dispose();
                }
            }
        }
    }

    static string NormalizeUrl(string s)
    {
        s = s.Trim().Trim('"');
        if (s.Length == 0) return s;
        if (s.Contains("://") || IsPath(s)) return s;
        return "https://" + s;
    }

    static bool IsPath(string s) => s.Length > 2 && (s[1] == ':' || s.StartsWith(@"\\"));

    static void CleanUp(SiteEntry s)
    {
        Notifier.Remove(s);
        if (s.IconFile.StartsWith(Store.IconsDir))
        {
            try { File.Delete(s.IconFile); } catch { }
            try { File.Delete(Path.ChangeExtension(s.IconFile, ".ico")); } catch { }
        }
    }

    static void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Dispose();
            foreach (var b in _pendingIcons.Values) b.Dispose();
            _pendingIcons.Clear();
        }
        base.Dispose(disposing);
    }
}
