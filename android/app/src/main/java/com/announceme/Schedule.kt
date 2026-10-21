package com.announceme

import java.time.Duration
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneId

object Schedule {
    val weekOrder = listOf(1, 2, 3, 4, 5, 6, 0)
    private val shortNames = mapOf(1 to "Пн", 2 to "Вт", 3 to "Ср", 4 to "Чт", 5 to "Пт", 6 to "Сб", 0 to "Вс")

    fun shortName(day: Int) = shortNames.getValue(day)

    // 0 = Sunday … 6 = Saturday, like the interface and .NET.
    fun dayIndex(d: LocalDate) = d.dayOfWeek.value % 7

    fun parseTime(t: String): LocalTime? = runCatching {
        val (h, m) = t.trim().replace('.', ':').split(":").map { it.toInt() }
        if (h in 0..23 && m in 0..59) LocalTime.of(h, m) else null
    }.getOrNull()

    private fun times(s: Site) = s.times.mapNotNull(::parseTime)

    fun lastOccurrence(s: Site, now: LocalDateTime): LocalDateTime? {
        val times = times(s).sortedDescending()
        if (times.isEmpty()) return null
        for (back in 0..7) {
            val date = now.toLocalDate().minusDays(back.toLong())
            if (dayIndex(date) !in s.days) continue
            times.map { date.atTime(it) }.firstOrNull { !it.isAfter(now) }?.let { return it }
        }
        return null
    }

    fun nextOccurrence(s: Site, now: LocalDateTime): LocalDateTime? {
        val times = times(s).sorted()
        if (times.isEmpty()) return null
        for (ahead in 0..7) {
            val date = now.toLocalDate().plusDays(ahead.toLong())
            if (dayIndex(date) !in s.days) continue
            times.map { date.atTime(it) }.firstOrNull { it.isAfter(now) }?.let { return it }
        }
        return null
    }

    fun due(now: LocalDateTime): List<Site> {
        val window = if (Store.catchUp) Duration.ofHours(12) else Duration.ofMinutes(5)
        return Store.sites.filter { s ->
            if (!s.enabled) return@filter false
            val occ = lastOccurrence(s, now) ?: return@filter false
            occ.toMillis() > s.lastFired && Duration.between(occ, now) <= window
        }
    }

    fun scheduleText(s: Site): String {
        val times = times(s).sorted().joinToString(", ") { "%02d:%02d".format(it.hour, it.minute) }
        val days = when {
            s.days.size == 7 -> "каждый день"
            s.days.size == 5 && 6 !in s.days && 0 !in s.days -> "по будням"
            s.days.isEmpty() -> "никогда"
            else -> weekOrder.filter { it in s.days }.joinToString(", ") { shortName(it) }
        }
        return "$days, $times"
    }

    fun whenText(at: LocalDateTime, now: LocalDateTime): String {
        val hm = "%02d:%02d".format(at.hour, at.minute)
        return when (at.toLocalDate()) {
            now.toLocalDate() -> "сегодня $hm"
            now.toLocalDate().plusDays(1) -> "завтра $hm"
            else -> "${shortName(dayIndex(at.toLocalDate()))} %02d.%02d, $hm".format(at.dayOfMonth, at.monthValue)
        }
    }
}

fun LocalDateTime.toMillis() = atZone(ZoneId.systemDefault()).toInstant().toEpochMilli()
fun Long.toLocal(): LocalDateTime = LocalDateTime.ofInstant(java.time.Instant.ofEpochMilli(this), ZoneId.systemDefault())
