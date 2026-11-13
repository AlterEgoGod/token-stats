using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
static class Setup
{
    [STAThread] static int Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "TokenStats-Setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (MessageBox.Show((System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "Установить TokenStats? Будут созданы ярлыки в меню Пуск и на рабочем столе." : "Install TokenStats for this Windows user? Start menu and Desktop shortcuts will be created."), "TokenStats Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, "TokenStats.exe");
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("TokenStats.exe"))
            using (var output = File.Create(exe)) input.CopyTo(output);
            using (var process = Process.Start(new ProcessStartInfo(exe, "--install") { UseShellExecute = false })) { process.WaitForExit(); return process.ExitCode; }
        }
        catch (Exception e) { MessageBox.Show(e.Message, "TokenStats Setup"); return 1; }
        finally { if (Directory.Exists(folder)) { try { File.Delete(Path.Combine(folder, "TokenStats.exe")); Directory.Delete(folder); } catch (IOException) { } } }
    }
}
