using DailyWallpaper;

static class CacheTests
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var client = new HttpClient();
            var store = new WallpaperStore(directory, new FakeRenderer(), client);
            var wallpaper = new Wallpaper("image.png", new Uri("https://example.com/image.png"),
                "2026-09-21", "cn", "Test");
            var current = store.Prepare(wallpaper, [1], DateTimeOffset.Now);
            var unrelated = Path.Combine(directory, "personal.jpg");
            File.WriteAllText(unrelated, "keep");
            var log = Path.Combine(directory, "application.log");
            File.WriteAllText(log, "keep");

            string[] AddGroup(int age, long size = 1)
            {
                var id = Guid.NewGuid().ToString("N");
                var paths = new[] { Path.Combine(directory, id + "-original.png"),
                    Path.Combine(directory, id + "-dark.jpg") };
                foreach (var path in paths)
                {
                    using (var file = File.Create(path)) file.SetLength(size);
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-age));
                }
                return paths;
            }

            var oldest = AddGroup(100);
            var retained = Enumerable.Range(1, WallpaperStore.MaxCachedWallpapers - 1)
                .Select(age => AddGroup(age)).ToArray();
            // Even an unusually old timestamp must not cause the current wallpaper to be evicted.
            File.SetLastWriteTimeUtc(current.ImagePath(directory, false), DateTime.UtcNow.AddYears(-1));
            File.SetLastWriteTimeUtc(current.ImagePath(directory, true), DateTime.UtcNow.AddYears(-1));
            Check(store.Load() == current, "Startup cleanup preserves current manifest");
            Check(oldest.All(path => !File.Exists(path)), "Count limit removes oldest pair");
            Check(retained.SelectMany(paths => paths).All(File.Exists), "Recent pairs retained");
            Check(File.ReadAllText(unrelated) == "keep" && File.ReadAllText(log) == "keep",
                "Unrelated images and logs preserved");

            var large = AddGroup(101, WallpaperStore.MaxCacheBytes / 2 + 1);
            Check(store.Load() == current && large.All(path => !File.Exists(path)),
                "Size limit removes oldest oversized pair");

            var fresh = store.Prepare(wallpaper, [2], DateTimeOffset.Now);
            Check(store.Load() == fresh, "Save cleanup preserves new wallpaper");
            Check(!File.Exists(current.ImagePath(directory, false)) &&
                !File.Exists(current.ImagePath(directory, true)), "Save enforces count limit");
            Check(Directory.GetFiles(directory, "*-original.png").Length == WallpaperStore.MaxCachedWallpapers,
                "At most 30 complete wallpaper pairs remain");

            using (var file = File.OpenWrite(fresh.ImagePath(directory, false)))
                file.SetLength(WallpaperStore.MaxCacheBytes + 1);
            Check(store.Load() == fresh && Directory.GetFiles(directory, "*-original.png").Length == 1,
                "Oversized current wallpaper protected while older groups are removed");
            if (OperatingSystem.IsWindows())
            {
                var locked = AddGroup(100);
                using (var file = new FileStream(locked[0], FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Check(store.Load() == fresh && File.Exists(locked[0]),
                        "Locked file does not prevent loading current wallpaper");
                }
                Check(store.Load() == fresh && locked.All(path => !File.Exists(path)),
                    "Cleanup retries previously locked orphan on next load");
            }
            File.WriteAllText(Path.Combine(directory, "current.json"), "broken");
            var orphan = AddGroup(100);
            Check(store.Load() is null && orphan.All(File.Exists), "Invalid manifest skips cleanup");
            Console.WriteLine("PASS: cache count and byte limits, oldest-first pair eviction, current protection, unrelated files, startup and save cleanup");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
