package com.announceme

import android.app.AlarmManager
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import java.time.LocalDateTime

// Android always shows the app's name in a notification's header; the site's name goes next
// to it (subText) and its icon is the large picture on the right.
object Notifier {
    private const val DAILY = "daily"
    private const val QUICK = "quick"

    fun channels(ctx: Context) {
        val nm = ctx.getSystemService(NotificationManager::class.java)!!
        nm.createNotificationChannel(NotificationChannel(DAILY, "Ежедневные", NotificationManager.IMPORTANCE_DEFAULT).apply {
            description = "Сайты, игры и свои напоминания по расписанию"
        })
        nm.createNotificationChannel(NotificationChannel(QUICK, "Быстрые напоминания", NotificationManager.IMPORTANCE_HIGH).apply {
            description = "«Через 5 минут достать курицу» и подобные"
        })
    }

    fun showSite(ctx: Context, s: Site) {
        channels(ctx)
        val lines = listOf(s.title, s.text).filter { it.isNotBlank() }
        val title = if (lines.size == 2) lines[0] else s.displayName
        val body = lines.lastOrNull() ?: "Пора заглянуть: ${s.displayName}"
        val b = Notification.Builder(ctx, DAILY)
            .setSmallIcon(R.drawable.ic_bell)
            .setSubText(s.displayName)
            .setContentTitle(title)
            .setContentText(body)
            .setStyle(Notification.BigTextStyle().bigText(body))
            .setAutoCancel(true)
            .setColor(0xFF2563EB.toInt())
        Favicon.load(s)?.let { b.setLargeIcon(it) }
        openIntent(ctx, s)?.let { pi ->
            b.setContentIntent(pi)
            b.addAction(Notification.Action.Builder(null, "Открыть", pi).build())
        }
        ctx.getSystemService(NotificationManager::class.java)!!.notify(s.id.hashCode(), b.build())
    }

    private fun openIntent(ctx: Context, s: Site): PendingIntent? {
        if (s.url.isBlank()) return null
        val view = Intent(Intent.ACTION_VIEW, Uri.parse(s.url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        return PendingIntent.getActivity(ctx, s.id.hashCode(), view, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    }

    fun showReminder(ctx: Context, r: Reminder) {
        channels(ctx)
        val at = r.at.toLocal()
        val id = r.id.hashCode()
        fun snooze(min: Int) = PendingIntent.getBroadcast(
            ctx, id * 31 + min,
            Intent(ctx, AlarmReceiver::class.java).setAction(AlarmReceiver.SNOOZE)
                .putExtra("text", r.text).putExtra("min", min).putExtra("nid", id),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val open = PendingIntent.getActivity(ctx, id, Intent(ctx, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        val n = Notification.Builder(ctx, QUICK)
            .setSmallIcon(R.drawable.ic_bell)
            .setSubText("Напоминание")
            .setContentTitle(r.text)
            .setContentText("На %02d:%02d".format(at.hour, at.minute))
            .setCategory(Notification.CATEGORY_REMINDER)
            .setContentIntent(open)
            .setAutoCancel(true)
            .setColor(0xFF2563EB.toInt())
            .addAction(Notification.Action.Builder(null, "Ещё 5 мин", snooze(5)).build())
            .addAction(Notification.Action.Builder(null, "Ещё 15 мин", snooze(15)).build())
            .build()
        ctx.getSystemService(NotificationManager::class.java)!!.notify(id, n)
    }
}

// One alarm for the nearest moment; when it rings, everything due is shown and the next one is set.
object Alarms {
    fun check(ctx: Context) {
        Store.load(ctx)
        val now = LocalDateTime.now()
        val nowMs = System.currentTimeMillis()

        val dueReminders = Store.reminders.filter { it.at <= nowMs }
        if (dueReminders.isNotEmpty()) {
            Store.reminders.removeAll(dueReminders.toSet())
            Store.save()
            dueReminders.forEach { Notifier.showReminder(ctx, it) }
        }
        if (!Store.paused) {
            val due = Schedule.due(now)
            if (due.isNotEmpty()) {
                due.forEach { it.lastFired = nowMs }
                Store.save()
                due.forEach { s ->
                    runCatching { Favicon.ensure(s) }
                    Notifier.showSite(ctx, s)
                }
            }
        }
        scheduleNext(ctx)
    }

    fun scheduleNext(ctx: Context) {
        Store.load(ctx)
        runCatching { QuickWidget.refresh(ctx) }
        val now = LocalDateTime.now()
        val times = mutableListOf<Long>()
        Store.reminders.mapTo(times) { it.at }
        if (!Store.paused)
            Store.sites.filter { it.enabled }.mapNotNullTo(times) { Schedule.nextOccurrence(it, now)?.toMillis() }
        val am = ctx.getSystemService(AlarmManager::class.java)!!
        val pi = PendingIntent.getBroadcast(ctx, 0, Intent(ctx, AlarmReceiver::class.java).setAction(AlarmReceiver.TICK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        val next = times.minOrNull()
        if (next == null) {
            am.cancel(pi)
            return
        }
        val exact = Build.VERSION.SDK_INT < Build.VERSION_CODES.S || am.canScheduleExactAlarms()
        if (exact) am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, next, pi)
        else am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, next, pi)
    }
}

class AlarmReceiver : BroadcastReceiver() {
    companion object {
        const val TICK = "com.announceme.TICK"
        const val SNOOZE = "com.announceme.SNOOZE"
    }

    override fun onReceive(ctx: Context, intent: Intent) {
        val pending = goAsync()
        Store.worker.execute {
            try {
                Store.load(ctx)
                if (intent.action == SNOOZE) {
                    ctx.getSystemService(NotificationManager::class.java)!!.cancel(intent.getIntExtra("nid", 0))
                    val min = intent.getIntExtra("min", 5)
                    Store.reminders += Reminder(at = System.currentTimeMillis() + min * 60_000L, text = intent.getStringExtra("text") ?: "Напоминание")
                    Store.save()
                }
                Alarms.check(ctx)
                MainActivity.current?.pushState()
            } finally {
                pending.finish()
            }
        }
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(ctx: Context, intent: Intent) {
        val pending = goAsync()
        Store.worker.execute {
            try { Alarms.check(ctx) } finally { pending.finish() }
        }
    }
}
