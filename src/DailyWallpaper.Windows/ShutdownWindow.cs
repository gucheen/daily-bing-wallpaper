namespace DailyWallpaper;

// Restart Manager sends session messages to top-level windows. A plain Control
// uses a hidden parking parent and is not a suitable application shutdown target.
internal sealed class ShutdownWindow : Form
{
    internal event Action? ExitRequested;

    public ShutdownWindow()
    {
        // Keep the default ShowInTaskbar: false creates an extra owner window,
        // preventing Restart Manager from reaching this shutdown handler.
        // SetVisibleCore keeps this window invisible, so no taskbar item appears.
        Text = "每日壁纸";
    }

    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case 0x0011: // WM_QUERYENDSESSION: agree, but do not exit yet.
                message.Result = 1;
                return;
            case 0x0016: // WM_ENDSESSION: FALSE means shutdown was cancelled.
                if (message.WParam != 0) ExitRequested?.Invoke();
                message.Result = 0;
                return;
            case 0x0010: // WM_CLOSE: also used by Restart Manager as a fallback.
                ExitRequested?.Invoke();
                message.Result = 0;
                return;
        }
        base.WndProc(ref message);
    }
}
