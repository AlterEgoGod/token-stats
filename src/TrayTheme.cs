using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TokenStats
{
    static class TrayTheme
    {
        public static Color TextColor()
        {
            if (SystemInformation.HighContrast) return SystemColors.WindowText;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    // The system theme controls the taskbar; the app theme can differ.
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    if (value is int) return (int)value == 1 ? Color.Black : Color.White;
                }
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)) throw;
            }
            // Windows versions without this setting use a dark taskbar.
            return Color.White;
        }
    }
}
