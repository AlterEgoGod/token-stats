using System;
using System.Globalization;
using System.Collections.Generic;
using Microsoft.Win32;
namespace TokenStats
{
    static class L
    {
        static string selected;
#if TESTS
        internal static string TestLanguage;
#endif
        static readonly Dictionary<string, string[]> labels = new Dictionary<string, string[]>();
        static L()
        {
            try { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\TokenStats")) selected=key==null ? null : key.GetValue("Language") as string; }
            catch(System.Security.SecurityException) {} catch(UnauthorizedAccessException) {} catch(System.IO.IOException) {}
        }
        public static bool Russian
        {
            get
            {
#if TESTS
                if (TestLanguage != null) return TestLanguage == "ru";
#endif
                return selected == "ru" || (selected != "en" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru");
            }
        }
        public static string T(string ru,string en) { var pair=new[]{ru,en}; labels[ru]=pair; labels[en]=pair; return Russian ? ru : en; }
        public static string Relabel(string text) { string[] pair; return labels.TryGetValue(text,out pair) ? (Russian ? pair[0] : pair[1]) : text; }
        public static void Select(string language)
        {
            if(language!="ru" && language!="en") throw new ArgumentException("Invalid language");
            using(var key=Registry.CurrentUser.CreateSubKey(@"Software\TokenStats")) key.SetValue("Language",language);
            selected=language;
        }
    }
}
