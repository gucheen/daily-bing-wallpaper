using System.Runtime.InteropServices;
using DailyWallpaper;

internal static class ShutdownTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    internal static void CheckMessages()
    {
        using var window = new ShutdownWindow();
        var exits = 0;
        window.ExitRequested += () => exits++;
        var handle = window.Handle;
        window.Show();
        if (GetAncestor(handle, 2) != handle || GetWindow(handle, 4) != 0 || window.Visible)
            throw new Exception("Shutdown window must be hidden, top-level and unowned");
        if (SendMessage(handle, 0x0011, 0, 1) != 1 || exits != 0)
            throw new Exception("Shutdown query must agree without exiting");
        SendMessage(handle, 0x0016, 0, 1);
        if (exits != 0) throw new Exception("Cancelled shutdown must leave the app running");
        SendMessage(handle, 0x0016, 1, 1);
        if (exits != 1) throw new Exception("Confirmed Restart Manager shutdown must exit");
        SendMessage(handle, 0x0010, 0, 0);
        if (exits != 2) throw new Exception("Close fallback must exit");
        Console.WriteLine("PASS: hidden top-level window, shutdown query, cancellation, confirmation and close fallback");
    }

    // Run the real application context in a GUI process while holding the installed
    // executable open. This exercises Restart Manager without changing the user's
    // wallpaper, using their cache, or competing with their single-instance lock.
    internal static void RunHost(string lockedFile, string directory)
    {
        using var file = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var client = new HttpClient(new PendingDownload(directory));
        using (var context = new WallpaperApplication(directory, client))
            Application.Run(context);
        File.WriteAllText(Path.Combine(directory, "stopped"), "graceful shutdown");
    }

    private sealed class PendingDownload(string directory) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            File.WriteAllText(Path.Combine(directory, "ready"), "download in progress");
            using var registration = cancellation.Register(() =>
                File.WriteAllText(Path.Combine(directory, "cancelled"), "download cancelled"));
            await Task.Delay(Timeout.Infinite, cancellation);
            throw new InvalidOperationException("Pending download should only end through cancellation");
        }
    }
}
