using System.Diagnostics;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AnnounceMe;

// Every entry gets its own AppUserModelID with the entry's name and icon, so Windows shows the
// toast as coming from that site or game rather than from this app. The ID is registered twice:
// under HKCU (name and icon in the toast header) and as a Start menu shortcut (the logo that
// programs reading notifications, such as Island, get from Windows).
public static class Notifier
{
    const string AumidRoot = @"Software\Classes\AppUserModelId";
    const string Prefix = "AnnounceMe.Site.";
    const string ReminderAumid = "AnnounceMe.Reminder";

    static string Aumid(SiteEntry e) => $"{Prefix}{e.Id}.r{e.Rev}";

    public static async Task ShowAsync(SiteEntry e)
    {
        await FaviconFetcher.EnsureIconAsync(e);

        var aumid = Aumid(e);
        Register(e, aumid);

        var name = e.DisplayName;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Title)) lines.Add(e.Title);
        if (!string.IsNullOrWhiteSpace(e.Text)) lines.Add(e.Text);
        if (lines.Count == 0) lines.Add($"Пора заглянуть: {name}");

        var texts = string.Concat(lines.Select(l => $"<text>{SecurityElement.Escape(l)}</text>"));
        var image = File.Exists(e.IconFile)
            ? $"<image placement=\"appLogoOverride\" src=\"{SecurityElement.Escape(new Uri(e.IconFile).AbsoluteUri)}\"/>"
            : "";

        // Web links open straight from the toast; programs and steam:// go through this app.
        string target = null;
        if (e.IsWebLink) target = e.Url;
        else if (!string.IsNullOrWhiteSpace(e.Url)) target = $"announceme:open?id={e.Id}";

        var launch = target != null
            ? $" launch=\"{SecurityElement.Escape(target)}\" activationType=\"protocol\""
            : " launch=\"announceme:noop\" activationType=\"protocol\"";
        var open = target != null
            ? $"<action content=\"Открыть {SecurityElement.Escape(name)}\" activationType=\"protocol\" arguments=\"{SecurityElement.Escape(target)}\"/>"
            : "";

        var xml =
            $"<toast{launch}>" +
            "<visual><binding template=\"ToastGeneric\">" + texts + image + "</binding></visual>" +
            "<actions>" + open +
            "<action content=\"Закрыть\" activationType=\"system\" arguments=\"dismiss\"/>" +
            "</actions>" +
            "</toast>";

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        ToastNotificationManager.CreateToastNotifier(aumid).Show(new ToastNotification(doc));
    }

    // Registers the sender ahead of time, so the first notification already has its logo.
    public static void Prepare(SiteEntry e)
    {
        try { Register(e, Aumid(e)); } catch { }
    }

    public static void ShowReminder(Reminder r)
    {
        var iconPath = Path.Combine(Store.Dir, "reminder.png");
        if (!File.Exists(iconPath))
        {
            using var src = typeof(Notifier).Assembly.GetManifestResourceStream("app.png");
            using var f = File.Create(iconPath);
            src.CopyTo(f);
        }
        using (var key = Registry.CurrentUser.CreateSubKey($@"{AumidRoot}\{ReminderAumid}"))
        {
            key.SetValue("DisplayName", "Напоминание");
            key.SetValue("IconUri", iconPath);
        }
        var link = Path.Combine(ShellLink.StartMenuDir, "Напоминание.lnk");
        if (!File.Exists(link))
        {
            var ico = Path.Combine(Store.Dir, "reminder.ico");
            using (var img = Image.FromFile(iconPath)) IcoWriter.Write(img, ico);
            TryCreateShortcut(link, ReminderAumid, "announceme:quick", ico, "Быстрое напоминание");
        }

        var text = SecurityElement.Escape(r.Text);
        var at = r.AtUtc.ToLocalTime().ToString("HH:mm");
        var snoozeText = Uri.EscapeDataString(r.Text);
        string Snooze(int min) => SecurityElement.Escape($"announceme:snooze?min={min}&text={snoozeText}");

        var xml =
            "<toast scenario=\"reminder\" launch=\"announceme:noop\" activationType=\"protocol\">" +
            $"<visual><binding template=\"ToastGeneric\"><text>{text}</text><text>На {at}</text></binding></visual>" +
            "<actions>" +
            $"<action content=\"Ещё 5 мин\" activationType=\"protocol\" arguments=\"{Snooze(5)}\"/>" +
            $"<action content=\"Ещё 15 мин\" activationType=\"protocol\" arguments=\"{Snooze(15)}\"/>" +
            "<action content=\"Готово\" activationType=\"system\" arguments=\"dismiss\"/>" +
            "</actions>" +
            "</toast>";

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        ToastNotificationManager.CreateToastNotifier(ReminderAumid).Show(new ToastNotification(doc));
    }

    // Opens what the entry points to: a site, a steam:// link or a program.
    public static void Launch(SiteEntry e)
    {
        if (string.IsNullOrWhiteSpace(e.Url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(e.Url) { UseShellExecute = true });
        }
        catch { }
    }

    static void Register(SiteEntry e, string aumid)
    {
        var name = e.DisplayName;
        using (var key = Registry.CurrentUser.CreateSubKey($@"{AumidRoot}\{aumid}"))
        {
            key.SetValue("DisplayName", name);
            if (File.Exists(e.IconFile)) key.SetValue("IconUri", e.IconFile);
            else key.DeleteValue("IconUri", false);
        }

        var ico = File.Exists(e.IconFile) ? Path.ChangeExtension(e.IconFile, ".ico") : null;
        if (ico != null && !File.Exists(ico))
            using (var img = Image.FromFile(e.IconFile)) IcoWriter.Write(img, ico);

        var link = Path.Combine(ShellLink.StartMenuDir, $"{ShellLink.SafeFileName(name)}.lnk");
        if (Store.Data.Sites.Any(s => s.Id != e.Id && s.ShortcutFile == link))
            link = Path.Combine(ShellLink.StartMenuDir, $"{ShellLink.SafeFileName(name)} ({e.Id[..4]}).lnk");
        if (e.ShortcutFile != link || e.ShortcutAumid != aumid || !File.Exists(link))
        {
            DeleteShortcut(e);
            if (TryCreateShortcut(link, aumid, $"announceme:open?id={e.Id}", ico, e.Text))
            {
                e.ShortcutFile = link;
                e.ShortcutAumid = aumid;
                Store.Save();
            }
        }
        Unregister(e.Id, keep: aumid);
    }

    static bool TryCreateShortcut(string path, string aumid, string args, string ico, string description)
    {
        try
        {
            ShellLink.Create(path, aumid, args, ico, description);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void DeleteShortcut(SiteEntry e)
    {
        if (!string.IsNullOrEmpty(e.ShortcutFile) && e.ShortcutFile.StartsWith(ShellLink.StartMenuDir))
            try { File.Delete(e.ShortcutFile); } catch { }
        e.ShortcutFile = "";
        e.ShortcutAumid = "";
    }

    public static void Remove(SiteEntry e)
    {
        DeleteShortcut(e);
        Unregister(e.Id);
    }

    // Removes this entry's registrations (all of them, or all but `keep`).
    static void Unregister(string id, string keep = null)
    {
        using var root = Registry.CurrentUser.OpenSubKey(AumidRoot, writable: true);
        if (root == null) return;
        foreach (var name in root.GetSubKeyNames())
            if (name.StartsWith($"{Prefix}{id}.") && name != keep)
                root.DeleteSubKeyTree(name, false);
    }
}
