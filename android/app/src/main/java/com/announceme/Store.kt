package com.announceme

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.util.UUID

// Days use java.time numbering mod 7 to match the interface: 0 = Sunday … 6 = Saturday.
class Site(
    var id: String = newId(),
    var url: String = "",
    var name: String = "",
    var title: String = "",
    var text: String = "",
    var enabled: Boolean = true,
    var days: MutableList<Int> = mutableListOf(0, 1, 2, 3, 4, 5, 6),
    var times: MutableList<String> = mutableListOf("09:00"),
    var iconFile: String = "",
    var rev: Int = 0,
    var templateId: String = "",
    var iconSite: String = "",
    var lastFired: Long = System.currentTimeMillis(),
) {
    val isWebLink get() = url.startsWith("http://", true) || url.startsWith("https://", true)

    val host: String
        get() = runCatching { java.net.URI(url).host?.removePrefix("www.") }.getOrNull() ?: url

    val displayName: String
        get() = when {
            name.isNotBlank() -> name
            isWebLink -> host
            else -> "Напоминание"
        }

    fun toJson() = JSONObject().apply {
        put("id", id); put("url", url); put("name", name); put("title", title); put("text", text)
        put("enabled", enabled); put("days", JSONArray(days)); put("times", JSONArray(times))
        put("iconFile", iconFile); put("rev", rev); put("templateId", templateId)
        put("iconSite", iconSite); put("lastFired", lastFired)
    }

    companion object {
        fun fromJson(o: JSONObject) = Site(
            id = o.optString("id", newId()),
            url = o.optString("url"),
            name = o.optString("name"),
            title = o.optString("title"),
            text = o.optString("text"),
            enabled = o.optBoolean("enabled", true),
            days = o.optJSONArray("days").ints(),
            times = o.optJSONArray("times").strings(),
            iconFile = o.optString("iconFile"),
            rev = o.optInt("rev"),
            templateId = o.optString("templateId"),
            iconSite = o.optString("iconSite"),
            lastFired = o.optLong("lastFired", System.currentTimeMillis()),
        )
    }
}

class Reminder(
    var id: String = newId(),
    var at: Long = 0,
    var created: Long = System.currentTimeMillis(),
    var text: String = "",
) {
    fun toJson() = JSONObject().apply { put("id", id); put("at", at); put("created", created); put("text", text) }

    companion object {
        fun fromJson(o: JSONObject) =
            Reminder(o.optString("id", newId()), o.optLong("at"), o.optLong("created"), o.optString("text"))
    }
}

fun newId() = UUID.randomUUID().toString().replace("-", "").take(12)

fun JSONArray?.ints(): MutableList<Int> = MutableList(this?.length() ?: 0) { this!!.getInt(it) }
fun JSONArray?.strings(): MutableList<String> = MutableList(this?.length() ?: 0) { this!!.getString(it) }

object Store {
    // Every read-modify-write of the data runs on this one thread.
    val worker: java.util.concurrent.ExecutorService = java.util.concurrent.Executors.newSingleThreadExecutor()

    val sites = mutableListOf<Site>()
    val reminders = mutableListOf<Reminder>()
    var paused = false
    var catchUp = true
    private var loaded = false
    private lateinit var file: File
    lateinit var iconsDir: File
        private set

    @Synchronized
    fun load(ctx: Context) {
        if (loaded) return
        file = File(ctx.filesDir, "settings.json")
        iconsDir = File(ctx.filesDir, "icons").apply { mkdirs() }
        if (file.exists()) {
            runCatching {
                val o = JSONObject(file.readText())
                o.optJSONArray("sites")?.let { a -> for (i in 0 until a.length()) sites += Site.fromJson(a.getJSONObject(i)) }
                o.optJSONArray("reminders")?.let { a -> for (i in 0 until a.length()) reminders += Reminder.fromJson(a.getJSONObject(i)) }
                paused = o.optBoolean("paused")
                catchUp = o.optBoolean("catchUp", true)
            }
        } else {
            sites += Templates.all.first { it.id == "chesscom" }.toSite()
            loaded = true
            save()
        }
        loaded = true
        // Icons left behind by replaced icons or cancelled previews.
        val used = sites.map { it.iconFile }.toSet()
        iconsDir.listFiles()?.filter { it.absolutePath !in used }?.forEach { it.delete() }
    }

    @Synchronized
    fun save() {
        val o = JSONObject().apply {
            put("sites", JSONArray(sites.map { it.toJson() }))
            put("reminders", JSONArray(reminders.map { it.toJson() }))
            put("paused", paused)
            put("catchUp", catchUp)
        }
        val tmp = File(file.parentFile, "settings.json.tmp")
        tmp.writeText(o.toString(2))
        tmp.renameTo(file)
    }
}
