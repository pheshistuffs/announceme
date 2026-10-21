package com.announceme

import android.app.Activity
import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.speech.RecognizerIntent
import android.widget.RemoteViews
import android.widget.Toast
import java.time.LocalDateTime

object Quick {
    // Turns a phrase into a quick reminder. Returns what was understood, or null.
    fun add(ctx: Context, phrase: String): QuickParser.Result? {
        Store.load(ctx)
        val r = QuickParser.parse(phrase, LocalDateTime.now()) ?: return null
        Store.reminders += Reminder(at = r.at.toMillis(), text = r.text)
        Store.save()
        Alarms.scheduleNext(ctx)
        MainActivity.current?.pushState()
        return r
    }

    fun speechIntent(): Intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH)
        .putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
        .putExtra(RecognizerIntent.EXTRA_LANGUAGE, "ru-RU")
        .putExtra(RecognizerIntent.EXTRA_PROMPT, "Например: «через 10 минут выключить плиту»")
        .putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 5)

    // Speech gives several guesses; take the first one that names a time.
    fun bestPhrase(data: Intent?): Pair<String, QuickParser.Result?>? {
        val guesses = data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)?.filter { it.isNotBlank() }
        if (guesses.isNullOrEmpty()) return null
        val now = LocalDateTime.now()
        for (g in guesses) QuickParser.parse(g, now)?.let { return g to it }
        return guesses[0] to null
    }
}

// Opened by the widget's microphone: asks for speech and sets the reminder, with no screen of its own.
class VoiceActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (savedInstanceState != null) return
        try {
            startActivityForResult(Quick.speechIntent(), 1)
        } catch (e: Exception) {
            Toast.makeText(this, "На телефоне нет распознавания речи (нужно приложение Google)", Toast.LENGTH_LONG).show()
            finish()
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        val best = if (resultCode == RESULT_OK) Quick.bestPhrase(data) else null
        if (best == null) {
            finish()
            return
        }
        val (phrase, _) = best
        val app = applicationContext
        Store.worker.execute {
            val r = Quick.add(app, phrase)
            runOnUiThread {
                if (r != null) {
                    Toast.makeText(app, "Напомню ${QuickParser.describe(r.at, LocalDateTime.now())}: ${r.text}", Toast.LENGTH_LONG).show()
                } else {
                    // No time in the phrase: open the app with the words already typed in.
                    Toast.makeText(app, "Не услышал время: «$phrase»", Toast.LENGTH_LONG).show()
                    startActivity(Intent(app, MainActivity::class.java)
                        .putExtra(MainActivity.EXTRA_QUICK_TEXT, phrase)
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP))
                }
                finish()
            }
        }
    }
}

class QuickWidget : AppWidgetProvider() {
    companion object {
        fun refresh(ctx: Context) {
            val mgr = AppWidgetManager.getInstance(ctx)
            val ids = mgr.getAppWidgetIds(ComponentName(ctx, QuickWidget::class.java))
            if (ids.isNotEmpty()) update(ctx, mgr, ids)
        }

        private fun update(ctx: Context, mgr: AppWidgetManager, ids: IntArray) {
            Store.load(ctx)
            val next = Store.reminders.minByOrNull { it.at }
            val views = RemoteViews(ctx.packageName, R.layout.widget_quick)
            val voice = PendingIntent.getActivity(ctx, 1,
                Intent(ctx, VoiceActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            val open = PendingIntent.getActivity(ctx, 2, Intent(ctx, MainActivity::class.java),
                PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            views.setOnClickPendingIntent(R.id.widget_mic, voice)
            views.setOnClickPendingIntent(R.id.widget_text, open)
            if (next != null) {
                val at = next.at.toLocal()
                views.setTextViewText(R.id.widget_title, next.text)
                views.setTextViewText(R.id.widget_sub, "в %02d:%02d · ещё %d".format(at.hour, at.minute, Store.reminders.size))
            } else {
                views.setTextViewText(R.id.widget_title, "Напомнить голосом")
                views.setTextViewText(R.id.widget_sub, "«через 10 минут выключить плиту»")
            }
            mgr.updateAppWidget(ids, views)
        }
    }

    override fun onUpdate(ctx: Context, mgr: AppWidgetManager, ids: IntArray) = update(ctx, mgr, ids)
}
