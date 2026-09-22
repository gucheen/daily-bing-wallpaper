using System.Diagnostics;
using Microsoft.Win32;

namespace DailyWallpaper;

internal sealed class WallpaperApplication : ApplicationContext
{
    private readonly ShutdownWindow dispatcher = new();
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem detail = new("正在加载壁纸…") { Enabled = false };
    private readonly ToolStripMenuItem mode = new() { Enabled = false };
    private readonly ToolStripMenuItem photoInfo = new("照片信息") { Enabled = false };
    private readonly ToolStripLabel photoDescription = new();
    private readonly ToolStripLabel photoDate = new();
    private readonly ToolStripLabel photoRegion = new();
    private readonly ToolStripLabel photoFileName = new();
    private readonly ToolStripLabel photoSource = new();
    private readonly ToolStripLabel photoDownloadedAt = new();
    private readonly ToolStripMenuItem refresh = new("立即更新壁纸");
    private readonly ToolStripMenuItem startup = new("登录时自动启动");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 60_000 };
    private readonly HttpClient client;
    private readonly CancellationTokenSource stopping = new();
    private readonly WallpaperStore store;
    private readonly string directory;
    private CachedWallpaper? current;
    private bool updating;
    private bool needsRetry;
    private bool awaitingUpdate;
    private bool exiting;
    private bool disposed;
    private bool applyFailed;
    private bool? lastAppliedDark;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;
    private DateTimeOffset? lastChecked;

    public WallpaperApplication(string directory, HttpClient? client = null)
    {
        this.directory = directory;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        store = new WallpaperStore(directory, new ImageRenderer(), this.client);
        dispatcher.ExitRequested += ExitThread;
        _ = dispatcher.Handle;
        refresh.Click += (_, _) => Refresh();
        photoInfo.DropDownItems.AddRange([photoDescription, photoDate, photoRegion,
            photoFileName, photoSource, new ToolStripSeparator(), photoDownloadedAt]);
        photoInfo.DropDown.ShowItemToolTips = true;
        menu.Items.AddRange([detail, mode, photoInfo, new ToolStripSeparator(), refresh]);
        menu.Items.Add("打开壁纸目录", null, (_, _) => OpenDirectory());
        startup.Click += (_, _) => ToggleStartup();
        menu.Items.Add(startup);
        menu.Opening += (_, _) => UpdateStartupMenu();
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
            if (exiting) return;
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
            (needsRetry || Schedule.IsDue(lastChecked ?? current?.RefreshedAt, DateTimeOffset.Now, TimeZoneInfo.Local))) Refresh();
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
            var changed = current != cached;
            current = cached;
            needsRetry = false;
            awaitingUpdate = false;
            lastChecked = DateTimeOffset.Now;
            retryAfter = DateTimeOffset.MinValue;
            if (changed) ApplyCurrent();
            Log(changed ? $"壁纸下载成功：{cached.Wallpaper.Date} {cached.Wallpaper.Description}" : "当前已是今日壁纸，无需重复下载");
        }
        catch (OperationCanceledException) when (exiting) { }
        catch (WallpaperNotUpdatedException e)
        {
            if (exiting) return;
            needsRetry = true;
            awaitingUpdate = true;
            retryAfter = DateTimeOffset.Now.AddMinutes(10);
            Log($"等待壁纸更新：{e.Message}");
        }
        catch (Exception e)
        {
            if (exiting) return;
            needsRetry = true;
            awaitingUpdate = false;
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
        detail.Text = updating ? "正在更新壁纸…" : awaitingUpdate ? "壁纸尚未更新，10 分钟后重试" : needsRetry ? "更新失败，10 分钟后重试" :
            applyFailed ? "设置桌面失败，将自动重试" : current is null ? "正在加载壁纸…" : $"今日壁纸：{current.Wallpaper.Date}";
        var description = current?.Wallpaper.Description;
        tray.Text = string.IsNullOrWhiteSpace(description) ? "每日壁纸" : description[..Math.Min(63, description.Length)];
        photoInfo.Enabled = current is not null;
        if (current is not null)
        {
            SetPhotoInfo(photoDescription, "描述", current.Wallpaper.Description);
            SetPhotoInfo(photoDate, "日期", current.Wallpaper.Date);
            SetPhotoInfo(photoRegion, "地区", current.Wallpaper.Region);
            SetPhotoInfo(photoFileName, "文件名", current.Wallpaper.FileName);
            SetPhotoInfo(photoSource, "来源", current.Wallpaper.Url.AbsoluteUri);
            SetPhotoInfo(photoDownloadedAt, "下载时间", current.RefreshedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        }
    }

    private static void SetPhotoInfo(ToolStripLabel item, string label, string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "未提供" : value.Trim();
        // 长描述和 URL 在菜单中缩略，悬停可查看完整内容；& 按原文显示。
        item.Text = $"{label}：{(text.Length > 60 ? text[..60] + "…" : text)}".Replace("&", "&&");
        item.ToolTipText = $"{label}：{text}";
        item.AutoToolTip = false;
    }

    private void OpenDirectory()
    {
        try { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(e.Message, "无法打开壁纸目录"); }
    }

    private void UpdateStartupMenu()
    {
        try { startup.Checked = StartupRegistration.IsEnabled(Application.ExecutablePath); }
        catch (Exception e) { Log($"读取自启动设置失败：{e}"); }
    }

    private void ToggleStartup()
    {
        try
        {
            StartupRegistration.SetEnabled(Application.ExecutablePath,
                !StartupRegistration.IsEnabled(Application.ExecutablePath));
            UpdateStartupMenu();
        }
        catch (Exception e)
        {
            Log($"设置自启动失败：{e}");
            MessageBox.Show(e.Message, "无法设置自启动", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(directory, "application.log"), $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    protected override void ExitThreadCore()
    {
        if (exiting) return;
        Stop();
        base.ExitThreadCore();
    }

    private void Stop()
    {
        if (exiting) return;
        exiting = true;
        stopping.Cancel();
        timer.Stop();
        tray.Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            Stop();
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
