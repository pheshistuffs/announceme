namespace AnnounceMe;

public static class Schedule
{
    public static readonly DayOfWeek[] WeekOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    public static string ShortName(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Пн",
        DayOfWeek.Tuesday => "Вт",
        DayOfWeek.Wednesday => "Ср",
        DayOfWeek.Thursday => "Чт",
        DayOfWeek.Friday => "Пт",
        DayOfWeek.Saturday => "Сб",
        _ => "Вс",
    };

    // The most recent scheduled moment that is not in the future, within the last week.
    public static DateTime? LastOccurrence(SiteEntry e, DateTime nowLocal)
    {
        var times = e.ParsedTimes().OrderByDescending(t => t).ToList();
        if (times.Count == 0) return null;
        for (int back = 0; back <= 7; back++)
        {
            var date = nowLocal.Date.AddDays(-back);
            if (!e.Days.Contains(date.DayOfWeek)) continue;
            foreach (var t in times)
            {
                var at = date + t;
                if (at <= nowLocal) return at;
            }
        }
        return null;
    }

    public static DateTime? NextOccurrence(SiteEntry e, DateTime nowLocal)
    {
        var times = e.ParsedTimes().OrderBy(t => t).ToList();
        if (times.Count == 0) return null;
        for (int ahead = 0; ahead <= 7; ahead++)
        {
            var date = nowLocal.Date.AddDays(ahead);
            if (!e.Days.Contains(date.DayOfWeek)) continue;
            foreach (var t in times)
            {
                var at = date + t;
                if (at > nowLocal) return at;
            }
        }
        return null;
    }

    // Entries whose scheduled moment has come and was not shown yet.
    public static List<SiteEntry> Due(AppData data, DateTime nowLocal)
    {
        var window = data.CatchUpMissed ? TimeSpan.FromHours(12) : TimeSpan.FromMinutes(5);
        var due = new List<SiteEntry>();
        foreach (var e in data.Sites.Where(s => s.Enabled))
        {
            var occ = LastOccurrence(e, nowLocal);
            if (occ == null) continue;
            if (occ.Value.ToUniversalTime() > e.LastFiredUtc && nowLocal - occ.Value <= window)
                due.Add(e);
        }
        return due;
    }
}
