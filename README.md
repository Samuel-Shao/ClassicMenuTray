# Classic Menu Tray · 经典右键菜单托盘

A small Windows tray utility that makes right-clicking the desktop and File Explorer open the classic Windows 10-style context menu directly on Windows 11 26H2, where the old registry tweaks no longer work.

一个 Windows 托盘小工具：在 Windows 11 26H2（旧注册表方法已失效）上，让桌面和文件资源管理器的右键直接弹出 Windows 10 风格的经典完整菜单。

[中文](#中文) · [English](#english)

---

## 中文

### 功能

- 桌面和文件资源管理器右键直接显示经典完整菜单
- 托盘常驻，首次运行自动开启开机自启
- 不修改系统文件，不需要管理员权限
- 托盘菜单可随时切换回 Windows 11 新菜单
- 保留旧版系统兼容设置（仅对仍支持注册表覆盖的旧版本有效）

### 系统要求

- Windows 11 x64（在 26H2 Build 26300.10066 上测试）
- .NET Framework 4.x（Windows 10/11 自带）

### 下载与使用

1. 在本仓库的 **Releases** 页面下载 `ClassicMenuTray.exe`。
2. 双击运行。托盘出现图标后，桌面和文件资源管理器右键会直接显示经典菜单。
3. 托盘菜单说明：
   - `状态：...`：当前右键模式
   - `切换回新右键菜单` / `切换为经典右键菜单`：切换右键接管
   - `旧版系统兼容设置`：写入/删除旧版注册表项（26H2 上无效，仅作兼容保留）
   - `重启资源管理器`、`打开系统右键菜单设置`
   - `开机自动启动`：有勾表示已启用
   - `关于`、`退出`
4. 程序未签名，SmartScreen 或杀毒软件可能提示，请选择“仍要运行”。

### 工作原理

Windows 11 26H2 不再响应经典的 `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` CLSID 覆盖和功能开关 `60813048`。

本程序改为安装一个作用域受限的 `WH_MOUSE_LL` 鼠标钩子，仅在桌面（`Progman` / `WorkerW`）和文件资源管理器窗口（`CabinetWClass` / `ExploreWClass`）上，于右键按下时注入 Shift，松开后释放，使系统直接显示 Shift+右键的经典完整菜单。

### 限制

- 仅在桌面和文件资源管理器窗口生效；任务栏、开始菜单、第三方程序不受影响
- 全屏程序中的右键不会被处理
- 仅接管鼠标右键，`Shift+F10` 等键盘方式不受影响
- 右键时会短暂按下 Shift；如果你本来按住 Shift，程序不会重复注入
- 程序是未签名的托管 exe，部分安全软件可能误报

### 从源码构建

需要 .NET Framework 4.x 的 `csc.exe`（Windows 自带），无需安装额外 SDK。

```bat
build.bat
```

输出到 `dist\`：

- `ClassicMenuTray.exe` — 托盘主程序
- `ClassicMenuHelper.exe` — 旧版注册表方法的提权辅助程序

### 卸载

1. 托盘菜单 → `退出`
2. 删除程序文件
3. 如已开启自启，先在托盘菜单关闭；或删除注册表值：
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ClassicMenuTray`
4. 可选：删除 `HKCU\Software\ClassicMenuTray`（保存的开关状态）

### 项目结构

```text
src/TrayApp.cs        托盘程序、鼠标钩子、菜单、开机自启
src/Helper.cs         旧版注册表方法（需要管理员权限）
src/app.manifest      主程序清单（asInvoker）
src/helper.manifest   辅助程序清单（requireAdministrator）
build.bat             使用 csc.exe 构建
```

---

## English

### Features

- Right-click on the desktop and in File Explorer opens the full classic menu directly
- Lives in the notification area and enables autostart on first run
- Does not modify system files and does not require administrator rights
- Tray menu lets you switch back to the Windows 11 menu at any time
- Keeps legacy registry actions for older Windows builds that still honor them

### Requirements

- Windows 11 x64 (tested on 26H2 Build 26300.10066)
- .NET Framework 4.x (included with Windows 10/11)

### Download and usage

1. Download `ClassicMenuTray.exe` from the **Releases** page of this repository.
2. Run it. A tray icon appears; right-clicking the desktop and File Explorer now shows the classic menu.
3. Tray menu:
   - `状态：...` — current right-click mode
   - `切换回新右键菜单` / `切换为经典右键菜单` — toggle the hook
   - `旧版系统兼容设置` — write/remove the legacy registry tweaks (ignored on 26H2)
   - `重启资源管理器` — restart Explorer
   - `打开系统右键菜单设置` — open the related Settings page
   - `开机自动启动` — a check mark means autostart is enabled
   - `关于` / `退出` — About / Exit
4. The executable is unsigned, so SmartScreen or antivirus software may warn you. Choose "Run anyway" if you trust the source.

The tray UI is currently in Chinese.

### How it works

Windows 11 26H2 ignores the classic `{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` CLSID override and the `60813048` feature switch.

This app installs a scoped low-level mouse hook (`WH_MOUSE_LL`). Only when right-clicking the desktop (`Progman` / `WorkerW`) or File Explorer windows (`CabinetWClass` / `ExploreWClass`), it injects Shift on right-button down and releases it afterwards, so Windows opens the classic Shift+right-click menu directly.

### Limitations

- Only desktop and File Explorer windows are handled; the taskbar, Start menu and third-party apps are untouched
- Right-clicks in fullscreen apps are not handled
- Only the mouse is handled; `Shift+F10` and other keyboard paths are unchanged
- Shift is briefly pressed on right-click; if you are already holding Shift, nothing extra is injected
- The app is an unsigned managed executable, so some security tools may flag it

### Build from source

You only need the `csc.exe` shipped with .NET Framework 4.x (no extra SDK).

```bat
build.bat
```

Outputs to `dist\`:

- `ClassicMenuTray.exe` — tray application
- `ClassicMenuHelper.exe` — elevated helper for the legacy registry method

### Uninstall

1. Tray menu → `退出` (Exit)
2. Delete the program files
3. If autostart is enabled, turn it off from the tray menu first, or delete:
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\ClassicMenuTray`
4. Optional: delete `HKCU\Software\ClassicMenuTray` (saved toggle state)

### Project layout

```text
src/TrayApp.cs        Tray app, mouse hook, menu, autostart
src/Helper.cs         Legacy registry method (requires admin)
src/app.manifest      Main app manifest (asInvoker)
src/helper.manifest   Helper manifest (requireAdministrator)
build.bat             Build with csc.exe
```
