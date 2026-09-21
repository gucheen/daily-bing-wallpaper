namespace DailyWallpaper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DailyWallpaper");
        try
        {
            Directory.CreateDirectory(directory);
            FileStream instance;
            try
            {
                instance = new FileStream(Path.Combine(directory, "application.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException e) when ((e.HResult & 0xffff) == 32) { return; }
            using (instance)
            using (var context = new WallpaperApplication(directory))
                Application.Run(context);
        }
        catch (Exception e)
        {
            MessageBox.Show($"启动失败：{e.Message}", "每日壁纸", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
