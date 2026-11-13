using System;
using System.Drawing;
namespace TokenStats
{
    static class AppFonts
    {
        public static Font Create(float size, FontStyle style, GraphicsUnit unit)
        { return new Font("Segoe UI",size,style,unit); }
    }
}
