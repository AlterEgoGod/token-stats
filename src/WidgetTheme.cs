using System;
using System.Drawing;
using Microsoft.Win32;

namespace TokenStats
{
    static class WidgetTheme
    {
        #if TESTS
        internal static bool? PreviewLight;
#endif
        public static bool Light { get; private set; }
        public static void Refresh()
        {
#if TESTS
            if (PreviewLight.HasValue) { Light = PreviewLight.Value; return; }
#endif
            try
            {
                using(var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    Light = key == null || Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) != 0;
            }
            catch (System.IO.IOException) { Light = false; }
            catch (UnauthorizedAccessException) { Light = false; }
            catch (System.Security.SecurityException) { Light = false; }
        }
        public static Color Canvas { get { return Light ? Color.FromArgb(245,246,248) : Color.FromArgb(29,29,29); } }
        public static Color Surface { get { return Light ? Color.White : Color.FromArgb(39,39,39); } }
        public static Color Text { get { return Light ? Color.FromArgb(62,68,77) : Color.White; } }
        public static Color Muted { get { return Light ? Color.FromArgb(84,92,105) : Color.FromArgb(170,170,170); } }
        public static Color Border { get { return Light ? Color.FromArgb(223,226,232) : Color.FromArgb(55,55,55); } }
        public static Color Control { get { return Light ? Color.FromArgb(220,237,247) : Color.FromArgb(53,53,53); } }
        public static Color Track { get { return Light ? Color.FromArgb(226,229,233) : Color.FromArgb(57,57,57); } }
        public static Color Accent { get { return Light ? Color.FromArgb(0,157,235) : Color.FromArgb(0,157,235); } }
        public static Color Warning { get { return Light ? Color.FromArgb(142,91,0) : Color.FromArgb(255,199,87); } }
        public static Color Danger { get { return Light ? Color.FromArgb(190,40,59) : Color.FromArgb(255,110,110); } }
        static readonly Brush lightText = new SolidBrush(Color.FromArgb(62,68,77));
        public static Brush TextBrush { get { return Light ? lightText : Brushes.White; } }
    }
}
