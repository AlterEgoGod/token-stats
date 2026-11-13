using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TokenStats
{
    static class Installation
    {
        static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenStats"); } }
        static string Target { get { return Path.Combine(Folder, "TokenStats.exe"); } }
        const string Entry = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TokenStats";
        static string StartLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TokenStats.lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TokenStats.lnk"); } }
        static void Shortcut(string path)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            object link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
            try
            {
                Type t = link.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Target });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Folder });
                t.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, link, new object[] { Target + ",0" });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, link, null);
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        public static int Install()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (!string.Equals(Application.ExecutablePath, Target, StringComparison.OrdinalIgnoreCase)) File.Copy(Application.ExecutablePath, Target, true);
                Shortcut(StartLink); Shortcut(DesktopLink);
                using (var key = Registry.CurrentUser.CreateSubKey(Entry))
                {
                    key.SetValue("DisplayName", "TokenStats"); key.SetValue("DisplayVersion", Application.ProductVersion);
                    key.SetValue("DisplayIcon", Target + ",0"); key.SetValue("InstallLocation", Folder);
                    key.SetValue("Publisher", "AlterEgoGod"); key.SetValue("UninstallString", "\"" + Target + "\" --uninstall");
                    key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                }
                MessageBox.Show(L.T("TokenStats установлен. Ярлыки добавлены в меню Пуск и на рабочий стол.","TokenStats installed. Shortcuts added to Start and Desktop."), "TokenStats");
                Process.Start(Target); return 0;
            }
            catch (Exception e) { MessageBox.Show(L.T("Не удалось установить. Закройте TokenStats и повторите.\n","Installation failed. Close TokenStats before retrying.\n") + e.Message, "TokenStats"); return 1; }
        }
        public static int Uninstall()
        {
            if (MessageBox.Show(L.T("Удалить TokenStats и ярлыки? Сначала закройте запущенные копии.","Remove TokenStats and its shortcuts? Close other running copies first."), "TokenStats", MessageBoxButtons.YesNo) != DialogResult.Yes) return 0;
            try
            {
                Startup.Set(false);
                if (File.Exists(StartLink)) File.Delete(StartLink);
                if (File.Exists(DesktopLink)) File.Delete(DesktopLink);
                // Delete only the application we installed; preserve any other files in its directory.
                string script = Path.Combine(Path.GetTempPath(), "TokenStats-remove-" + Guid.NewGuid().ToString("N") + ".ps1");
                string escaped = Target.Replace("'", "''");
                File.WriteAllText(script, "Wait-Process -Id " + Process.GetCurrentProcess().Id + " -ErrorAction SilentlyContinue\r\ntry { Remove-Item -LiteralPath '" + escaped + "' -Force -ErrorAction Stop; Remove-Item -LiteralPath 'HKCU:\\" + Entry + "' -Recurse -ErrorAction SilentlyContinue } catch { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('Close TokenStats and retry removal.'); }\r\nRemove-Item -LiteralPath $PSCommandPath -Force\r\n");
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"") { UseShellExecute = false, CreateNoWindow = true });
                return 0;
            }
            catch (Exception e) { MessageBox.Show(e.Message, "TokenStats"); return 1; }
        }
    }
}
