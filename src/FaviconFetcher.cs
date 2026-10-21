using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Text.RegularExpressions;

namespace AnnounceMe;

public static class FaviconFetcher
{
    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
        return c;
    }

    public static async Task EnsureIconAsync(SiteEntry e)
    {
        if (File.Exists(e.IconFile)) return;
        var bmp = await FetchForAsync(e.Url, e.IconSite);
        if (bmp == null && string.IsNullOrWhiteSpace(e.Url) && string.IsNullOrWhiteSpace(e.IconSite))
        {
            // Nothing to take an icon from: use the app's bell.
            using var s = typeof(FaviconFetcher).Assembly.GetManifestResourceStream("app.png");
            using var img = Image.FromStream(s);
            bmp = new Bitmap(img);
        }
        if (bmp == null) return;
        using (bmp)
        {
            e.Rev++;
            SaveIcon(e, bmp);
        }
        Store.Save();
    }

    // Writes the icon as a 256x256 PNG and points the entry at it.
    public static void SaveIcon(SiteEntry e, Image img)
    {
        Directory.CreateDirectory(Store.IconsDir);
        var path = Path.Combine(Store.IconsDir, $"{e.Id}_r{e.Rev}.png");
        using (var square = ToSquare(img, 256))
            square.Save(path, ImageFormat.Png);
        e.IconFile = path;
    }

    public static Bitmap ToSquare(Image img, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.InterpolationMode = img.Width < size / 4 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        float scale = Math.Min((float)size / img.Width, (float)size / img.Height);
        float w = img.Width * scale, h = img.Height * scale;
        g.DrawImage(img, (size - w) / 2, (size - h) / 2, w, h);
        return bmp;
    }

    // The icon for whatever an entry points to: the icon page, a web link, or a program on disk.
    public static async Task<Bitmap> FetchForAsync(string url, string iconSite)
    {
        if (!string.IsNullOrWhiteSpace(iconSite)) return await FetchAsync(iconSite);
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return await FetchAsync(url);
        return FromFile(url);
    }

    public static Bitmap FromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var icon = Icon.ExtractAssociatedIcon(path);
            return icon?.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    // Tries the icons the page declares, the usual well-known paths and a favicon service,
    // and returns the biggest one that decodes.
    public static async Task<Bitmap> FetchAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var page)) return null;
        var origin = new Uri(page.GetLeftPart(UriPartial.Authority));

        var candidates = new List<Uri>();
        try
        {
            var html = await Http.GetStringAsync(page);
            var baseUri = page;
            foreach (Match m in Regex.Matches(html, @"<link\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var tag = m.Value;
                var rel = Attr(tag, "rel");
                var href = Attr(tag, "href");
                if (rel == null || href == null || !rel.Contains("icon", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || href.StartsWith("data:")) continue;
                if (Uri.TryCreate(baseUri, WebUtility.HtmlDecode(href), out var u)) candidates.Add(u);
            }
        }
        catch { }

        candidates.Add(new Uri(origin, "/apple-touch-icon.png"));
        candidates.Add(new Uri(origin, "/favicon.ico"));
        var host = page.Host;
        candidates.Add(new Uri($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=256"));
        candidates.Add(new Uri($"https://icons.duckduckgo.com/ip3/{Uri.EscapeDataString(host)}.ico"));

        var results = await Task.WhenAll(candidates.Distinct().Select(TryDownload));
        var best = results.Where(b => b != null).OrderByDescending(b => b.Width * b.Height).FirstOrDefault();
        foreach (var b in results)
            if (b != null && b != best) b.Dispose();
        return best;
    }

    static string Attr(string tag, string name)
    {
        var m = Regex.Match(tag, $@"\b{name}\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }

    static async Task<Bitmap> TryDownload(Uri u)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(u);
            return Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    public static Bitmap Decode(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 8) return null;
        try
        {
            // .ico: ask for the largest frame
            if (bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0)
            {
                using var ms = new MemoryStream(bytes);
                using var icon = new Icon(ms, 256, 256);
                return icon.ToBitmap();
            }
            using var s = new MemoryStream(bytes);
            using var img = Image.FromStream(s);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }
}
