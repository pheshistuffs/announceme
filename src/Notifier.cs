using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace AnnounceMe;

// Every site gets its own AppUserModelID registered under HKCU with the site's name and
// favicon, so Windows shows the toast as coming from that site rather than from this app.
public static class Notifier
{
    const string AumidRoot = @"Software\Classes\AppUserModelId";
    const string Prefix = "AnnounceMe.Site.";

    static string Aumid(SiteEntry e) => $"{Prefix}{e.Id}.r{e.Rev}";

    public static async Task ShowAsync(SiteEntry e)
    {
        await FaviconFetcher.EnsureIconAsync(e);

        var aumid = Aumid(e);
        Register(e, aumid);

        var url = SecurityElement.Escape(e.Url);
        var name = SecurityElement.Escape(string.IsNullOrWhiteSpace(e.Name) ? e.Host : e.Name);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(e.Title)) lines.Add(e.Title);
        if (!string.IsNullOrWhiteSpace(e.Text)) lines.Add(e.Text);
        if (lines.Count == 0) lines.Add($"Пора зайти на {e.Host}");

        var texts = string.Concat(lines.Select(l => $"<text>{SecurityElement.Escape(l)}</text>"));
        var image = File.Exists(e.IconFile)
            ? $"<image placement=\"appLogoOverride\" src=\"{SecurityElement.Escape(new Uri(e.IconFile).AbsoluteUri)}\"/>"
            : "";

        var xml =
            $"<toast launch=\"{url}\" activationType=\"protocol\">" +
            "<visual><binding template=\"ToastGeneric\">" + texts + image + "</binding></visual>" +
            "<actions>" +
            $"<action content=\"Открыть {name}\" activationType=\"protocol\" arguments=\"{url}\"/>" +
            "<action content=\"Закрыть\" activationType=\"system\" arguments=\"dismiss\"/>" +
            "</actions>" +
            "</toast>";

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        ToastNotificationManager.CreateToastNotifier(aumid).Show(new ToastNotification(doc));
    }

    const string ReminderAumid = "AnnounceMe.Reminder";

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

    static void Register(SiteEntry e, string aumid)
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"{AumidRoot}\{aumid}"))
        {
            key.SetValue("DisplayName", string.IsNullOrWhiteSpace(e.Name) ? e.Host : e.Name);
            if (File.Exists(e.IconFile)) key.SetValue("IconUri", e.IconFile);
            else key.DeleteValue("IconUri", false);
        }
        Unregister(e.Id, keep: aumid);
    }

    // Removes this site's registrations (all of them, or all but `keep`).
    public static void Unregister(string id, string keep = null)
    {
        using var root = Registry.CurrentUser.OpenSubKey(AumidRoot, writable: true);
        if (root == null) return;
        foreach (var name in root.GetSubKeyNames())
            if (name.StartsWith($"{Prefix}{id}.") && name != keep)
                root.DeleteSubKeyTree(name, false);
    }
}
