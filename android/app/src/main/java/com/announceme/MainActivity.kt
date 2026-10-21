package com.announceme

import android.Manifest
import android.annotation.SuppressLint
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.webkit.JavascriptInterface
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import org.json.JSONArray
import org.json.JSONObject
import java.time.LocalDateTime
import java.util.UUID
import kotlin.concurrent.thread

// The interface is the same ui.html as on Windows, in a WebView. The page calls
// Android.call('{"id","cmd","args"}'); answers and state go back through window.onHostMessage.
class MainActivity : Activity() {
    companion object {
        @Volatile var current: MainActivity? = null
        private const val PICK_ICON = 1
    }

    private lateinit var web: WebView
    private val pendingIcons = HashMap<String, Bitmap>()
    private val iconCache = HashMap<String, String>()
    private var pickIconReply: Int? = null

    @SuppressLint("SetJavaScriptEnabled")
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        Store.load(this)
        Notifier.channels(this)
        web = WebView(this).apply {
            setBackgroundColor(Color.parseColor("#161616"))
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true
            settings.allowFileAccess = true
            addJavascriptInterface(Bridge(), "Android")
            webViewClient = object : WebViewClient() {
                // Links open in the browser, never inside the app.
                override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                    if (request.url.scheme == "file") return false
                    openUri(request.url.toString())
                    return true
                }
            }
            loadUrl("file:///android_asset/ui.html")
        }
        setContentView(web)

        if (Build.VERSION.SDK_INT >= 33 && checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED)
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 0)
        Store.worker.execute { Alarms.check(this) }
    }

    override fun onResume() {
        super.onResume()
        current = this
        pushState()
    }

    override fun onPause() {
        super.onPause()
        if (current === this) current = null
    }

    fun pushState() {
        Store.worker.execute {
            val msg = JSONObject().put("type", "state").put("data", buildState())
            post(msg)
        }
    }

    private fun post(msg: JSONObject) {
        val js = "window.onHostMessage && window.onHostMessage(${msg})"
        runOnUiThread { if (!isDestroyed) web.evaluateJavascript(js, null) }
    }

    private fun iconData(s: Site): Any {
        if (s.iconFile.isEmpty()) return JSONObject.NULL
        iconCache[s.iconFile]?.let { return it }
        val bmp = Favicon.load(s) ?: return JSONObject.NULL
        val url = Favicon.dataUrl(bmp)
        bmp.recycle()
        iconCache[s.iconFile] = url
        return url
    }

    private fun kindOf(s: Site): String {
        val t = Templates.all.firstOrNull { it.matches(s) }
        if (t != null) return if (t.group == Templates.HABITS) (if (s.url.isEmpty()) "custom" else "web") else "game"
        if (s.url.isBlank()) return "custom"
        return if (s.isWebLink) "web" else "game"
    }

    private fun buildState(): JSONObject {
        val now = LocalDateTime.now()
        val sites = JSONArray()
        for (s in Store.sites) {
            val next = if (s.enabled && !Store.paused) Schedule.nextOccurrence(s, now) else null
            sites.put(JSONObject().apply {
                put("id", s.id); put("name", s.displayName); put("rawName", s.name)
                put("title", s.title); put("text", s.text); put("url", s.url)
                put("icon", iconData(s)); put("enabled", s.enabled)
                put("days", JSONArray(Schedule.weekOrder.filter { it in s.days }))
                put("times", JSONArray(s.times)); put("schedule", Schedule.scheduleText(s))
                put("kind", kindOf(s))
                put("next", next?.toString() ?: JSONObject.NULL)
                put("nextText", next?.let { Schedule.whenText(it, now) } ?: JSONObject.NULL)
            })
        }
        val reminders = JSONArray()
        for (r in Store.reminders.sortedBy { it.at }) {
            val at = r.at.toLocal()
            reminders.put(JSONObject().apply {
                put("id", r.id); put("text", r.text)
                put("at", at.toString()); put("created", r.created.toLocal().toString())
                put("whenText", Schedule.whenText(at, now))
                put("inText", QuickParser.describe(at, now))
            })
        }
        val templates = JSONArray()
        for (t in Templates.all) templates.put(JSONObject().apply {
            put("id", t.id); put("group", t.group); put("name", t.name); put("text", t.text)
            put("when", t.describe); put("on", Store.sites.any { t.matches(it) })
        })
        return JSONObject().apply {
            put("now", now.toString())
            put("sites", sites)
            put("reminders", reminders)
            put("templates", templates)
            put("settings", JSONObject().put("paused", Store.paused).put("catchUp", Store.catchUp)
                .put("autostart", true).put("hotkey", ""))
        }
    }

    class UserError(message: String) : Exception(message)

    inner class Bridge {
        @JavascriptInterface
        fun call(json: String) {
            val msg = runCatching { JSONObject(json) }.getOrNull() ?: return
            val id = msg.optInt("id")
            val cmd = msg.optString("cmd")
            val args = msg.optJSONObject("args") ?: JSONObject()
            // Network work runs on its own thread; everything touching the data on Store.worker.
            val run = {
                val reply = JSONObject().put("type", "reply").put("id", id)
                try {
                    val data = handle(cmd, args, id)
                    if (data !== Deferred) post(reply.put("ok", true).put("data", data ?: JSONObject.NULL))
                } catch (e: UserError) {
                    post(reply.put("ok", false).put("error", e.message))
                } catch (e: Exception) {
                    post(reply.put("ok", false).put("error", "Что-то пошло не так: ${e.message}"))
                }
            }
            if (cmd == "fetchIcon") thread { run() } else Store.worker.execute(run)
        }
    }

    private object Deferred

    private fun find(a: JSONObject) = Store.sites.firstOrNull { it.id == a.optString("id") } ?: throw UserError("Напоминание не найдено")

    private fun handle(cmd: String, a: JSONObject, replyId: Int): Any? {
        when (cmd) {
            "getState" -> return buildState()

            "saveSite" -> {
                val existing = Store.sites.firstOrNull { it.id == a.optString("id") }
                val entry = existing?.let { Site.fromJson(it.toJson()) } ?: Site()
                fill(entry, a)
                if (existing != null) Store.sites[Store.sites.indexOf(existing)] = entry else Store.sites += entry
                Store.save()
                Alarms.scheduleNext(this)
                pushState()
                if (entry.iconFile.isEmpty()) {
                    runCatching { Favicon.ensure(entry) }
                    pushState()
                }
                return null
            }

            "previewSite" -> {
                val existing = Store.sites.firstOrNull { it.id == a.optString("id") }
                val entry = existing?.let { Site.fromJson(it.toJson()) } ?: Site()
                fill(entry, a, keepPending = true)
                runCatching { Favicon.ensure(entry) }
                Notifier.showSite(this, entry)
                return null
            }

            "deleteSite" -> {
                val s = find(a)
                Store.sites.remove(s)
                Store.save()
                if (s.iconFile.isNotEmpty()) java.io.File(s.iconFile).delete()
                Alarms.scheduleNext(this)
                pushState()
                return null
            }

            "toggleSite" -> {
                val s = find(a)
                s.enabled = a.optBoolean("enabled")
                if (s.enabled) s.lastFired = System.currentTimeMillis()
                Store.save()
                Alarms.scheduleNext(this)
                pushState()
                return null
            }

            "testSite" -> {
                val s = find(a)
                runCatching { Favicon.ensure(s) }
                Notifier.showSite(this, s)
                pushState()
                return null
            }

            "openSite" -> {
                openUri(find(a).url)
                return null
            }

            "fetchIcon" -> {
                val url = normalizeUrl(a.optString("url"))
                val iconSite = Store.sites.firstOrNull { it.id == a.optString("id") }?.iconSite ?: ""
                val bmp = Favicon.fetchFor(url, iconSite) ?: throw UserError("Иконка не нашлась, выберите картинку")
                return pending(bmp)
            }

            "pickIcon" -> {
                pickIconReply = replyId
                runOnUiThread {
                    startActivityForResult(Intent(Intent.ACTION_GET_CONTENT).setType("image/*").addCategory(Intent.CATEGORY_OPENABLE), PICK_ICON)
                }
                return Deferred
            }

            "parseQuick" -> {
                val now = LocalDateTime.now()
                val r = QuickParser.parse(a.optString("text"), now) ?: return null
                return JSONObject().put("when", QuickParser.describe(r.at, now)).put("text", r.text)
            }

            "addQuick" -> {
                var text = a.optString("text").trim()
                val minutes = a.optInt("minutes")
                val now = LocalDateTime.now()
                val at: LocalDateTime
                if (minutes > 0) {
                    QuickParser.parse(text, now)?.let { text = it.text }
                    text = if (text.isEmpty()) "Напоминание" else text.substring(0, 1).uppercase() + text.substring(1)
                    at = now.plusMinutes(minutes.toLong())
                } else {
                    val r = QuickParser.parse(text, now)
                        ?: throw UserError("Не вижу, когда напомнить. Добавьте «через 10 минут» или «в 18:30»")
                    text = r.text
                    at = r.at
                }
                Store.reminders += Reminder(at = at.toMillis(), text = text)
                Store.save()
                Alarms.scheduleNext(this)
                pushState()
                return null
            }

            "deleteReminder" -> {
                Store.reminders.removeAll { it.id == a.optString("id") }
                Store.save()
                Alarms.scheduleNext(this)
                pushState()
                return null
            }

            "setPaused" -> {
                val v = a.optBoolean("value")
                if (v != Store.paused) {
                    Store.paused = v
                    // Don't flood with everything that came due during the pause.
                    if (!v) Store.sites.forEach { it.lastFired = System.currentTimeMillis() }
                    Store.save()
                    Alarms.scheduleNext(this)
                }
                pushState()
                return null
            }

            "setCatchUp" -> {
                Store.catchUp = a.optBoolean("value")
                Store.save()
                pushState()
                return null
            }

            "setTemplate" -> {
                val t = Templates.all.firstOrNull { it.id == a.optString("id") } ?: throw UserError("Нет такого шаблона")
                if (a.optBoolean("on")) {
                    if (Store.sites.none { t.matches(it) }) {
                        val s = t.toSite()
                        Store.sites += s
                        Store.save()
                        pushState()
                        runCatching { Favicon.ensure(s) }
                    }
                } else {
                    Store.sites.filter { t.matches(it) }.forEach { s ->
                        Store.sites.remove(s)
                        if (s.iconFile.isNotEmpty()) java.io.File(s.iconFile).delete()
                    }
                    Store.save()
                }
                Alarms.scheduleNext(this)
                pushState()
                return null
            }

            "notificationSettings" -> {
                startActivity(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, packageName))
                return null
            }

            "openExternal" -> {
                openUri(a.optString("url"))
                return null
            }

            else -> throw UserError("Эта функция есть только в Windows-версии")
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != PICK_ICON) return
        val replyId = pickIconReply ?: return
        pickIconReply = null
        val uri = data?.data
        thread {
            val reply = JSONObject().put("type", "reply").put("id", replyId)
            val bmp = uri?.let { u -> runCatching { contentResolver.openInputStream(u)?.use { Favicon.decode(it.readBytes()) } }.getOrNull() }
            when {
                uri == null -> post(reply.put("ok", true).put("data", JSONObject.NULL))
                bmp == null -> post(reply.put("ok", false).put("error", "Не удалось прочитать картинку"))
                else -> Store.worker.execute { post(reply.put("ok", true).put("data", pending(bmp))) }
            }
        }
    }

    private fun pending(bmp: Bitmap): JSONObject {
        val token = UUID.randomUUID().toString()
        synchronized(pendingIcons) { pendingIcons[token] = bmp }
        return JSONObject().put("token", token).put("dataUrl", Favicon.dataUrl(bmp)).put("size", "${bmp.width}×${bmp.height}")
    }

    // Validates the editor's fields and copies them into the entry.
    private fun fill(entry: Site, a: JSONObject, keepPending: Boolean = false) {
        val url = normalizeUrl(a.optString("url"))
        if (url.isNotEmpty() && runCatching { Uri.parse(url).scheme }.getOrNull().isNullOrEmpty())
            throw UserError("Не понимаю ссылку. Пример: https://www.chess.com/ или steam://rungameid/730")

        var name = a.optString("name").trim()
        if (name.isEmpty() && (url.startsWith("http://") || url.startsWith("https://")))
            name = Uri.parse(url).host?.removePrefix("www.") ?: ""
        if (name.isEmpty()) throw UserError("Укажите название, например «Тренировка» или «Genshin Impact»")

        val times = a.optString("times").split(',', ';', ' ').filter { it.isNotBlank() }.map { t ->
            val lt = Schedule.parseTime(t) ?: throw UserError("Не понимаю время «$t». Пишите как 09:00")
            "%02d:%02d".format(lt.hour, lt.minute)
        }.distinct().sorted()
        if (times.isEmpty()) throw UserError("Укажите хотя бы одно время, например 09:00")

        val days = a.optJSONArray("days").ints().distinct()
        if (days.isEmpty()) throw UserError("Отметьте хотя бы один день")

        val scheduleChanged = entry.times != times || entry.days.sorted() != days.sorted()
        entry.url = url
        entry.title = a.optString("title").trim()
        entry.text = a.optString("text").trim()
        entry.times = times.toMutableList()
        entry.days = days.toMutableList()
        entry.enabled = a.optBoolean("enabled", true)
        if (scheduleChanged) entry.lastFired = System.currentTimeMillis()
        entry.name = name

        val token = a.optString("iconToken")
        val icon = synchronized(pendingIcons) { if (keepPending) pendingIcons[token] else pendingIcons.remove(token) }
        if (icon != null) {
            entry.rev++
            Favicon.save(entry, icon)
            if (!keepPending) icon.recycle()
        }
    }

    private fun normalizeUrl(s: String): String {
        val u = s.trim()
        if (u.isEmpty() || u.contains("://")) return u
        return "https://$u"
    }

    private fun openUri(url: String) {
        if (url.isBlank()) return
        runCatching { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    }

    override fun onDestroy() {
        synchronized(pendingIcons) { pendingIcons.values.forEach { it.recycle() }; pendingIcons.clear() }
        super.onDestroy()
    }
}
