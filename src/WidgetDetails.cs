using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TokenStats
{
    sealed partial class QuotaWidget
    {
        public void SetExpanded(bool value)
        {
            if (expanded == value) return;
            if (value)
            {
                compactLocation = Location;
                Point bottomRight = new Point(Right, Bottom);
                expanded = true; ApplySize();
                Location = new Point(bottomRight.X - Width, bottomRight.Y - Height);
                KeepOnScreen(); expandedOrigin = Location;
            }
            else
            {
                Point restore = new Point(compactLocation.X + Left - expandedOrigin.X, compactLocation.Y + Top - expandedOrigin.Y);
                expanded = false; ApplySize(); Location = restore; KeepOnScreen();
            }
            SaveSettings(); Invalidate();
        }

        internal int HitAction(Point point)
        {
            float x = point.X * (expanded ? 400f : 220f) / ClientSize.Width;
            float y = point.Y * (expanded ? 510f : 144f) / ClientSize.Height;
            if (!expanded)
            {
                if (new RectangleF(172, 12, 32, 32).Contains(x, y)) return 1;

            }
            else
            {
                if (new RectangleF(348, 16, 36, 36).Contains(x, y)) return 1;
                if (new RectangleF(20, 466, 162, 30).Contains(x, y)) return 3;
                if (new RectangleF(194, 466, 186, 30).Contains(x, y)) return 4;
            }
            return 0;
        }

        static string ResetDetails(Quota quota, bool valid)
        {
            if (quota != null && quota.Expired) return L.T("Период завершился. Ожидаем новый лимит.","Period ended. Waiting for quota reset.");
            if (!valid) return L.T("Нет актуальных данных","No current data");
            if (!quota.Reset.HasValue) return L.T("Время сброса неизвестно","Reset time unavailable");
            int minutes = Math.Max(0, (int)Math.Ceiling((quota.Reset.Value - DateTimeOffset.Now).TotalMinutes));
            return L.T("Сброс: ","Resets: ") + quota.Reset.Value.LocalDateTime.ToString(L.Russian ? "dd.MM.yyyy HH:mm" : "yyyy-MM-dd HH:mm") + L.T("\nЧерез ","\nIn ") +
                (minutes >= 1440 ? minutes / 1440 + L.T(" дн "," d ") : "") + (minutes % 1440) / 60 + L.T(" ч "," h ") + (minutes % 60).ToString("00") + L.T(" мин"," min");
        }

        void PaintExpanded(Graphics g)
        {
            g.ScaleTransform(ClientSize.Width / 400f, ClientSize.Height / 510f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var bg = new LinearGradientBrush(new Point(0, 0), new Point(400, 510), WidgetTheme.Canvas, BackColor))
            using (var shape = WindowOutline(400,510,FallbackWindowRadius))
            using (var border = new Pen(WidgetTheme.Border))
            using (var muted = new SolidBrush(WidgetTheme.Muted))
            using (var titleFont = AppFonts.Create( 16, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = AppFonts.Create( 11, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.FillPath(bg, shape); g.DrawPath(border, shape);
                DrawText(g,"TokenStats", titleFont, WidgetTheme.TextBrush, 20, 19);
                DrawExpandIcon(g, new RectangleF(348, 16, 36, 36), true);
                DrawCard(g, 60, L.T("ЛИМИТ НА 5 ЧАСОВ","5-HOUR LIMIT"), weeklyPrimary ? secondary : remaining, weeklyPrimary ? secondaryQuota : primaryQuota);
                DrawCard(g, 224, L.T("НЕДЕЛЬНЫЙ ЛИМИТ","WEEKLY LIMIT"), weeklyPrimary ? remaining : secondary, weeklyPrimary ? primaryQuota : secondaryQuota);
                string text = detailsError != null ? L.T("Не удалось обновить данные: ","Could not refresh data: ") + detailsError :
                    lastUpdated.HasValue ? L.T("Обновлено в ","Updated at ") + lastUpdated.Value.LocalDateTime.ToString("HH:mm:ss") + L.T(" · проверка каждые 30 секунд"," · checked every 30 seconds") : L.T("Ожидание данных…","Waiting for data...");
                if (detailsError != null && lastUpdated.HasValue && primaryQuota != null)
                    text += L.T("\nПоследний остаток: ","\nLast remaining: ") + primaryQuota.Remaining.ToString("0.#") + "% · " + lastUpdated.Value.LocalDateTime.ToString("dd.MM HH:mm");
                using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter })
                    DrawText(g,text, smallFont, muted, new RectangleF(20, 397, 360, 59), format);
                DrawButton(g, new RectangleF(20, 466, 162, 30), L.T("Обновить лимиты","Refresh limits"));
                DrawButton(g, new RectangleF(194, 466, 186, 30), L.T("Войти через ChatGPT","Sign in with ChatGPT"));
            }
        }

        void DrawExpandIcon(Graphics g, RectangleF bounds, bool collapse)
        {
            using(var fill = new SolidBrush(WidgetTheme.Light && hoverAction != 1 && !(pressedAction == 1 && Capture && !moved) ? WidgetTheme.Surface : ActionFill(1)))
            using(var shape = Rounded(bounds,ControlRadius))
            using(var pen = new Pen(WidgetTheme.Accent,1.6f))
            {
                g.FillPath(fill,shape);
                float x=bounds.X+bounds.Width/2, y=bounds.Y+bounds.Height/2;
                if(!collapse) {
                    g.DrawLines(pen,new[]{new PointF(x-6,y-1),new PointF(x-6,y-6),new PointF(x-1,y-6)});
                    g.DrawLines(pen,new[]{new PointF(x+1,y+6),new PointF(x+6,y+6),new PointF(x+6,y+1)});
                } else {
                    g.DrawLines(pen,new[]{new PointF(x-7,y-2),new PointF(x-2,y-2),new PointF(x-2,y-7)});
                    g.DrawLines(pen,new[]{new PointF(x+2,y+7),new PointF(x+2,y+2),new PointF(x+7,y+2)});
                }
            }
        }

        void DrawButton(Graphics g, RectangleF bounds, string text)
        {
            int action = bounds.X < 100 ? 3 : 4;
            using (var path = Rounded(bounds, ControlRadius))
            using (var fill = new SolidBrush(WidgetTheme.Light && hoverAction != action && !(pressedAction == action && Capture && !moved) ? WidgetTheme.Surface : ActionFill(action)))
            using (var font = AppFonts.Create( 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            { g.FillPath(fill, path);
                DrawText(g,text, font, WidgetTheme.TextBrush, bounds, format); }
        }

        static void DrawCard(Graphics g, float top, string title, double? value, Quota quota)
        {
            const float contentLeft = 30f;
            Color accent = !value.HasValue ? WidgetTheme.Muted : value <= 10 ? WidgetTheme.Danger : value <= 25 ? WidgetTheme.Warning : WidgetTheme.Accent;
            using (var path = Rounded(new RectangleF(16, top, 368, 154), CardRadius))
            using (var bg = new SolidBrush(WidgetTheme.Surface))
            using (var muted = new SolidBrush(WidgetTheme.Muted))
            using (var color = new SolidBrush(accent))
            using (var titleFont = AppFonts.Create( 10, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var numberFont = AppFonts.Create( 42, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = AppFonts.Create( 11, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.FillPath(bg, path);
                DrawText(g,title, titleFont, muted, contentLeft, top + 12);
                DrawPercentage(g,value.HasValue ? value.Value.ToString("0.#") : "—",value.HasValue,numberFont,contentLeft,top+29,true);
                using (var track = Rounded(new RectangleF(contentLeft, top + 89, 340, 6), 3))
                using (var trackBrush = new SolidBrush(WidgetTheme.Track))
                {
                    g.FillPath(trackBrush, track);
                    if (value.HasValue && value.Value > 0)
                    {
                        var state = g.Save(); g.SetClip(new RectangleF(contentLeft, top + 89, (float)(340 * value.Value / 100), 6));
                        g.FillPath(color, track); g.Restore(state);
                    }
                }
                DrawText(g,ResetDetails(quota, value.HasValue), smallFont, muted, new RectangleF(contentLeft, top + 106, 340, 40));
            }
        }
    }
}
