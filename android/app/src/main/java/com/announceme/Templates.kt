package com.announceme

import java.time.LocalDate
import java.time.LocalTime
import java.time.ZoneId
import java.time.ZoneOffset

// Ready-made daily reminders for games with dailies and a few habits (same list as Templates.cs).
class Template(
    val id: String,
    val group: String,
    val name: String,
    val text: String,
    val url: String,
    val iconSite: String = "",
    // Daily reset in UTC (EU servers): the reminder goes off then in local time, not before 10:00.
    val resetUtc: LocalTime? = null,
    val localTime: LocalTime? = null,
    val days: List<Int>? = null,
) {
    private fun resetLocal(): LocalTime? = resetUtc?.let {
        LocalDate.now(ZoneOffset.UTC).atTime(it).atOffset(ZoneOffset.UTC)
            .atZoneSameInstant(ZoneId.systemDefault()).toLocalTime()
    }

    val reminderTime: LocalTime
        get() {
            resetLocal()?.let { return if (it.isBefore(LocalTime.of(10, 0))) LocalTime.of(10, 0) else it }
            return localTime ?: LocalTime.of(10, 0)
        }

    val describe: String
        get() {
            val d = days?.let { ds -> Schedule.weekOrder.filter { it in ds }.joinToString(", ") { Schedule.shortName(it) } } ?: "каждый день"
            val t = "%02d:%02d".format(reminderTime.hour, reminderTime.minute)
            val reset = resetLocal()?.let { " (сброс в %02d:%02d)".format(it.hour, it.minute) } ?: ""
            return "$d, $t$reset"
        }

    fun toSite() = Site(
        name = name, text = text, url = url, iconSite = iconSite, templateId = id,
        times = mutableListOf("%02d:%02d".format(reminderTime.hour, reminderTime.minute)),
        days = (days ?: listOf(0, 1, 2, 3, 4, 5, 6)).toMutableList(),
    )

    fun matches(s: Site) = s.templateId == id ||
        (s.templateId.isEmpty() && url.isNotEmpty() && s.url.trimEnd('/').equals(url.trimEnd('/'), true))
}

object Templates {
    const val GACHA = "Гача-игры"
    const val GAMES = "Другие игры"
    const val HABITS = "Сайты и привычки"

    private fun t(h: Int, m: Int = 0) = LocalTime.of(h, m)

    val all = listOf(
        Template("genshin", GACHA, "Genshin Impact",
            "Поручения обновились: сделай дейлики, потрать смолу и отметься в HoYoLAB",
            "https://act.hoyolab.com/ys/event/signin-sea-v3/index.html?act_id=e202102251931481",
            "https://genshin.hoyoverse.com/", resetUtc = t(3)),
        Template("hsr", GACHA, "Honkai: Star Rail",
            "Ежедневная тренировка обновилась: потрать энергию освоения и отметься в HoYoLAB",
            "https://act.hoyolab.com/bbs/event/signin/hkrpg/index.html?act_id=e202303301540311",
            "https://hsr.hoyoverse.com/", resetUtc = t(3)),
        Template("zzz", GACHA, "Zenless Zone Zero",
            "Ежедневные дела обновились: потрать заряд батареи и отметься в HoYoLAB",
            "https://act.hoyolab.com/bbs/event/signin/zzz/e202406031448091.html?act_id=e202406031448091",
            "https://zenless.hoyoverse.com/", resetUtc = t(3)),
        Template("wuwa", GACHA, "Wuthering Waves",
            "Дейлики обновились: сделай активности и потрать волновые пластины",
            "https://wutheringwaves.kurogames.com/", resetUtc = t(3)),
        Template("arknights", GACHA, "Arknights",
            "Ежедневные задания обновились: потрать санити и собери базу",
            "https://www.arknights.global/", resetUtc = t(11)),
        Template("bluearchive", GACHA, "Blue Archive",
            "Дейлики обновились: потрать AP и загляни в кафе",
            "https://bluearchive.nexon.com/", resetUtc = t(19)),

        Template("hearthstone", GAMES, "Hearthstone", "Новые ежедневные задания",
            "https://hearthstone.blizzard.com/", localTime = t(18)),
        Template("mirtankov", GAMES, "Мир танков", "Ежедневные боевые задачи и бонус за первую победу",
            "https://tanki.su/", localTime = t(19)),
        Template("wot", GAMES, "World of Tanks", "Ежедневные задачи и бонус за первую победу",
            "https://worldoftanks.eu/", localTime = t(19)),
        Template("epic", GAMES, "Epic Games Store", "Новая бесплатная игра недели — забери её",
            "https://store.epicgames.com/ru/free-games", "https://store.epicgames.com/",
            resetUtc = t(16, 5), days = listOf(4)),

        Template("chesscom", HABITS, "chess.com", "Обновились ежедневные задачи по шахматам",
            "https://www.chess.com/", localTime = t(9)),
        Template("lichess", HABITS, "Lichess", "Задача дня ждёт",
            "https://lichess.org/training/daily", "https://lichess.org/", localTime = t(10)),
        Template("duolingo", HABITS, "Duolingo", "Один урок, чтобы не потерять ударный режим",
            "https://www.duolingo.com/", localTime = t(20)),
        Template("water", HABITS, "Перерыв", "Встань, разомнись и выпей воды", "", localTime = t(15)),
    )
}
