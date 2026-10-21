package com.announceme

import java.time.Duration
import java.time.LocalDateTime
import java.util.regex.Matcher
import java.util.regex.Pattern

// Reads phrases like "через 5 минут достать курицу из духовки", "в 18:30 позвонить маме",
// "завтра в 9 утра купить хлеб" or the short "10м чай" into a moment and a text.
// The same rules as QuickParser.cs in the Windows app.
object QuickParser {
    data class Result(val at: LocalDateTime, val text: String)

    // Android's regex engine rejects UNICODE_CHARACTER_CLASS, so letters are spelled out
    // instead of \w (which would be ASCII-only on a desktop JVM).
    private const val F = Pattern.CASE_INSENSITIVE or Pattern.UNICODE_CASE

    private val through = Pattern.compile("(?<![\\p{L}\\p{N}_])через(?![\\p{L}\\p{N}_])", F)
    private val durPart = Pattern.compile(
        "\\s*(?:(?<num>\\d+(?:[.,]\\d+)?)\\s*(?<unit>секунд[уы]?|сек|с|минут[уы]?|мин|м|час(?:а|ов)?|ч|дн(?:я|ей)|день|д)(?![\\p{L}\\p{N}_])" +
            "|(?<word>полчаса|полминуты|полтора\\s+часа|пару\\s+минут|минуту|час|сутки)(?![\\p{L}\\p{N}_]))(?:\\s+и(?![\\p{L}\\p{N}_]))?", F)
    private val leadingNumber = Pattern.compile("^\\s*(?<num>\\d+)(?=\\s+\\D)", F)
    private val dayWord = Pattern.compile("(?<![\\p{L}\\p{N}_])(?<day>сегодня|завтра|послезавтра)(?![\\p{L}\\p{N}_])", F)
    private val clockFull = Pattern.compile(
        "(?<![\\p{L}\\p{N}_])(?:в\\s+)?(?<h>\\d{1,2})[:.](?<m>\\d{2})(?:\\s*(?<part>утра|дня|вечера|ночи))?(?![\\p{L}\\p{N}_])", F)
    private val clockShort = Pattern.compile(
        "(?<![\\p{L}\\p{N}_])в\\s+(?<h>\\d{1,2})(?:\\s*час(?:а|ов)?)?(?:\\s*(?<part>утра|дня|вечера|ночи))?(?![\\p{L}\\p{N}_])", F)
    private val filler = Pattern.compile(
        "^(?:(?:напомни(?:ть)?|напоминание|мне|что|чтобы|нужно|надо|пора|,|-|—|:)\\s*)+", F)

    fun parse(input: String, now: LocalDateTime): Result? {
        if (input.isBlank()) return null
        var s = numbersToDigits(input.trim())
        val at: LocalDateTime

        val th = through.matcher(s)
        val fromThrough = if (th.find()) readDuration(s, th.end())?.takeIf { it.first > Duration.ZERO }?.let { th.start() to it } else null
        val fromStart = if (fromThrough == null) readDuration(s, 0)?.takeIf { it.first > Duration.ZERO } else null

        if (fromThrough != null) {
            at = now.plus(fromThrough.second.first)
            s = cut(s, fromThrough.first, fromThrough.second.second)
        } else if (fromStart != null) {
            at = now.plus(fromStart.first)
            s = cut(s, 0, fromStart.second)
        } else {
            val clock = findClock(s)
            val day = dayWord.matcher(s).let { if (it.find()) it else null }
            if (clock != null) {
                var h = clock.group("h").toInt()
                val m = runCatching { clock.group("m") }.getOrNull()?.toIntOrNull() ?: 0
                val part = clock.group("part")?.lowercase()
                if ((part == "вечера" || part == "дня") && h < 12) h += 12
                if (part == "ночи" && h == 12) h = 0
                if (h > 23 || m > 59) return null
                var date = now.toLocalDate()
                if (day != null) date = date.plusDays(
                    when (day.group("day").lowercase()) { "завтра" -> 1L; "послезавтра" -> 2L; else -> 0L })
                var candidate = date.atTime(h, m)
                if (day == null && !candidate.isAfter(now)) candidate = candidate.plusDays(1)
                at = candidate
                val cs = clock.start(); val ce = clock.end()
                if (day != null && day.start() > cs) {
                    s = cut(s, day.start(), day.end()); s = cut(s, cs, ce)
                } else {
                    s = cut(s, cs, ce)
                    if (day != null) s = cut(s, day.start(), day.end())
                }
            } else {
                val lead = leadingNumber.matcher(s)
                if (!lead.find()) return null
                at = now.plusMinutes(lead.group("num").toLong())
                s = cut(s, 0, lead.end())
            }
        }
        if (!at.isAfter(now)) return null
        return Result(at, clean(s))
    }

    private val units = mapOf(
        "ноль" to 0, "один" to 1, "одна" to 1, "одну" to 1, "два" to 2, "две" to 2, "три" to 3, "четыре" to 4,
        "пять" to 5, "шесть" to 6, "семь" to 7, "восемь" to 8, "девять" to 9, "десять" to 10,
        "одиннадцать" to 11, "двенадцать" to 12, "тринадцать" to 13, "четырнадцать" to 14, "пятнадцать" to 15,
        "шестнадцать" to 16, "семнадцать" to 17, "восемнадцать" to 18, "девятнадцать" to 19)
    private val tens = mapOf("двадцать" to 20, "тридцать" to 30, "сорок" to 40, "пятьдесят" to 50)
    private val numberWords = Pattern.compile(
        "(?<![\\p{L}\\p{N}_])(?:(?<tens>двадцать|тридцать|сорок|пятьдесят)(?:\\s+(?<unit1>один|одна|одну|два|две|три|четыре|пять|шесть|семь|восемь|девять))?" +
            "|(?<unit2>ноль|одиннадцать|двенадцать|тринадцать|четырнадцать|пятнадцать|шестнадцать|семнадцать|восемнадцать|девятнадцать|десять|один|одна|одну|два|две|три|четыре|пять|шесть|семь|восемь|девять))(?![\\p{L}\\p{N}_])", F)

    // Speech often comes as words: "через пять минут" -> "через 5 минут".
    fun numbersToDigits(s: String): String {
        val m = numberWords.matcher(s)
        val sb = StringBuffer()
        while (m.find()) {
            val n = if (m.group("tens") != null)
                tens.getValue(m.group("tens").lowercase()) + (m.group("unit1")?.let { units.getValue(it.lowercase()) } ?: 0)
            else units.getValue(m.group("unit2").lowercase())
            m.appendReplacement(sb, n.toString())
        }
        m.appendTail(sb)
        return sb.toString()
    }

    private fun findClock(s: String): Matcher? {
        val full = clockFull.matcher(s)
        if (full.find()) return full
        val short = clockShort.matcher(s)
        return if (short.find()) short else null
    }

    // Returns the total and where the duration ends, or null when there is none at pos.
    private fun readDuration(s: String, pos: Int): Pair<Duration, Int>? {
        var total = Duration.ZERO
        var end = pos
        var any = false
        val m = durPart.matcher(s)
        while (end < s.length) {
            m.region(end, s.length)
            if (!m.lookingAt() || m.end() == end) break
            total = total.plus(partValue(m))
            end = m.end()
            any = true
        }
        return if (any) total to end else null
    }

    private fun partValue(m: Matcher): Duration {
        m.group("word")?.let { w ->
            return when (w.lowercase().replace(Regex("\\s+"), " ")) {
                "полчаса" -> Duration.ofMinutes(30)
                "полминуты" -> Duration.ofSeconds(30)
                "полтора часа" -> Duration.ofMinutes(90)
                "пару минут" -> Duration.ofMinutes(2)
                "минуту" -> Duration.ofMinutes(1)
                "час" -> Duration.ofHours(1)
                else -> Duration.ofDays(1)
            }
        }
        val n = m.group("num").replace(',', '.').toDouble()
        val unit = m.group("unit").lowercase()
        val seconds = when {
            unit.startsWith("с") -> n
            unit.startsWith("м") -> n * 60
            unit.startsWith("ч") -> n * 3600
            else -> n * 86400
        }
        return Duration.ofSeconds(seconds.toLong())
    }

    private fun cut(s: String, from: Int, to: Int) = (s.substring(0, from) + " " + s.substring(to)).trim()

    private fun clean(input: String): String {
        var s = input.replace(Regex("\\s+"), " ").trim()
        s = filler.matcher(s).replaceFirst("").trim(' ', ',', '.', '-', '—', ':')
        if (s.isEmpty()) return "Напоминание"
        return s.substring(0, 1).uppercase() + s.substring(1)
    }

    fun describe(at: LocalDateTime, now: LocalDateTime): String {
        val hm = "%02d:%02d".format(at.hour, at.minute)
        val whenText = when (at.toLocalDate()) {
            now.toLocalDate() -> "сегодня в $hm"
            now.toLocalDate().plusDays(1) -> "завтра в $hm"
            else -> "%02d.%02d в $hm".format(at.dayOfMonth, at.monthValue)
        }
        val left = Duration.between(now, at)
        val inText = when {
            left.toMinutes() < 1 -> "${maxOf(1, left.seconds)} сек"
            left.toHours() < 1 -> "${Math.round(left.seconds / 60.0)} мин"
            left.toDays() < 1 -> "${left.toHours()} ч ${left.toMinutes() % 60} мин"
            else -> "${left.toDays()} дн"
        }
        return "$whenText (через $inText)"
    }
}
