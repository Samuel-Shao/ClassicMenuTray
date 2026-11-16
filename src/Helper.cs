using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClassicMenu
{
    internal static class Helper
    {
        private const string PriorityKey = @"SYSTEM\CurrentControlSet\Control\FeatureManagement\Overrides\8";
        private const string FeatureKey = PriorityKey + @"\60813048";

        [STAThread]
        private static int Main(string[] args)
        {
            string action = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            try
            {
                if (action == "enable")
                {
                    EnableClassicMenu();
                    return 0;
                }
                if (action == "disable")
                {
                    RestoreNewMenu();
                    return 0;
                }

                MessageBox.Show("未知操作参数：" + action, "经典右键菜单",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "操作失败：\n" + ex.Message + "\n\n请确认当前账户具有管理员权限。",
                    "经典右键菜单",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void EnableClassicMenu()
        {
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(FeatureKey))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法创建功能开关注册表项。");
                }

                key.SetValue("EnabledState", 1, RegistryValueKind.DWord);
                key.SetValue("EnabledStateOptions", 0, RegistryValueKind.DWord);
                key.SetValue("Variant", 0, RegistryValueKind.DWord);
                key.SetValue("VariantPayloadKind", 0, RegistryValueKind.DWord);
                key.SetValue("VariantPayload", 0, RegistryValueKind.DWord);
            }
        }

        private static void RestoreNewMenu()
        {
            Registry.LocalMachine.DeleteSubKeyTree(FeatureKey, false);

            bool empty = false;
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(PriorityKey))
            {
                if (key != null)
                {
                    empty = key.SubKeyCount == 0 && key.ValueCount == 0;
                }
            }

            if (empty)
            {
                Registry.LocalMachine.DeleteSubKey(PriorityKey, false);
            }
        }
    }
}
