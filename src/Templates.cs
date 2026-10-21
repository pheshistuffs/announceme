namespace AnnounceMe;

// Ready-made daily reminders for games with dailies and a few habits.
public record Template(
    string Id,
    string Group,
    string Name,
    string Text,
    string Url,
    string IconSite = "",
    // Daily reset in UTC (EU servers). The reminder goes off at the reset in local time,
    // but not earlier than 10:00.
    TimeSpan? ResetUtc = null,
    // Used when there is no reset time.
    TimeSpan? LocalTime = null,
    DayOfWeek[] Days = null)
{
    public TimeSpan ReminderTime
    {
        get
        {
            if (ResetUtc is { } utc)
            {
                var local = (DateTime.UtcNow.Date + utc).ToLocalTime().TimeOfDay;
                return local < TimeSpan.FromHours(10) ? TimeSpan.FromHours(10) : local;
            }
            return LocalTime ?? TimeSpan.FromHours(10);
        }
    }

    public string ResetText => ResetUtc is { } utc
        ? $"сброс в {(DateTime.UtcNow.Date + utc).ToLocalTime():HH:mm}"
        : "";

    public SiteEntry ToEntry() => new()
    {
        Name = Name,
        Text = Text,
        Url = Url,
        IconSite = IconSite,
        TemplateId = Id,
        Times = new() { ReminderTime.ToString(@"hh\:mm") },
        Days = (Days ?? Enum.GetValues<DayOfWeek>()).ToList(),
    };

    public bool Matches(SiteEntry e) =>
        e.TemplateId == Id || (string.IsNullOrEmpty(e.TemplateId) && !string.IsNullOrEmpty(Url) &&
                               string.Equals(e.Url.TrimEnd('/'), Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
}

public static class Templates
{
    public const string Gacha = "Гача-игры";
    public const string Games = "Другие игры";
    public const string Habits = "Сайты и привычки";

    static TimeSpan T(int h, int m = 0) => new(h, m, 0);

    public static readonly Template[] All =
    {
        new("genshin", Gacha, "Genshin Impact",
            "Поручения обновились: сделай дейлики, потрать смолу и отметься в HoYoLAB",
            "https://act.hoyolab.com/ys/event/signin-sea-v3/index.html?act_id=e202102251931481",
            "https://genshin.hoyoverse.com/", ResetUtc: T(3)),
        new("hsr", Gacha, "Honkai: Star Rail",
            "Ежедневная тренировка обновилась: потрать энергию освоения и отметься в HoYoLAB",
            "https://act.hoyolab.com/bbs/event/signin/hkrpg/index.html?act_id=e202303301540311",
            "https://hsr.hoyoverse.com/", ResetUtc: T(3)),
        new("zzz", Gacha, "Zenless Zone Zero",
            "Ежедневные дела обновились: потрать заряд батареи и отметься в HoYoLAB",
            "https://act.hoyolab.com/bbs/event/signin/zzz/e202406031448091.html?act_id=e202406031448091",
            "https://zenless.hoyoverse.com/", ResetUtc: T(3)),
        new("wuwa", Gacha, "Wuthering Waves",
            "Дейлики обновились: сделай активности и потрать волновые пластины",
            "https://wutheringwaves.kurogames.com/", ResetUtc: T(3)),
        new("arknights", Gacha, "Arknights",
            "Ежедневные задания обновились: потрать санити и собери базу",
            "https://www.arknights.global/", ResetUtc: T(11)),
        new("bluearchive", Gacha, "Blue Archive",
            "Дейлики обновились: потрать AP и загляни в кафе",
            "https://bluearchive.nexon.com/", ResetUtc: T(19)),

        new("hearthstone", Games, "Hearthstone",
            "Новые ежедневные задания",
            "https://hearthstone.blizzard.com/", LocalTime: T(18)),
        new("mirtankov", Games, "Мир танков",
            "Ежедневные боевые задачи и бонус за первую победу",
            "https://tanki.su/", LocalTime: T(19)),
        new("wot", Games, "World of Tanks",
            "Ежедневные задачи и бонус за первую победу",
            "https://worldoftanks.eu/", LocalTime: T(19)),
        new("epic", Games, "Epic Games Store",
            "Новая бесплатная игра недели — забери её",
            "https://store.epicgames.com/ru/free-games", "https://store.epicgames.com/",
            ResetUtc: T(16, 5), Days: new[] { DayOfWeek.Thursday }),

        new("chesscom", Habits, "chess.com",
            "Обновились ежедневные задачи по шахматам",
            "https://www.chess.com/", LocalTime: T(9)),
        new("lichess", Habits, "Lichess",
            "Задача дня ждёт",
            "https://lichess.org/training/daily", "https://lichess.org/", LocalTime: T(10)),
        new("duolingo", Habits, "Duolingo",
            "Один урок, чтобы не потерять ударный режим",
            "https://www.duolingo.com/", LocalTime: T(20)),
        new("water", Habits, "Перерыв",
            "Встань, разомнись и выпей воды",
            "", LocalTime: T(15)),
    };

    public static string Describe(Template t)
    {
        var days = t.Days == null ? "каждый день" :
            string.Join(", ", Schedule.WeekOrder.Where(t.Days.Contains).Select(Schedule.ShortName));
        var when = $"{days}, {t.ReminderTime:hh\\:mm}";
        return t.ResetText.Length > 0 ? $"{when} ({t.ResetText})" : when;
    }
}
