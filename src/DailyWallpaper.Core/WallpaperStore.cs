using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DailyWallpaper;

public sealed record Wallpaper(
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("url")] Uri Url,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("desc")] string Description);

public sealed record CachedWallpaper(Wallpaper Wallpaper, string OriginalName, string DarkName,
    DateTimeOffset RefreshedAt)
{
    public string ImagePath(string directory, bool dark) =>
        Path.Combine(directory, dark ? DarkName : OriginalName);
}

public interface IImageRenderer
{
    byte[] CreateDarkJpeg(byte[] original);
    bool IsValid(string path);
}

public static class Schedule
{
    public static bool IsDue(DateTimeOffset? last, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (last is null) return true;
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var scheduled = local.Date.AddHours(9);
        return local.DateTime >= scheduled &&
            TimeZoneInfo.ConvertTime(last.Value, zone).DateTime < scheduled;
    }
}

public static class DarkTone
{
    public static double ToLinear(byte value)
    {
        var c = value / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    public static byte ToSrgb(double value)
    {
        var c = value <= 0.0031308 ? 12.92 * value : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp((int)Math.Round(c * 255), 0, 255);
    }

    public static (byte R, byte G, byte B) Apply(byte r, byte g, byte b)
    {
        var lr = ToLinear(r);
        var lg = ToLinear(g);
        var lb = ToLinear(b);
        var luminance = 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
        // 在线性光空间降低曝光；按亮度压制高光，避免各通道分别处理造成偏色。
        var highlight = Math.Clamp((luminance - 0.25) / 0.75, 0, 1);
        var gain = Math.Pow(2, -0.65) * (1 - 0.35 * highlight * highlight);
        return (ToSrgb(lr * gain), ToSrgb(lg * gain), ToSrgb(lb * gain));
    }
}

public sealed class WallpaperStore(string directory, IImageRenderer renderer, HttpClient client)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string DirectoryPath => directory;
    private string Manifest => Path.Combine(directory, "current.json");

    public CachedWallpaper? Load()
    {
        try
        {
            var cached = JsonSerializer.Deserialize<CachedWallpaper>(File.ReadAllBytes(Manifest), Json);
            if (cached?.Wallpaper?.Url is null || !SafeName(cached.OriginalName) || !SafeName(cached.DarkName))
                return null;
            return renderer.IsValid(cached.ImagePath(directory, false)) &&
                renderer.IsValid(cached.ImagePath(directory, true)) ? cached : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static bool SafeName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        name.IndexOfAny(['/', '\\', ':']) < 0 && name != "." && name != ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    public async Task<CachedWallpaper> FetchAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        var date = now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var metadata = await DownloadAsync(new Uri($"https://bing.wdbyte.com/today?date={date}"), cancellation);
        var wallpaper = JsonSerializer.Deserialize<Wallpaper>(metadata) ?? throw new InvalidDataException("壁纸信息为空");
        if (wallpaper.Url is null || !wallpaper.Url.IsAbsoluteUri || wallpaper.Url.Scheme != "https")
            throw new InvalidDataException("壁纸地址必须使用 HTTPS");
        var original = await DownloadAsync(wallpaper.Url, cancellation);
        return await Task.Run(() => Prepare(wallpaper, original, now, cancellation), cancellation);
    }

    private async Task<byte[]> DownloadAsync(Uri url, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await client.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellation);
    }

    public CachedWallpaper Prepare(Wallpaper wallpaper, byte[] original, DateTimeOffset now,
        CancellationToken cancellation = default)
    {
        var dark = renderer.CreateDarkJpeg(original);
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var extension = wallpaper.Url.AbsolutePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var cached = new CachedWallpaper(wallpaper, id + "-original" + extension, id + "-dark.jpg", now);
        try
        {
            // 唯一文件名避免桌面沿用旧图片缓存；最后提交清单才能保留失败前的有效壁纸。
            File.WriteAllBytes(cached.ImagePath(directory, false), original);
            File.WriteAllBytes(cached.ImagePath(directory, true), dark);
            cancellation.ThrowIfCancellationRequested();
            var temporary = Manifest + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(cached, Json));
                File.Move(temporary, Manifest, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return cached;
        }
        catch
        {
            foreach (var path in new[] { cached.ImagePath(directory, false), cached.ImagePath(directory, true) })
                try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }
}
