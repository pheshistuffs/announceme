package com.announceme

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.RectF
import android.util.Base64
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.util.concurrent.Callable
import java.util.concurrent.Executors

// Finds a site's icon: the icons the page declares, the usual paths and a favicon service,
// keeping the biggest one that decodes. Blocking: call off the main thread.
object Favicon {
    private const val UA = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36"
    private val pool = Executors.newFixedThreadPool(4)

    fun fetchFor(url: String, iconSite: String): Bitmap? = when {
        iconSite.isNotBlank() -> fetch(iconSite)
        url.startsWith("http://", true) || url.startsWith("https://", true) -> fetch(url)
        else -> null
    }

    fun fetch(url: String): Bitmap? {
        val page = runCatching { URI(url) }.getOrNull() ?: return null
        val host = page.host ?: return null
        val origin = "${page.scheme}://${page.authority}"
        val candidates = linkedSetOf<String>()
        runCatching {
            val html = String(download(url) ?: ByteArray(0))
            Regex("<link\\b[^>]*>", RegexOption.IGNORE_CASE).findAll(html).forEach { m ->
                val tag = m.value
                val rel = attr(tag, "rel") ?: return@forEach
                val href = attr(tag, "href") ?: return@forEach
                if (!rel.contains("icon", true) || href.endsWith(".svg", true) || href.startsWith("data:")) return@forEach
                candidates += page.resolve(href.replace("&amp;", "&")).toString()
            }
        }
        candidates += "$origin/apple-touch-icon.png"
        candidates += "$origin/favicon.ico"
        candidates += "https://www.google.com/s2/favicons?domain=$host&sz=256"
        candidates += "https://icons.duckduckgo.com/ip3/$host.ico"

        val results = pool.invokeAll(candidates.map { c -> Callable { download(c)?.let { decode(it) } } })
            .mapNotNull { runCatching { it.get() }.getOrNull() }
        val best = results.maxByOrNull { it.width * it.height }
        results.filter { it !== best }.forEach { it.recycle() }
        return best
    }

    private fun attr(tag: String, name: String): String? {
        val m = Regex("\\b$name\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOption.IGNORE_CASE).find(tag) ?: return null
        return m.groupValues.drop(1).firstOrNull { it.isNotEmpty() }
    }

    private fun download(url: String): ByteArray? = runCatching {
        val c = URL(url).openConnection() as HttpURLConnection
        c.connectTimeout = 8000
        c.readTimeout = 8000
        c.instanceFollowRedirects = true
        c.setRequestProperty("User-Agent", UA)
        c.setRequestProperty("Accept-Language", "ru,en;q=0.8")
        try {
            if (c.responseCode !in 200..299) null else c.inputStream.use { it.readBytes() }
        } finally {
            c.disconnect()
        }
    }.getOrNull()

    fun decode(bytes: ByteArray): Bitmap? = runCatching { BitmapFactory.decodeByteArray(bytes, 0, bytes.size) }.getOrNull()

    fun square(src: Bitmap, size: Int): Bitmap {
        val out = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
        val scale = minOf(size.toFloat() / src.width, size.toFloat() / src.height)
        val w = src.width * scale
        val h = src.height * scale
        val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG).apply { isFilterBitmap = src.width >= size / 4 }
        Canvas(out).drawBitmap(src, null, RectF((size - w) / 2, (size - h) / 2, (size + w) / 2, (size + h) / 2), paint)
        return out
    }

    fun save(site: Site, bmp: Bitmap) {
        val file = File(Store.iconsDir, "${site.id}_r${site.rev}.png")
        square(bmp, 192).let { sq -> file.outputStream().use { sq.compress(Bitmap.CompressFormat.PNG, 100, it) }; sq.recycle() }
        site.iconFile = file.absolutePath
    }

    fun ensure(site: Site) {
        if (site.iconFile.isNotEmpty() && File(site.iconFile).exists()) return
        val bmp = fetchFor(site.url, site.iconSite) ?: return
        site.rev++
        save(site, bmp)
        bmp.recycle()
        Store.save()
    }

    fun load(site: Site): Bitmap? =
        if (site.iconFile.isNotEmpty() && File(site.iconFile).exists()) BitmapFactory.decodeFile(site.iconFile) else null

    fun dataUrl(bmp: Bitmap, size: Int = 96): String {
        val sq = square(bmp, size)
        val out = ByteArrayOutputStream()
        sq.compress(Bitmap.CompressFormat.PNG, 100, out)
        sq.recycle()
        return "data:image/png;base64," + Base64.encodeToString(out.toByteArray(), Base64.NO_WRAP)
    }
}
