using DailyWallpaper;
using System.Drawing.Imaging;

var renderer = new ImageRenderer();
using var source = new Bitmap(64, 32);
using (var graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(230, 205, 180));
using var png = new MemoryStream();
source.Save(png, ImageFormat.Png);
var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
try
{
    using var client = new HttpClient();
    var store = new WallpaperStore(directory, renderer, client);
    var wallpaper = new Wallpaper("../../outside.png", new Uri("https://example.com/test.png"), "2026-09-21", "cn", "Test");
    var cached = store.Prepare(wallpaper, png.ToArray(), DateTimeOffset.Now);
    if (!File.ReadAllBytes(cached.ImagePath(directory, false)).SequenceEqual(png.ToArray())) throw new Exception("Original changed");
    using (var dark = new Bitmap(cached.ImagePath(directory, true)))
    {
        if (dark.Size != source.Size) throw new Exception("Dimensions changed");
        var pixel = dark.GetPixel(20, 20);
        if (!(pixel.R < 230 && pixel.G < 205 && pixel.B < 180 && pixel.B > 0)) throw new Exception("Dark image invalid");
    }
    if (store.Load() != cached) throw new Exception("Cache failed");
    try { renderer.CreateDarkJpeg([1,2,3]); throw new Exception("Invalid image accepted"); }
    catch (ArgumentException) { }
    File.WriteAllBytes(cached.ImagePath(directory, true), [1,2,3]);
    if (store.Load() is not null) throw new Exception("Corrupt JPEG accepted");
    Console.WriteLine("PASS: real PNG/JPEG rendering, dimensions, dark pixels, original preservation, invalid images");
}
finally { Directory.Delete(directory, true); }
