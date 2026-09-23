using DailyWallpaper;
using System.Net;
using System.Text;
using System.Text.Json;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(8), "Test", "Test");
DateTimeOffset At(int day, int hour) => new(2026, 9, day, hour, 0, 0, TimeSpan.FromHours(8));
bool Due(DateTimeOffset? last, DateTimeOffset now) => Schedule.IsDue(last, now, zone);
Assert(Due(null, At(21, 8)), "Empty cache must refresh before 9");
Assert(!Due(At(20, 9), At(21, 8)), "Must wait until 9 with a cache");
Assert(Due(At(20, 9), At(21, 9)), "9:00 boundary");
Assert(Due(At(21, 8), At(21, 9)), "Early download must refresh at 9");
Assert(!Due(At(21, 9), At(21, 18)), "No repeated daily download");
Assert(Due(At(19, 9), At(21, 18)), "Missed update after resume");
Assert(Due(At(20, 9).ToUniversalTime(), At(21, 9).ToUniversalTime()), "Time-zone conversion");
for (var value = 1; value <= 255; value++)
{
    var c = (byte)value;
    var dark = DarkTone.Apply(c, c, c);
    Assert(dark.R <= c && dark.R == dark.G && dark.G == dark.B, "Neutral darkening");
    if (value > 20) Assert(dark.R < c && dark.R > 0, "Preserve visible detail");
}
Assert(DarkTone.Apply(0, 0, 0) == ((byte)0, (byte)0, (byte)0), "Preserve black");
var white = DarkTone.Apply(255,255,255).R;
Assert(DarkTone.ToLinear(white) < Math.Pow(2,-0.65), "Highlights need extra suppression");
var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
try
{
    var handler = new FakeHttp();
    using var client = new HttpClient(handler);
    var renderer = new FakeRenderer();
    var store = new WallpaperStore(directory, renderer, client);
    var wallpaper = new Wallpaper("../../outside.png", new Uri("https://example.com/image.png"), "2026-09-21", "cn", "Test");
    var original = Encoding.UTF8.GetBytes("valid image");
    var cached = store.Prepare(wallpaper, original, At(21, 9));
    Assert(File.ReadAllBytes(cached.ImagePath(directory, false)).SequenceEqual(original), "Original byte preservation");
    Assert(Path.GetFileName(cached.OriginalName) == cached.OriginalName, "Ignore remote file name");
    Assert(store.Load() == cached, "Cache round trip");
    renderer.Reject = true;
    try { store.Prepare(wallpaper, [], At(22,9)); throw new Exception("Invalid image accepted"); }
    catch (InvalidDataException) { }
    renderer.Reject = false;
    Assert(store.Load() == cached, "Failure preserves previous cache");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try { store.Prepare(wallpaper, original, At(22,9), cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { }
    }
    Assert(store.Load() == cached, "Cancellation preserves cache");
    handler.Metadata = JsonSerializer.SerializeToUtf8Bytes(wallpaper);
    handler.Image = original;
    async Task ExpectPending(Wallpaper candidate)
    {
        handler.Metadata = JsonSerializer.SerializeToUtf8Bytes(candidate);
        try { await store.FetchAsync(At(22,9), CancellationToken.None); throw new Exception("Unchanged wallpaper accepted"); }
        catch (WallpaperNotUpdatedException) { }
        Assert(store.Load() == cached, "Pending update preserves cache and refresh timestamp");
        Assert(Directory.GetFiles(directory).Length == 3, "Pending update must not create image files");
    }
    await ExpectPending(wallpaper);
    Assert(handler.Count == 1, "Stale date must skip image download");
    foreach (var invalidDate in new[] { "invalid", "2026-02-30", "" })
    {
        var count = handler.Count;
        handler.Metadata = JsonSerializer.SerializeToUtf8Bytes(wallpaper with { Date = invalidDate });
        try { await store.FetchAsync(At(22,9), CancellationToken.None); throw new Exception("Invalid date accepted"); }
        catch (InvalidDataException) { }
        Assert(handler.Count == count + 1 && store.Load() == cached, "Invalid date skips image and preserves cache");
    }
    var next = wallpaper with { Date = "2026-09-22", Url = new Uri("https://example.com/renamed.png") };
    await ExpectPending(next);
    // A new URL is not proof of new image content; an unchanged URL may serve a new image.
    next = next with { Url = wallpaper.Url };
    handler.Metadata = JsonSerializer.SerializeToUtf8Bytes(next);
    handler.Image = Encoding.UTF8.GetBytes("new image content");
    var beforeFetch = handler.Count;
    var fetched = await store.FetchAsync(At(22,9), CancellationToken.None);
    Assert(handler.Count == beforeFetch + 2 && store.Load() == fetched && fetched != cached, "New date and image accepted even with same URL");
    beforeFetch = handler.Count;
    var repeated = await store.FetchAsync(At(22,10), CancellationToken.None);
    Assert(handler.Count == beforeFetch + 1 && repeated == fetched, "Same date skips image download and preserves refresh timestamp");
    handler.Metadata = JsonSerializer.SerializeToUtf8Bytes(next with { Date = "2026-09-24" });
    handler.Image = Encoding.UTF8.GetBytes("early release image content");
    beforeFetch = handler.Count;
    fetched = await store.FetchAsync(At(23,22), CancellationToken.None);
    Assert(handler.Count == beforeFetch + 2 && store.Load() == fetched && fetched.Wallpaper.Date == "2026-09-24",
        "Early release is downloaded and cached with its published date");
    Assert(fetched.RefreshedAt == At(23,22), "Early release retains actual refresh time");
    beforeFetch = handler.Count;
    repeated = await store.FetchAsync(At(23,23), CancellationToken.None);
    Assert(handler.Count == beforeFetch + 1 && repeated == fetched, "Repeated future date skips image download");
    handler.Metadata = JsonSerializer.SerializeToUtf8Bytes(next with { Date = "2026-09-23" });
    beforeFetch = handler.Count;
    try { await store.FetchAsync(At(23,23), CancellationToken.None); throw new Exception("Date rollback accepted"); }
    catch (WallpaperNotUpdatedException) { }
    Assert(handler.Count == beforeFetch + 1 && store.Load() == fetched, "Earlier server date preserves early release cache");
    handler.Fail = true;
    try { await store.FetchAsync(At(23,9), CancellationToken.None); throw new Exception("HTTP error accepted"); }
    catch (HttpRequestException) { }
    Assert(store.Load() == fetched, "HTTP failure preserves cache");
    File.Delete(fetched.ImagePath(directory, true));
    Assert(store.Load() is null, "Missing dark image rejects cache");
    File.WriteAllText(Path.Combine(directory,"current.json"), "broken json");
    Assert(store.Load() is null, "Corrupt manifest rejects cache");
    File.WriteAllText(Path.Combine(directory,"current.json"), JsonSerializer.Serialize(cached with { OriginalName = "../outside.jpg" }));
    Assert(store.Load() is null, "Manifest traversal rejected");
    Console.WriteLine("PASS: scheduling, time zones, tone mapping, cache, date validation, duplicate images, original preservation, cancellation, HTTP failures, path validation");
}
finally { Directory.Delete(directory, true); }

sealed class FakeRenderer : IImageRenderer
{
    public bool Reject;
    public byte[] CreateDarkJpeg(byte[] original) => Reject ? throw new InvalidDataException() : [1,2,3];
    public bool IsValid(string path) => File.Exists(path);
}
sealed class FakeHttp : HttpMessageHandler
{
    public byte[] Metadata = [];
    public byte[] Image = [];
    public bool Fail;
    public int Count;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        { Content = new ByteArrayContent(request.RequestUri!.Host == "bing.wdbyte.com" ? Metadata : Image) });
    }
}
