using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnnounceMe;

public class SiteEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Url { get; set; } = "";
    // Shown as the notification's sender (header of the toast).
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<DayOfWeek> Days { get; set; } = Enum.GetValues<DayOfWeek>().ToList();
    public List<string> Times { get; set; } = new() { "09:00" };
    public string IconFile { get; set; } = "";
    // Bumped whenever the name or icon changes: Windows caches the sender identity,
    // so a new revision gets a fresh AppUserModelID.
    public int Rev { get; set; }
    public DateTime LastFiredUtc { get; set; } = DateTime.UtcNow;

    public SiteEntry Clone() => JsonSerializer.Deserialize<SiteEntry>(JsonSerializer.Serialize(this));

    public string Host
    {
        get
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out var u))
                return u.Host.StartsWith("www.") ? u.Host[4..] : u.Host;
            return Url;
        }
    }

    public IEnumerable<TimeSpan> ParsedTimes()
    {
        foreach (var t in Times)
            if (TimeSpan.TryParse(t, out var ts) && ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1))
                yield return ts;
    }

    public string ScheduleText()
    {
        var times = string.Join(", ", ParsedTimes().OrderBy(t => t).Select(t => t.ToString(@"hh\:mm")));
        string days;
        if (Days.Count == 7) days = "каждый день";
        else if (Days.Count == 5 && !Days.Contains(DayOfWeek.Saturday) && !Days.Contains(DayOfWeek.Sunday)) days = "по будням";
        else if (Days.Count == 0) days = "никогда";
        else days = string.Join(", ", Schedule.WeekOrder.Where(Days.Contains).Select(Schedule.ShortName));
        return $"{days}, {times}";
    }
}

public class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public DateTime AtUtc { get; set; }
    public string Text { get; set; } = "";
}

public class AppData
{
    public List<SiteEntry> Sites { get; set; } = new();
    public List<Reminder> Reminders { get; set; } = new();
    // Global hotkey for a quick reminder, e.g. "Ctrl+Alt+N"; empty turns it off.
    public string QuickHotkey { get; set; } = "Ctrl+Alt+N";
    public bool CatchUpMissed { get; set; } = true;
    public bool Paused { get; set; }
    public bool TrayHintShown { get; set; }
}

public static class Store
{
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnnounceMe");
    public static readonly string IconsDir = Path.Combine(Dir, "icons");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppData Data { get; private set; }

    public static void Load()
    {
        Directory.CreateDirectory(IconsDir);
        if (File.Exists(FilePath))
        {
            try
            {
                Data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options);
            }
            catch
            {
                File.Copy(FilePath, FilePath + ".broken", true);
            }
        }
        if (Data == null)
        {
            Data = new AppData();
            Data.Sites.Add(new SiteEntry
            {
                Url = "https://www.chess.com/",
                Name = "chess.com",
                Text = "Обновились ежедневные задачи по шахматам",
                Times = new() { "09:00" },
            });
            Save();
        }
        CleanupIcons();
    }

    // Icons left behind by edits that were cancelled or replaced.
    static void CleanupIcons()
    {
        var used = Data.Sites.Select(s => s.IconFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(IconsDir))
            if (!used.Contains(f))
                try { File.Delete(f); } catch { }
    }

    public static void Save()
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Data, Options));
        File.Move(tmp, FilePath, true);
    }
}
