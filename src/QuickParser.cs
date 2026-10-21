using System.Globalization;
using System.Text.RegularExpressions;

namespace AnnounceMe;

// Reads phrases like "через 5 минут достать курицу из духовки", "в 18:30 позвонить маме",
// "завтра в 9 утра купить хлеб" or the short "10м чай" into a moment and a text.
public static class QuickParser
{
    public record Result(DateTime At, string Text);

    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly Regex Through = new(@"(?<!\w)через(?!\w)", Opts);

    static readonly Regex DurPart = new(
        @"\G\s*(?:(?<num>\d+(?:[.,]\d+)?)\s*(?<unit>секунд[уы]?|сек|с|минут[уы]?|мин|м|час(?:а|ов)?|ч|дн(?:я|ей)|день|д)(?!\w)" +
        @"|(?<word>полчаса|полминуты|полтора\s+часа|пару\s+минут|минуту|час|сутки)(?!\w))(?:\s+и(?!\w))?", Opts);

    static readonly Regex LeadingNumber = new(@"^\s*(?<num>\d+)(?=\s+\D)", Opts);

    static readonly Regex DayWord = new(@"(?<!\w)(?<day>сегодня|завтра|послезавтра)(?!\w)", Opts);

    static readonly Regex Clock = new(
        @"(?<!\w)(?:в\s+)?(?<h>\d{1,2})[:.](?<m>\d{2})(?:\s*(?<part>утра|дня|вечера|ночи))?(?!\w)" +
        @"|(?<!\w)в\s+(?<h>\d{1,2})(?:\s*час(?:а|ов)?)?(?:\s*(?<part>утра|дня|вечера|ночи))?(?!\w)", Opts);

    static readonly Regex Filler = new(
        @"^(?:(?:напомни(?:ть)?|напоминание|мне|что|чтобы|нужно|надо|пора|,|-|—|:)\s*)+", Opts);

    public static Result Parse(string input, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = NumbersToDigits(input.Trim());
        DateTime? at = null;

        var through = Through.Match(s);
        if (through.Success && ReadDuration(s, through.Index + through.Length, out var dur, out var end) && dur > TimeSpan.Zero)
        {
            at = now + dur;
            s = Cut(s, through.Index, end);
        }
        else if (ReadDuration(s, 0, out dur, out end) && dur > TimeSpan.Zero)
        {
            at = now + dur;
            s = Cut(s, 0, end);
        }
        else
        {
            var clock = Clock.Match(s);
            var day = DayWord.Match(s);
            if (clock.Success)
            {
                int h = int.Parse(clock.Groups["h"].Value);
                int m = clock.Groups["m"].Success ? int.Parse(clock.Groups["m"].Value) : 0;
                var part = clock.Groups["part"].Value.ToLowerInvariant();
                if ((part == "вечера" || part == "дня") && h < 12) h += 12;
                if (part == "ночи" && h == 12) h = 0;
                if (h > 23 || m > 59) return null;

                var date = now.Date;
                if (day.Success)
                    date = date.AddDays(day.Groups["day"].Value.ToLowerInvariant() switch
                    {
                        "завтра" => 1,
                        "послезавтра" => 2,
                        _ => 0,
                    });
                var candidate = date.AddHours(h).AddMinutes(m);
                if (!day.Success && candidate <= now) candidate = candidate.AddDays(1);
                at = candidate;

                // Cut the later span first so the earlier index stays valid.
                if (day.Success && day.Index > clock.Index)
                {
                    s = Cut(s, day.Index, day.Index + day.Length);
                    s = Cut(s, clock.Index, clock.Index + clock.Length);
                }
                else
                {
                    s = Cut(s, clock.Index, clock.Index + clock.Length);
                    if (day.Success) s = Cut(s, day.Index, day.Index + day.Length);
                }
            }
            else
            {
                var lead = LeadingNumber.Match(s);
                if (!lead.Success) return null;
                at = now.AddMinutes(int.Parse(lead.Groups["num"].Value));
                s = Cut(s, 0, lead.Length);
            }
        }

        if (at <= now) return null;
        return new Result(at.Value, CleanText(s));
    }

    static readonly Dictionary<string, int> Units = new()
    {
        ["ноль"] = 0, ["один"] = 1, ["одна"] = 1, ["одну"] = 1, ["два"] = 2, ["две"] = 2, ["три"] = 3, ["четыре"] = 4,
        ["пять"] = 5, ["шесть"] = 6, ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10,
        ["одиннадцать"] = 11, ["двенадцать"] = 12, ["тринадцать"] = 13, ["четырнадцать"] = 14, ["пятнадцать"] = 15,
        ["шестнадцать"] = 16, ["семнадцать"] = 17, ["восемнадцать"] = 18, ["девятнадцать"] = 19,
    };

    static readonly Dictionary<string, int> Tens = new() { ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40, ["пятьдесят"] = 50 };

    static readonly Regex NumberWords = new(
        @"(?<!\w)(?:(?<tens>двадцать|тридцать|сорок|пятьдесят)(?:\s+(?<unit1>один|одна|одну|два|две|три|четыре|пять|шесть|семь|восемь|девять))?" +
        @"|(?<unit2>ноль|одиннадцать|двенадцать|тринадцать|четырнадцать|пятнадцать|шестнадцать|семнадцать|восемнадцать|девятнадцать|десять|один|одна|одну|два|две|три|четыре|пять|шесть|семь|восемь|девять))(?!\w)", Opts);

    // Dictation often comes as words: "через пять минут" -> "через 5 минут".
    public static string NumbersToDigits(string s) => NumberWords.Replace(s, m =>
        (m.Groups["tens"].Success
            ? Tens[m.Groups["tens"].Value.ToLowerInvariant()] +
              (m.Groups["unit1"].Success ? Units[m.Groups["unit1"].Value.ToLowerInvariant()] : 0)
            : Units[m.Groups["unit2"].Value.ToLowerInvariant()]).ToString());

    static bool ReadDuration(string s, int pos, out TimeSpan total, out int end)
    {
        total = TimeSpan.Zero;
        end = pos;
        bool any = false;
        while (true)
        {
            var m = DurPart.Match(s, end);
            if (!m.Success || m.Length == 0) break;
            total += PartValue(m);
            end = m.Index + m.Length;
            any = true;
        }
        return any;
    }

    static TimeSpan PartValue(Match m)
    {
        if (m.Groups["word"].Success)
        {
            var w = Regex.Replace(m.Groups["word"].Value.ToLowerInvariant(), @"\s+", " ");
            return w switch
            {
                "полчаса" => TimeSpan.FromMinutes(30),
                "полминуты" => TimeSpan.FromSeconds(30),
                "полтора часа" => TimeSpan.FromMinutes(90),
                "пару минут" => TimeSpan.FromMinutes(2),
                "минуту" => TimeSpan.FromMinutes(1),
                "час" => TimeSpan.FromHours(1),
                _ => TimeSpan.FromDays(1),
            };
        }
        var n = double.Parse(m.Groups["num"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = m.Groups["unit"].Value.ToLowerInvariant();
        if (unit.StartsWith("с")) return TimeSpan.FromSeconds(n);
        if (unit.StartsWith("м")) return TimeSpan.FromMinutes(n);
        if (unit.StartsWith("ч")) return TimeSpan.FromHours(n);
        return TimeSpan.FromDays(n);
    }

    static string Cut(string s, int from, int to) => (s[..from] + " " + s[to..]).Trim();

    static string CleanText(string s)
    {
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Filler.Replace(s, "").Trim(' ', ',', '.', '-', '—', ':');
        if (s.Length == 0) return "Напоминание";
        return char.ToUpper(s[0]) + s[1..];
    }

    public static string Describe(DateTime at, DateTime now)
    {
        string when;
        if (at.Date == now.Date) when = $"сегодня в {at:HH:mm}";
        else if (at.Date == now.Date.AddDays(1)) when = $"завтра в {at:HH:mm}";
        else when = $"{at:dd.MM} в {at:HH:mm}";
        var left = at - now;
        string inText = left.TotalMinutes < 1 ? $"{Math.Max(1, (int)Math.Round(left.TotalSeconds))} сек"
            : left.TotalHours < 1 ? $"{(int)Math.Round(left.TotalMinutes)} мин"
            : left.TotalDays < 1 ? $"{(int)left.TotalHours} ч {left.Minutes} мин"
            : $"{(int)left.TotalDays} дн";
        return $"{when} (через {inText})";
    }
}
