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
    var fetched = await store.FetchAsync(At(22,9), CancellationToken.None);
    Assert(handler.Count == 2 && store.Load() == fetched, "Metadata and image fetch");
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
    Console.WriteLine("PASS: scheduling, time zones, tone mapping, cache, original preservation, cancellation, HTTP failures, path validation");
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
