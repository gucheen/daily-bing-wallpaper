# Daily Wallpaper · 每日壁纸（Windows）

与同级 `daily-wallpaper` macOS 应用对应的 Windows 托盘版本，使用 C# / .NET 10 / Windows Forms。双击启动，不显示主窗口或控制台。

- 每天本地时间 9:00 获取 Bing 壁纸，每分钟检查一次；无缓存时立即下载，错过更新时间后在启动、唤醒或解锁时补更新。
- 跟随 Windows **系统模式**切换原图与深色版，主题变化后立即检查，每分钟另有兜底检查。自定义主题下以“选择你的默认 Windows 模式”为准，不使用单独的应用模式。
- 深色版压低高光并降低曝光 0.65 EV，JPEG 质量 95；原图按下载字节完整保留，切换不需要网络。
- 下载或图片处理失败保留上次壁纸，10 分钟后重试；手动更新可立即重试。
- 自动应用到全部显示器，保留系统壁纸填充方式；重新连接显示器、唤醒和解锁时重新应用，设置失败后每分钟重试。
- 托盘菜单包含壁纸日期、当前模式、立即更新、打开壁纸目录、关于、退出；提示文字显示图片说明。
- 同一用户只运行一个实例。

## 构建

安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 Python 3。在此目录执行：

```powershell
python scripts/build.py --test
```

输出：`artifacts/win-x64/DailyWallpaper.exe`。自包含单文件包含 .NET 运行时，使用者不需要另装 .NET。目标为 Windows 10/11；建议使用仍受支持的 Windows 版本。ARM64 设备使用：

```powershell
python scripts/build.py --runtime win-arm64 --test
```

也可以直接执行：

```powershell
dotnet publish src/DailyWallpaper.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o artifacts/win-x64
```

macOS/Linux 可交叉编译，但不能运行 Windows 托盘和图像处理测试。产物未做发布者签名；公开分发时可增加自己的代码签名。

## 安装和登录启动

将 `DailyWallpaper.exe` 放到固定位置，例如 `%LOCALAPPDATA%\Programs\DailyWallpaper`，双击运行。托盘图标可能在任务栏的隐藏图标区域。

按 `Win + R` 输入 `shell:startup`，在打开的目录中创建指向该 exe 的快捷方式即可登录启动。退出应用只停止当前会话，不删除快捷方式。更新时先从托盘退出，再替换 exe。

## 缓存与日志

原图、深色图、`current.json` 和 `application.log` 存储在：

```text
%LOCALAPPDATA%\DailyWallpaper\
```

图片采用独立文件名避免 Windows 沿用旧缓存；新图片生成并保存成功后才替换清单。历史图片保留，行为与 macOS 版一致。清单或图片损坏会触发重新下载。

## 深色效果

Core Image 是苹果专属组件。这里在线性 sRGB 空间按亮度压制高光，再降低曝光，避免简单叠黑造成的颜色变化。处理目标与 macOS 版一致，但不是苹果 `CIHighlightShadowAdjust` 的逐像素复刻；两端可能存在视觉差异。处理在后台运行，不阻塞托盘操作。

## 验证

```powershell
dotnet run --project tests/DailyWallpaper.Core.Tests -c Release
dotnet run --project tests/DailyWallpaper.Windows.Tests -c Release
```

核心测试覆盖 9 点调度、跨时区、补更新、暗图色调、原图保留、缓存读写、取消、网络失败和非法缓存路径。Windows 测试使用真实 PNG/JPEG 检查尺寸、暗图像素和损坏图片拒绝。

Windows 手动检查：

1. 双击启动，只出现托盘图标；重复启动不增加实例。
2. 检查首次下载、菜单和全部显示器壁纸。
3. 切换“设置 → 个性化 → 颜色”中的 Windows 明暗模式，确认离线时也能切换。
4. 断网手动更新，确认旧图保留；恢复网络后确认 10 分钟重试或手动更新成功。
5. 检查跨过 9:00、睡眠唤醒、解锁、显示器热插拔和退出。
6. 添加启动快捷方式并重新登录，确认启动；删除快捷方式可取消登录启动。

Windows 虚拟桌面的独立壁纸及组织策略限制，需要在目标机器上验证；不使用未公开的虚拟桌面接口。程序退出后当前壁纸仍保留。

## 数据来源

与 macOS 版相同：[Bing Wallpaper API](https://bing.wdbyte.com/today)。图片版权归原作者及相关权利方所有，本工具仅供个人桌面壁纸使用。

## 实现参考

- [Microsoft：设置全部显示器壁纸](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-idesktopwallpaper-setwallpaper)
- [Microsoft：系统设置、显示器与电源事件](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents)
