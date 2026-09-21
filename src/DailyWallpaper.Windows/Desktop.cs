using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DailyWallpaper;

internal static class Desktop
{
    public static bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 0;
    }

    public static void SetWallpaper(string path)
    {
        var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), true)!;
        var desktop = (IDesktopWallpaper)Activator.CreateInstance(type)!;
        try { desktop.SetWallpaper(null, path); }
        finally { Marshal.FinalReleaseComObject(desktop); }
    }

    // 这里只声明 COM 接口的首个方法；null 显示器 ID 表示应用到所有显示器。
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId,
            [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    }
}
