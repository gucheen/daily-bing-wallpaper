# 每日壁纸 · Windows

轻量 Windows 托盘应用，每天 9:00 更新 Bing 壁纸，跟随 Windows 系统明暗模式切换原图与深色版。

- 支持多显示器、登录自启动和手动更新。
- 错过更新时间会补更新；下载失败保留旧壁纸并自动重试。
- 下载前检查服务端日期，支持 Bing 提前上线日期晚于今天的图片；服务端日期与缓存相同时跳过图片下载，日期早于今天或本地缓存时保留旧缓存。日期更新后还会比对原图 SHA-256，重复图片保留旧缓存并在 10 分钟后重试。
- 原图与深色图本地缓存，切换无需联网。

## 安装使用

运行 `DailyWallpaper-win-x64-Setup.exe`，按中文向导安装。缺少 **.NET 10 Desktop Runtime（桌面运行时）** 时，会询问是否下载并安装；安装运行时需要管理员权限。

也可以直接运行便携版 `DailyWallpaper.exe`：自包含版无需安装运行时，轻量版需要预先安装对应架构的桌面运行时。

右键托盘图标可更新壁纸、打开壁纸目录、切换“登录时自动启动”或退出。退出不会取消自启动；移动便携版后需重新启用自启动。

“照片信息”子菜单展示当前缓存照片的描述、日期、地区、文件名、来源地址及下载时间；较长内容可悬停查看全文。

缓存与日志位于 `%LOCALAPPDATA%\DailyWallpaper`，卸载时保留。

升级时安装器会自动关闭应用；旧版进程未能正常退出时，Windows Restart Manager 会在等待超时后强制结束占用安装文件的进程并继续安装。此步骤可能需要约 30 秒。

## 构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 Python 3。构建安装包还需 Windows 和 [Inno Setup 6.7.3+](https://jrsoftware.org/isdl.php)。

| 版本 | 命令 | 输出 |
| --- | --- | --- |
| 自包含版 | `python scripts/build.py --test` | `artifacts/win-x64/DailyWallpaper.exe` |
| 不含运行时 | `python scripts/build.py --framework-dependent --test` | `artifacts/win-x64-framework-dependent/DailyWallpaper.exe` |
| 在线安装包 | `python scripts/build.py --installer --test` | `artifacts/installer/DailyWallpaper-win-x64-Setup.exe` |

添加 `--runtime win-arm64` 可构建 ARM64 版本。Inno Setup 不在默认位置时，用 `--iscc "C:\path\ISCC.exe"` 指定。CI 提供以上三种 x64 产物。

## 测试与维护

`--test` 运行应用测试；构建轻量版后，在已安装 x64 .NET 10 桌面运行时的 Windows 上运行安装回归测试：

```powershell
./scripts/test_installer.ps1
```

更新安装包使用的运行时版本和校验值后，需重新构建：

```powershell
python scripts/update_installer_runtime.py
```

## 致谢

壁纸来自 [Bing Wallpaper API](https://bing.wdbyte.com/today)，版权归原作者及相关权利方所有，仅供个人桌面使用。安装向导采用 [Inno Setup 社区中文翻译](https://github.com/jrsoftware/issrc/blob/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl)。
