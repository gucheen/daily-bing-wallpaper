using System.Diagnostics;
using Microsoft.Win32;

namespace DailyWallpaper;

internal sealed class WallpaperApplication : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem detail = new("正在加载壁纸…") { Enabled = false };
    private readonly ToolStripMenuItem mode = new() { Enabled = false };
    private readonly ToolStripMenuItem refresh = new("立即更新壁纸");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 60_000 };
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly CancellationTokenSource stopping = new();
    private readonly WallpaperStore store;
    private readonly string directory;
    private CachedWallpaper? current;
    private bool updating;
    private bool needsRetry;
    private bool exiting;
    private bool applyFailed;
    private bool? lastAppliedDark;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;

    public WallpaperApplication(string directory)
    {
        this.directory = directory;
        store = new WallpaperStore(directory, new ImageRenderer(), client);
        _ = dispatcher.Handle;
        refresh.Click += (_, _) => Refresh();
        menu.Items.AddRange([detail, mode, new ToolStripSeparator(), refresh]);
        menu.Items.Add("打开壁纸目录", null, (_, _) => OpenDirectory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关于每日壁纸", null, (_, _) => MessageBox.Show(
            "每日壁纸 1.0.0\n每天 9:00 获取 Bing 壁纸，跟随 Windows 系统明暗模式。\n图片版权归原作者及相关权利方所有。", "关于每日壁纸"));
        menu.Items.Add("退出每日壁纸", null, (_, _) => ExitThread());
        tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "每日壁纸", ContextMenuStrip = menu, Visible = true
        };
        SystemEvents.UserPreferenceChanged += PreferencesChanged;
        SystemEvents.DisplaySettingsChanged += WorkspaceChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        timer.Tick += (_, _) => Check();
        timer.Start();
        dispatcher.BeginInvoke(() =>
        {
            current = store.Load();
            ApplyCurrent();
            Check();
        });
    }

    private void Post(Action action)
    {
        if (exiting) return;
        try { dispatcher.BeginInvoke(() => { if (!exiting) action(); }); }
        catch (InvalidOperationException) { }
    }

    private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs e) => Post(Check);
    private void WorkspaceChanged(object? sender, EventArgs e) => Post(() => { ApplyCurrent(); Check(); });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) WorkspaceChanged(sender, e);
    }
    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock) WorkspaceChanged(sender, e);
    }

    private void Check()
    {
        if (exiting) return;
        if (lastAppliedDark != Desktop.IsDark()) ApplyCurrent();
        if (DateTimeOffset.Now >= retryAfter &&
            (needsRetry || Schedule.IsDue(current?.RefreshedAt, DateTimeOffset.Now, TimeZoneInfo.Local))) Refresh();
    }

    private async void Refresh()
    {
        if (updating || exiting) return;
        updating = true;
        UpdateMenu();
        try
        {
            var cached = await store.FetchAsync(DateTimeOffset.Now, stopping.Token);
            if (exiting) return;
            current = cached;
            needsRetry = false;
            retryAfter = DateTimeOffset.MinValue;
            ApplyCurrent();
            Log($"壁纸下载成功：{cached.Wallpaper.Date} {cached.Wallpaper.Description}");
        }
        catch (OperationCanceledException) when (exiting) { }
        catch (Exception e)
        {
            if (exiting) return;
            needsRetry = true;
            retryAfter = DateTimeOffset.Now.AddMinutes(10);
            Log($"壁纸更新失败：{e}");
        }
        finally
        {
            updating = false;
            if (!exiting) UpdateMenu();
        }
    }

    private void ApplyCurrent()
    {
        var dark = Desktop.IsDark();
        if (current is not null)
        {
            try
            {
                Desktop.SetWallpaper(current.ImagePath(directory, dark));
                lastAppliedDark = dark;
                applyFailed = false;
            }
            catch (Exception e)
            {
                lastAppliedDark = null;
                applyFailed = true;
                Log($"设置桌面失败：{e}");
            }
        }
        UpdateMenu();
    }

    private void UpdateMenu()
    {
        refresh.Enabled = !updating;
        mode.Text = Desktop.IsDark() ? "跟随系统：深色壁纸" : "跟随系统：原始壁纸";
        detail.Text = updating ? "正在更新壁纸…" : needsRetry ? "更新失败，10 分钟后重试" :
            applyFailed ? "设置桌面失败，将自动重试" : current is null ? "正在加载壁纸…" : $"今日壁纸：{current.Wallpaper.Date}";
        var description = current?.Wallpaper.Description;
        tray.Text = string.IsNullOrWhiteSpace(description) ? "每日壁纸" : description[..Math.Min(63, description.Length)];
    }

    private void OpenDirectory()
    {
        try { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(e.Message, "无法打开壁纸目录"); }
    }

    private void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(directory, "application.log"), $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    protected override void ExitThreadCore()
    {
        exiting = true;
        stopping.Cancel();
        timer.Stop();
        tray.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            exiting = true;
            stopping.Cancel();
            SystemEvents.UserPreferenceChanged -= PreferencesChanged;
            SystemEvents.DisplaySettingsChanged -= WorkspaceChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.SessionSwitch -= SessionChanged;
            timer.Dispose();
            tray.Icon?.Dispose();
            tray.Dispose();
            menu.Dispose();
            dispatcher.Dispose();
            client.Dispose();
            stopping.Dispose();
        }
        base.Dispose(disposing);
    }
}
