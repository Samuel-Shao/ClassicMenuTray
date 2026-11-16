using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClassicMenu
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "ClassicMenuTray.SingleInstance", out created))
            {
                if (!created)
                {
                    return;
                }

                try { SetProcessDPIAware(); }
                catch { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "ClassicMenuTray";
        private const string ClsidRoot = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        private const string ClsidKey = ClsidRoot + @"\InprocServer32";
        private const string FeatureKey = @"SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8\60813048";
        private const string MarkerKey = @"Software\ClassicMenuTray";
        private const string HelperName = "ClassicMenuHelper.exe";

        private const int WH_MOUSE_LL = 14;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int VK_SHIFT = 0x10;
        private const int VK_LSHIFT = 0xA0;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint GA_ROOT = 2;

        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _status;
        private readonly ToolStripMenuItem _shiftAssistItem;
        private readonly ToolStripMenuItem _enable;
        private readonly ToolStripMenuItem _disable;
        private readonly ToolStripMenuItem _autoStart;
        private readonly Icon _iconOn;
        private readonly Icon _iconOff;

        private readonly LowLevelMouseProc _hookProc;
        private readonly System.Windows.Forms.Timer _shiftTimer;
        private IntPtr _hook;
        private bool _shiftAssist;
        private bool _shiftInjected;

        public TrayApp()
        {
            _iconOn = CreateIcon(true);
            _iconOff = CreateIcon(false);

            _menu = new ContextMenuStrip();
            _menu.Font = new Font("Microsoft YaHei UI", 9f);
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = true;
            _menu.ShowItemToolTips = true;

            ToolStripMenuItem title = new ToolStripMenuItem("经典右键菜单");
            title.Font = new Font(_menu.Font, FontStyle.Bold);
            title.Enabled = false;
            _menu.Items.Add(title);
            _menu.Items.Add(new ToolStripSeparator());

            _status = new ToolStripMenuItem("状态：正在检测...");
            _status.Enabled = false;
            _menu.Items.Add(_status);
            _menu.Items.Add(new ToolStripSeparator());

            _shiftAssistItem = new ToolStripMenuItem("切换为经典右键菜单");
            _shiftAssistItem.ToolTipText = "在桌面和文件资源管理器窗口中右键时，自动按住 Shift 显示经典菜单";
            _shiftAssistItem.Click += OnToggleShiftAssist;
            _menu.Items.Add(_shiftAssistItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem legacy = new ToolStripMenuItem("旧版系统兼容设置");
            _enable = new ToolStripMenuItem("写入经典菜单注册表项");
            _enable.Click += OnEnable;
            legacy.DropDownItems.Add(_enable);

            _disable = new ToolStripMenuItem("删除注册表项并恢复新菜单");
            _disable.Click += OnDisable;
            legacy.DropDownItems.Add(_disable);
            _menu.Items.Add(legacy);

            ToolStripMenuItem restart = new ToolStripMenuItem("重启资源管理器");
            restart.Click += OnRestartExplorer;
            _menu.Items.Add(restart);

            ToolStripMenuItem settings = new ToolStripMenuItem("打开系统右键菜单设置");
            settings.Click += OnOpenSettings;
            _menu.Items.Add(settings);

            _menu.Items.Add(new ToolStripSeparator());

            _autoStart = new ToolStripMenuItem("开机自动启动");
            _autoStart.Click += OnToggleAutoStart;
            _menu.Items.Add(_autoStart);

            ToolStripMenuItem about = new ToolStripMenuItem("关于");
            about.Click += OnAbout;
            _menu.Items.Add(about);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += OnExit;
            _menu.Items.Add(exit);

            _menu.Opening += delegate { UpdateUi(); };

            _icon = new NotifyIcon();
            _icon.ContextMenuStrip = _menu;
            _icon.Icon = _iconOff;
            _icon.Text = "经典右键菜单";
            _icon.Visible = true;
            _icon.DoubleClick += delegate { _menu.Show(Cursor.Position); };

            _shiftAssist = LoadShiftAssist();
            _shiftTimer = new System.Windows.Forms.Timer();
            _shiftTimer.Interval = 450;
            _shiftTimer.Tick += OnShiftTimer;

            _hookProc = MouseHookProc;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, IntPtr.Zero, 0);
            if (_hook == IntPtr.Zero)
            {
                _shiftAssist = false;
            }

            bool firstRun = LoadFirstRun();
            if (firstRun)
            {
                if (!IsAutoStartEnabled())
                {
                    SetAutoStart(true);
                }
                _icon.ShowBalloonTip(4000, "经典右键菜单",
                    "已开启：在桌面和文件资源管理器中右键时直接显示经典菜单。", ToolTipIcon.Info);
            }
            else
            {
                RepairAutoStartPath();
            }

            UpdateUi();
        }

        private static bool ClsidPresent()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ClsidKey))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    string value = key.GetValue("") as string;
                    return value == null || value.Length == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool FeatureDisabled()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(FeatureKey))
                {
                    if (key == null)
                    {
                        return false;
                    }
                    object value = key.GetValue("EnabledState");
                    return value != null && Convert.ToInt32(value) == 1;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return key != null && key.GetValue(RunValue) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled)
                    {
                        key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                    }
                    else if (key.GetValue(RunValue) != null)
                    {
                        key.DeleteValue(RunValue, false);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法修改开机启动设置：\n" + ex.Message, "经典右键菜单",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void RepairAutoStartPath()
        {
            if (!IsAutoStartEnabled())
            {
                return;
            }

            try
            {
                string current;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    current = key == null ? null : key.GetValue(RunValue) as string;
                }
                string expected = "\"" + Application.ExecutablePath + "\"";
                if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                {
                    SetAutoStart(true);
                }
            }
            catch { }
        }

        private static bool LoadFirstRun()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MarkerKey))
                {
                    if (key != null && key.GetValue("FirstRun") != null)
                    {
                        return false;
                    }
                }

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(MarkerKey))
                {
                    key.SetValue("FirstRun", 1, RegistryValueKind.DWord);
                    if (key.GetValue("ClsidBeforeExists") == null)
                    {
                        key.SetValue("ClsidBeforeExists", ClsidPresent() ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool LoadShiftAssist()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MarkerKey))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("ShiftAssist");
                        if (value != null)
                        {
                            return Convert.ToInt32(value) != 0;
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        private static void SaveShiftAssist(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(MarkerKey))
                {
                    key.SetValue("ShiftAssist", enabled ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private void OnToggleShiftAssist(object sender, EventArgs e)
        {
            if (_hook == IntPtr.Zero)
            {
                MessageBox.Show("无法安装鼠标钩子，Shift 辅助不可用。\n\n请尝试以当前用户重新启动程序。",
                    "经典右键菜单", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _shiftAssist = !_shiftAssist;
            SaveShiftAssist(_shiftAssist);
            if (!_shiftAssist)
            {
                ReleaseInjectedShift();
            }
            UpdateUi();
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _shiftAssist)
            {
                int message = wParam.ToInt32();
                if (message == WM_RBUTTONDOWN)
                {
                    if (!_shiftInjected && !ShiftDown() && ShouldAssistAtCursor(lParam))
                    {
                        keybd_event((byte)VK_LSHIFT, 0, 0, UIntPtr.Zero);
                        _shiftInjected = true;
                        ArmShiftRelease(1200);
                    }
                }
                else if (message == WM_RBUTTONUP && _shiftInjected)
                {
                    ArmShiftRelease(450);
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool ShiftDown()
        {
            return (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
        }

        private static bool ShouldAssistAtCursor(IntPtr lParam)
        {
            try
            {
                MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                IntPtr hwnd = WindowFromPoint(info.pt);
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr root = GetAncestor(hwnd, GA_ROOT);
                if (root == IntPtr.Zero)
                {
                    root = hwnd;
                }

                System.Text.StringBuilder name = new System.Text.StringBuilder(64);
                if (GetClassName(root, name, name.Capacity) == 0)
                {
                    return false;
                }

                string cls = name.ToString();
                return cls == "Progman" || cls == "WorkerW"
                    || cls == "CabinetWClass" || cls == "ExploreWClass";
            }
            catch
            {
                return false;
            }
        }

        private void ArmShiftRelease(int delay)
        {
            _shiftTimer.Stop();
            _shiftTimer.Interval = delay;
            _shiftTimer.Start();
        }

        private void OnShiftTimer(object sender, EventArgs e)
        {
            _shiftTimer.Stop();
            ReleaseInjectedShift();
        }

        private void ReleaseInjectedShift()
        {
            if (!_shiftInjected)
            {
                return;
            }

            keybd_event((byte)VK_LSHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            _shiftInjected = false;
        }

        private void UpdateUi()
        {
            bool assist = _shiftAssist && _hook != IntPtr.Zero;
            bool legacy = ClsidPresent() && FeatureDisabled();
            bool active = assist || legacy;

            if (assist)
            {
                _status.Text = "状态：右键直接显示经典菜单";
            }
            else if (legacy)
            {
                _status.Text = "状态：经典菜单已启用（旧方法）";
            }
            else
            {
                _status.Text = "状态：右键显示 Win11 新菜单";
            }

            _shiftAssistItem.Text = assist ? "切换回新右键菜单" : "切换为经典右键菜单";
            _shiftAssistItem.ToolTipText = assist
                ? "关闭右键接管，恢复 Windows 11 新右键菜单"
                : "在桌面和文件资源管理器窗口中右键时，自动按住 Shift 显示经典菜单";
            _shiftAssistItem.Enabled = _hook != IntPtr.Zero;
            _enable.Enabled = !legacy;
            _disable.Enabled = legacy;
            bool autoStart = IsAutoStartEnabled();
            _autoStart.Checked = autoStart;
            _autoStart.ToolTipText = autoStart
                ? "当前：已开启，点击关闭"
                : "当前：已关闭，点击开启";

            Icon desired = active ? _iconOn : _iconOff;
            if (!ReferenceEquals(_icon.Icon, desired))
            {
                _icon.Icon = desired;
            }
            _icon.Text = assist ? "经典右键菜单：右键已接管"
                : (active ? "经典右键菜单：已启用" : "经典右键菜单：未启用");
        }

        private void OnEnable(object sender, EventArgs e)
        {
            DialogResult proceed = MessageBox.Show(
                "这是旧版 Windows 11 的兼容方法。\n" +
                "Windows 11 26H2 已忽略这些设置，写入后仍会显示新菜单。\n\n" +
                "26H2 请使用托盘菜单中的“切换为经典右键菜单”。\n\n仍要继续吗？",
                "经典右键菜单", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (proceed != DialogResult.Yes)
            {
                return;
            }

            bool existedBefore = ClsidPresent();

            try
            {
                using (RegistryKey marker = Registry.CurrentUser.CreateSubKey(MarkerKey))
                {
                    if (marker.GetValue("ClsidBeforeExists") == null)
                    {
                        marker.SetValue("ClsidBeforeExists", existedBefore ? 1 : 0, RegistryValueKind.DWord);
                    }
                }

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ClsidKey))
                {
                    if (key == null)
                    {
                        throw new InvalidOperationException("无法创建注册表项。");
                    }
                    key.SetValue("", "", RegistryValueKind.String);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法写入当前用户的注册表设置：\n" + ex.Message, "经典右键菜单",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string error;
            int code = RunHelper("enable", out error);
            if (code == 0)
            {
                try
                {
                    using (RegistryKey marker = Registry.CurrentUser.CreateSubKey(MarkerKey))
                    {
                        marker.SetValue("FeatureManaged", 1, RegistryValueKind.DWord);
                    }
                }
                catch { }
            }
            else if (code == 1223)
            {
                MessageBox.Show("已取消管理员授权，经典菜单尚未完全启用。\n\n可以再次点击“启用经典右键菜单”。",
                    "经典右键菜单", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateUi();
                return;
            }
            else
            {
                string detail = string.IsNullOrEmpty(error) ? "" : "\n" + error;
                MessageBox.Show("修改系统功能开关失败。" + detail +
                    "\n\n经典菜单可能无法生效。你也可以尝试在“设置 → 个性化 → 上下文菜单”中打开“显示更多选项”。",
                    "经典右键菜单", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            UpdateUi();

            DialogResult restart = MessageBox.Show(
                "设置已写入。\n\n现在重启资源管理器以应用设置？\n（当前打开的文件夹窗口会关闭）\n\n" +
                "如果重启后仍是新菜单，请重启电脑。",
                "经典右键菜单",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (restart == DialogResult.Yes)
            {
                RestartExplorer();
                UpdateUi();
            }
        }

        private void OnDisable(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                "确定恢复 Windows 11 新版右键菜单？\n\n会删除本程序写入的功能开关。",
                "经典右键菜单",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }

            string error;
            int code = RunHelper("disable", out error);
            if (code == 1223)
            {
                MessageBox.Show("已取消管理员授权，设置未修改。", "经典右键菜单",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (code != 0)
            {
                string detail = string.IsNullOrEmpty(error) ? "" : "\n" + error;
                MessageBox.Show("删除系统功能开关失败。" + detail, "经典右键菜单",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                int before;
                using (RegistryKey marker = Registry.CurrentUser.OpenSubKey(MarkerKey))
                {
                    if (marker != null)
                    {
                        object value = marker.GetValue("ClsidBeforeExists");
                        before = value == null ? -1 : Convert.ToInt32(value);
                        if (before == 0)
                        {
                            Registry.CurrentUser.DeleteSubKeyTree(ClsidRoot, false);
                        }
                        marker.Close();
                        using (RegistryKey writable = Registry.CurrentUser.OpenSubKey(MarkerKey, true))
                        {
                            if (writable != null)
                            {
                                writable.DeleteValue("FeatureManaged", false);
                                writable.DeleteValue("ClsidBeforeExists", false);
                            }
                        }
                    }
                }
            }
            catch { }

            UpdateUi();

            DialogResult restart = MessageBox.Show(
                "已恢复新版菜单设置。\n\n现在重启资源管理器？\n（当前打开的文件夹窗口会关闭）\n\n" +
                "如果设置尚未生效，请重启电脑。",
                "经典右键菜单",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (restart == DialogResult.Yes)
            {
                RestartExplorer();
                UpdateUi();
            }
        }

        private void OnRestartExplorer(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                "重启资源管理器会关闭当前打开的文件夹窗口，是否继续？",
                "经典右键菜单",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                RestartExplorer();
                UpdateUi();
            }
        }

        private void OnOpenSettings(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:personalization-contextmenu") { UseShellExecute = true });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-settings:personalization") { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void OnToggleAutoStart(object sender, EventArgs e)
        {
            bool enabled = !IsAutoStartEnabled();
            SetAutoStart(enabled);
            UpdateUi();

            if (IsAutoStartEnabled() == enabled)
            {
                _icon.ShowBalloonTip(2000, "经典右键菜单",
                    enabled ? "已开启开机自动启动。" : "已关闭开机自动启动。", ToolTipIcon.Info);
            }
        }

        private void OnAbout(object sender, EventArgs e)
        {
            string display = "";
            string build = Environment.OSVersion.Version.Build.ToString();
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("DisplayVersion");
                        if (value != null)
                        {
                            display = " " + value.ToString();
                        }
                        value = key.GetValue("CurrentBuildNumber");
                        if (value != null)
                        {
                            build = value.ToString();
                        }
                    }
                }
            }
            catch { }

            string text =
                "经典右键菜单 1.1\n\n" +
                "Windows 11 26H2 不再允许把旧菜单设为系统默认，因此程序改为：\n" +
                "在桌面和文件资源管理器窗口中右键时，自动按住 Shift，让系统直接显示 Windows 10 风格的完整菜单。\n\n" +
                "· 仅在桌面和文件资源管理器窗口生效\n" +
                "· 不修改系统文件，不需要管理员权限\n" +
                "· 任务栏、开始菜单和第三方程序不受影响\n\n" +
                "托盘菜单可随时切换回新菜单。\n" +
                "系统：Windows 11" + display + " Build " + build;

            MessageBox.Show(text, "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExit(object sender, EventArgs e)
        {
            _shiftTimer.Stop();
            ReleaseInjectedShift();
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            _icon.Visible = false;
            _icon.Dispose();
            _menu.Dispose();
            DestroyIcon(_iconOn.Handle);
            DestroyIcon(_iconOff.Handle);
            _iconOn.Dispose();
            _iconOff.Dispose();
            ExitThread();
        }

        private static int RunHelper(string action, out string error)
        {
            error = null;
            string path = Path.Combine(Application.StartupPath, HelperName);
            if (!File.Exists(path))
            {
                error = "找不到 " + HelperName + "，请确认两个程序位于同一文件夹。";
                return -2;
            }

            ProcessStartInfo info = new ProcessStartInfo(path, action);
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WindowStyle = ProcessWindowStyle.Hidden;

            try
            {
                using (Process process = Process.Start(info))
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Win32Exception ex)
            {
                error = ex.Message;
                return ex.NativeErrorCode;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return -1;
            }
        }

        private static void RestartExplorer()
        {
            try
            {
                Process[] running = Process.GetProcessesByName("explorer");
                foreach (Process process in running)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(4000);
                    }
                    catch { }
                    finally
                    {
                        process.Dispose();
                    }
                }

                Thread.Sleep(1200);

                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("重启资源管理器失败：\n" + ex.Message + "\n\n请手动重启或重启电脑。",
                    "经典右键菜单", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Icon CreateIcon(bool active)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);

                    Color top = active ? Color.FromArgb(34, 158, 92) : Color.FromArgb(122, 132, 146);
                    Color bottom = active ? Color.FromArgb(21, 115, 71) : Color.FromArgb(74, 84, 98);

                    Rectangle bounds = new Rectangle(1, 1, 30, 30);
                    using (GraphicsPath path = RoundedRect(bounds, 7))
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Rectangle(0, 0, 32, 32), top, bottom, 90f))
                    {
                        graphics.FillPath(brush, path);
                    }

                    using (Pen pen = new Pen(Color.White, 2.6f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        graphics.DrawLine(pen, 8f, 10f, 24f, 10f);
                        graphics.DrawLine(pen, 8f, 16f, 24f, 16f);
                        graphics.DrawLine(pen, 8f, 22f, 19f, 22f);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                return Icon.FromHandle(handle);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
