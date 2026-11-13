using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TokenStats
{
    sealed partial class QuotaWidget : Form
    {
        const string SettingsKey = @"Software\TokenStats\Widget";
        readonly bool persist;
        readonly ToolTip hint = new ToolTip();
        readonly Timer dismissTimer = new Timer { Interval = 100 };
        bool enabled, alwaysVisible, pinned = true, dragging, moved;
        Point dragStart, windowStart;
        Point? savedLocation;
        double? remaining, secondary;
        bool weeklyPrimary;
        string resetText = L.T("Ожидание данных","Waiting for data");
        public event Action RefreshRequested;
        public event Action LoginRequested;
        bool expanded;
        int pressedAction, hoverAction;
        Point compactLocation, expandedOrigin;
        Quota primaryQuota, secondaryQuota;
        string detailsError;
        DateTimeOffset? lastUpdated;
        public bool Expanded { get { return expanded; } }
        public bool AlwaysVisible { get { return alwaysVisible; } }
        public bool WidgetEnabled { get { return enabled; } }
        public bool SnapEnabled { get { return snapEnabled; } }
        bool snapEnabled = true;
        public bool Pinned { get { return pinned; } }
        protected override bool ShowWithoutActivation { get { return false; } }

        public QuotaWidget(bool saveSettings)
        {
            persist = saveSettings;
            WidgetTheme.Refresh();
            Text = L.T("TokenStats — виджет","TokenStats widget");
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = WidgetTheme.Canvas;
            DoubleBuffered = true;
            Cursor = Cursors.Default;
            AccessibleName = L.T("Остаток лимита Codex","Remaining Codex quota");
            if (persist) LoadSettings();
            TopMost = pinned;
            ApplySize();

            SystemEvents.DisplaySettingsChanged += DisplayChanged;
            dismissTimer.Tick += delegate { DismissIfInactive(); };
        }

        void LoadSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
                {
                    if (key == null) return;
                    snapEnabled = Convert.ToInt32(key.GetValue("SnapToEdge", 1)) != 0;
                    alwaysVisible = Convert.ToInt32(key.GetValue("AlwaysVisible", 0)) != 0;
                    pinned = Convert.ToInt32(key.GetValue("Pinned", 1)) != 0;

                    if (key.GetValue("X") != null && key.GetValue("Y") != null)
                        savedLocation = new Point(Convert.ToInt32(key.GetValue("X")), Convert.ToInt32(key.GetValue("Y")));
                }
            }
            catch (Exception e) { if (!(e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException || e is FormatException || e is OverflowException)) throw; }
        }

        void SaveSettings()
        {
            if (!persist) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    key.SetValue("SnapToEdge", snapEnabled ? 1 : 0);
                    key.SetValue("AlwaysVisible", alwaysVisible ? 1 : 0);
                    key.SetValue("Pinned", pinned ? 1 : 0);

                    Point position = expanded ? new Point(compactLocation.X + Left - expandedOrigin.X, compactLocation.Y + Top - expandedOrigin.Y) : Location;
                    key.SetValue("X", position.X); key.SetValue("Y", position.Y);
                }
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException)) throw;
                hint.SetToolTip(this, L.T("Не удалось сохранить настройки виджета: ","Could not save widget preferences: ") + e.Message);
            }
        }

        public static Point Fit(Point location, Size size, Rectangle area)
        {
            return new Point(Math.Max(area.Left, Math.Min(location.X, area.Right - size.Width)),
                Math.Max(area.Top, Math.Min(location.Y, area.Bottom - size.Height)));
        }

        public void PlaceInitial()
        {
            if (savedLocation.HasValue)
                Location = Fit(savedLocation.Value, Size, Screen.FromPoint(savedLocation.Value).WorkingArea);
            else MoveToTray(false);
            ApplySize();
            if (snapEnabled) SnapToEdge();
        }

        public void MoveToTray(bool save)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = Fit(new Point(area.Right - Width - 16, area.Bottom - Height - 16), Size, area);
            if (save) SaveSettings();
        }

        public void SetEnabled(bool value)
        {
            enabled = value;
            dismissTimer.Stop();
            if (enabled) { if (!Visible) SetExpanded(false); KeepOnScreen(); Show(); Activate(); }
            else { Hide(); SetExpanded(false); }
            SaveSettings();
        }

        public void RefreshLanguage()
        {
            Text=L.T("TokenStats — виджет","TokenStats widget");
            AccessibleName=L.T("Остаток лимита Codex","Remaining Codex quota");
            hint.RemoveAll(); hoverAction=0; Invalidate();
        }
        public void RefreshTheme()
        {
            WidgetTheme.Refresh(); BackColor = WidgetTheme.Canvas; Invalidate();
        }
        public void SetAlwaysVisible(bool value)
        {
            alwaysVisible = value;
            if (value) { dismissTimer.Stop(); if (!Visible) SetEnabled(true); }
            else if (Visible && !ContainsFocus) dismissTimer.Start();
            SaveSettings();
        }
        public static Point Snap(Point location, Size size, Rectangle area)
        {
            location = Fit(location, size, area);
            int left = location.X - area.Left, right = area.Right - location.X - size.Width;
            int top = location.Y - area.Top, bottom = area.Bottom - location.Y - size.Height;
            const int threshold = 32;
            if (left <= threshold) location.X = area.Left;
            else if (right <= threshold) location.X = area.Right - size.Width;
            if (top <= threshold) location.Y = area.Top;
            else if (bottom <= threshold) location.Y = area.Bottom - size.Height;
            return location;
        }
        public void SetSnapEnabled(bool value) { snapEnabled = value; if(value) SnapToEdge(); SaveSettings(); }
        void SnapToEdge() { Location = Snap(Location, Size, Screen.FromRectangle(Bounds).WorkingArea); }
        public void SetPinned(bool value) { pinned = value; TopMost = value; SaveSettings(); }
        public static float AutomaticScale(Size monitor)
        {
            float resolution = monitor.Width >= 3840 ? 1.75f : monitor.Width >= 2560 ? 1.25f : monitor.Width >= 1920 ? 1f : 0.82f;
            return resolution;
        }
        void ApplySize() { ApplySize(Screen.FromRectangle(Bounds)); }
        void ApplySize(Screen monitor)
        {
            float scale = AutomaticScale(monitor.Bounds.Size);
            ClientSize = new Size((int)Math.Round((expanded ? 400 : 220) * scale), (int)Math.Round((expanded ? 510 : 144) * scale));
        }
        void KeepOnScreen() { Location = Fit(Location, Size, Screen.FromRectangle(Bounds).WorkingArea); }
        void DisplayChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => { if (!IsDisposed) { ApplySize(); KeepOnScreen(); SaveSettings(); } }));
        }

        public void UpdateQuota(Quota fiveHourQuota, Quota weeklyQuota, string error, DateTimeOffset? updated = null, bool preferWeekly = false)
        {
            weeklyPrimary = preferWeekly;
            var selected = preferWeekly ? weeklyQuota : fiveHourQuota;
            primaryQuota = selected; secondaryQuota = preferWeekly ? fiveHourQuota : weeklyQuota;
            detailsError = error; lastUpdated = updated;
            remaining = error == null && selected != null && !selected.Expired ? (double?)selected.Remaining : null;
            secondary = error == null && secondaryQuota != null && !secondaryQuota.Expired ? (double?)secondaryQuota.Remaining : null;
            if (error != null) resetText = L.T("Нет связи · см. подробнее","Offline · expand for details");
            else if (selected != null && selected.Expired) resetText = L.T("Ожидаем новый лимит","Waiting for quota reset");
            else if (remaining.HasValue && selected.Reset.HasValue)
            {
                int minutes = Math.Max(0, (int)Math.Ceiling((selected.Reset.Value - DateTimeOffset.Now).TotalMinutes));
                resetText = L.T("Сброс через ","Resets in ") + (minutes >= 1440 ? minutes / 1440 + L.T(" дн "," d ") : "") +
                    (minutes % 1440) / 60 + L.T(" ч "," h ") + (minutes % 60).ToString("00") + L.T(" мин"," min");
            }
            else resetText = remaining.HasValue ? L.T("Время сброса неизвестно","Reset time unavailable") : L.T("Нет актуальных данных","No current data");
            AccessibleDescription = remaining.HasValue ? L.T("Осталось ","Remaining ") + remaining.Value.ToString("0.#") +
                (weeklyPrimary ? L.T("% недельного лимита","% of weekly quota") : L.T("% лимита на 5 часов","% of 5-hour quota")) : resetText;
            Invalidate();
        }

        static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        const float ControlRadius = 8f;
        const float CardRadius = 8f;
        const float FallbackWindowRadius = 8f;
        static void DrawText(Graphics g, string text, Font font, Brush brush, float x, float y)
        { DrawText(g,text,font,brush,new RectangleF(x,y,2000,200),null); }
        static void DrawText(Graphics g, string text, Font font, Brush brush, RectangleF bounds)
        { DrawText(g,text,font,brush,bounds,null); }
        static void DrawText(Graphics g, string text, Font font, Brush brush, RectangleF bounds, StringFormat format)
        {
            var solid = brush as SolidBrush;
            if(solid == null) { g.DrawString(text,font,brush,bounds); return; }
            using(var transform = g.Transform) {
                var points = new[] { new PointF(bounds.Left,bounds.Top), new PointF(bounds.Right,bounds.Bottom) };
                transform.TransformPoints(points);
                float scale = (float)Math.Sqrt(transform.Elements[0]*transform.Elements[0]+transform.Elements[1]*transform.Elements[1]);
                var box = Rectangle.FromLTRB((int)Math.Round(points[0].X),(int)Math.Round(points[0].Y),(int)Math.Round(points[1].X),(int)Math.Round(points[1].Y));
                using(var physical = new Font(font.FontFamily,Math.Max(8f,(float)Math.Round(font.Size*scale)),font.Style,GraphicsUnit.Pixel)) {
                    var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
                    if(format != null && format.Alignment == StringAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
                    if(format != null && format.LineAlignment == StringAlignment.Center) flags |= TextFormatFlags.VerticalCenter;
                    if(format != null && format.Trimming == StringTrimming.EllipsisCharacter) flags |= TextFormatFlags.EndEllipsis;
                    var state=g.Save(); g.ResetTransform(); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    try { TextRenderer.DrawText(g,text,physical,box,solid.Color,flags); } finally { g.Restore(state); }
                }
            }
        }
        void DrawStatusHeader(Graphics g, string text, Font font, Color statusColor)
        {
            float scale = ClientSize.Width / 220f;
            using(var physical = AppFonts.Create((float)Math.Round(font.Size*scale),font.Style,GraphicsUnit.Pixel)) {
                var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                var size=TextRenderer.MeasureText(text,physical,new Size(2000,200),flags);
                using(var line=new Bitmap((int)Math.Ceiling(160*scale),size.Height+6))
                using(var draw=Graphics.FromImage(line)) {
                    Color background=WidgetTheme.Canvas;
                    draw.Clear(background);
                    int textX=(int)Math.Round(11*scale);
                    TextRenderer.DrawText(draw,text,physical,new Point(textX,0),WidgetTheme.Muted,background,flags);
                    int top=line.Height,bottom=-1;
                    for(int y=0;y<line.Height;y++) for(int x=textX;x<line.Width;x++) {
                        Color pixel=line.GetPixel(x,y);
                        int difference=Math.Max(Math.Abs(pixel.R-background.R),Math.Max(Math.Abs(pixel.G-background.G),Math.Abs(pixel.B-background.B)));
                        if(difference>=32) {top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
                    }
                    float center=bottom>=top ? (top+bottom+1)/2f : size.Height/2f;
                    float diameter=5*scale;
                    // Pixel centres share the same coordinate system as the measured glyph pixels.
                    float cy=center-.5f, cx=(diameter-1)/2f, radius=diameter/2f;
                    for(int y=0;y<line.Height;y++) for(int x=0;x<(int)Math.Ceiling(diameter);x++) {
                        int inside=0;
                        for(int sy=0;sy<4;sy++) for(int sx=0;sx<4;sx++) {
                            float dx=x+(sx+.5f)/4-.5f-cx, dy=y+(sy+.5f)/4-.5f-cy;
                            if(dx*dx+dy*dy<=radius*radius) inside++;
                        }
                        if(inside>0) { float alpha=inside/16f; line.SetPixel(x,y,Color.FromArgb((int)(background.R+(statusColor.R-background.R)*alpha),(int)(background.G+(statusColor.G-background.G)*alpha),(int)(background.B+(statusColor.B-background.B)*alpha))); }
                    }
                    var state=g.Save();g.ResetTransform();
                    try {g.DrawImageUnscaled(line,(int)Math.Round(16*scale),(int)Math.Round(14*scale));}
                    finally {g.Restore(state);}
                }
            }
        }
        static void DrawPercentage(Graphics g, string number, bool hasValue, Font font, float x, float y, bool fullSize = false)
        {
            // Measure and render both pieces through GDI in physical pixels.
            using(var transform = g.Transform) {
                var origin = new[] { new PointF(x,y) }; transform.TransformPoints(origin);
                float scale = (float)Math.Sqrt(transform.Elements[0]*transform.Elements[0]+transform.Elements[1]*transform.Elements[1]);
                using(var physical = AppFonts.Create((float)Math.Round(font.Size*scale),font.Style,GraphicsUnit.Pixel)) {
                    var flags=TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                    var state=g.Save();g.ResetTransform();
                    try {
                        Point at = new Point((int)Math.Round(origin[0].X),(int)Math.Round(origin[0].Y));
                        TextRenderer.DrawText(g,number,physical,at,WidgetTheme.Text,flags);
                        if(hasValue) {
                            int width=TextRenderer.MeasureText(g,number,physical,new Size(2000,200),flags).Width;
                            at.X += width + Math.Max(1,(int)Math.Round(6*scale));
                            using(var percent = AppFonts.Create(fullSize ? physical.Size : Math.Max(10,(float)Math.Round(physical.Size*.48f)),fullSize ? physical.Style : FontStyle.Regular,GraphicsUnit.Pixel)) {
                                at.Y += (int)Math.Round((physical.Height-percent.Height)*.78f);
                                TextRenderer.DrawText(g,"%",percent,at,WidgetTheme.Accent,flags);
                            }
                        }
                    } finally {g.Restore(state);}
                }
            }
        }
        bool nativeCorners;
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int preference = 2; nativeCorners = DwmSetWindowAttribute(Handle, 33, ref preference, 4) == 0; }
            catch (DllNotFoundException) { nativeCorners = false; }
            UpdateWindowRegion();
        }
        void UpdateWindowRegion()
        {
            if (Width < 1 || Height < 1) return;
            Region next = null;
            if (!nativeCorners)
                using(var path = Rounded(new RectangleF(0,0,Width,Height), FallbackWindowRadius * Width / (expanded ? 400 : 220))) next = new Region(path);
            var old = Region; Region = next; if(old != null) old.Dispose();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); UpdateWindowRegion(); Invalidate();
        }
        GraphicsPath WindowOutline(float width, float height, float radius)
        {
            if (!nativeCorners) return Rounded(new RectangleF(.5f,.5f,width-1,height-1),radius);
            var path = new GraphicsPath(); path.AddRectangle(new RectangleF(.5f,.5f,width-1,height-1)); return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (expanded) { PaintExpanded(g); return; }
            g.ScaleTransform(ClientSize.Width / 220f, ClientSize.Height / 144f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Color accent = !remaining.HasValue ? WidgetTheme.Muted : remaining <= 10 ? WidgetTheme.Danger : remaining <= 25 ? WidgetTheme.Warning : WidgetTheme.Accent;
            using (var background = new LinearGradientBrush(new Point(0, 0), new Point(220, 144), WidgetTheme.Canvas, BackColor))
            using (var shape = WindowOutline(220,144,FallbackWindowRadius))
            using (var border = new Pen(WidgetTheme.Border))
            using (var accentBrush = new SolidBrush(accent))
            using (var muted = new SolidBrush(WidgetTheme.Muted))
            using (var titleFont = AppFonts.Create( 9, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var numberFont = AppFonts.Create( 46, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var smallFont = AppFonts.Create( 10, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.FillPath(background, shape); g.DrawPath(border, shape);
                DrawStatusHeader(g,weeklyPrimary ? L.T("CODEX  /  НЕДЕЛЯ","CODEX  /  WEEK") : L.T("CODEX  /  5 ЧАСОВ","CODEX  /  5 HOURS"),titleFont,accent);
                DrawExpandIcon(g, new RectangleF(172, 12, 32, 32), false);
                string number = remaining.HasValue ? Math.Floor(remaining.Value).ToString() : "—";
                DrawPercentage(g,number,remaining.HasValue,numberFont,16,29);
                using (var track = Rounded(new RectangleF(16, 91, 188, 6), 3))
                using (var trackBrush = new SolidBrush(WidgetTheme.Track))
                {
                    g.FillPath(trackBrush, track);
                    if (remaining.HasValue && remaining.Value > 0)
                    {
                        var state = g.Save();
                        g.SetClip(new RectangleF(16, 91, (float)(188 * remaining.Value / 100), 6));
                        g.FillPath(accentBrush, track); g.Restore(state);
                    }
                }
                DrawText(g,resetText, smallFont, muted, 16, 103);
                DrawText(g,(weeklyPrimary ? L.T("5 часов  ","5 hours  ") : L.T("Неделя  ","Week  ")) + (secondary.HasValue ? Math.Floor(secondary.Value) + "%" : "—"), smallFont, muted, 16, 120);
            }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverAction = 0; Invalidate(); }
        Color ActionFill(int action) { return pressedAction == action && Capture && !moved ? WidgetTheme.Border : hoverAction == action ? WidgetTheme.Track : WidgetTheme.Control; }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); Invalidate();
            if (e.Button != MouseButtons.Left) return;
            pressedAction = HitAction(e.Location);
            dragging = true; moved = false; dragStart = MousePosition; windowStart = Location; Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = HitAction(e.Location);
            if (hover != hoverAction) { hoverAction = hover; hint.SetToolTip(this, hover == 1 ? (expanded ? L.T("Свернуть","Collapse") : L.T("Развернуть","Expand")) : hover == 3 ? L.T("Обновить","Refresh") : hover == 4 ? L.T("Войти","Sign in") : ""); Invalidate(); }
            Cursor = hover == 0 ? Cursors.Default : Cursors.Hand;
            if (!dragging) return;
            Point current = MousePosition;
            int dx = current.X - dragStart.X, dy = current.Y - dragStart.Y;
            if (!moved && Math.Abs(dx) < SystemInformation.DragSize.Width / 2 && Math.Abs(dy) < SystemInformation.DragSize.Height / 2) return;
            moved = true;
            var targetMonitor = Screen.FromPoint(current);
            ApplySize(targetMonitor);
            var area = targetMonitor.WorkingArea;
            var position = new Point(windowStart.X + dx, windowStart.Y + dy);
            Location = snapEnabled ? Snap(position, Size, area) : Fit(position, Size, area);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !dragging) return;
            bool wasMoved = moved; dragging = false; Capture = false;
            if (wasMoved) { if (snapEnabled) SnapToEdge(); SaveSettings(); }
            else if (pressedAction != 0 && pressedAction == HitAction(e.Location))
            {
                if (pressedAction == 1) SetExpanded(!expanded);
                else if (pressedAction == 2 && ContextMenuStrip != null) ContextMenuStrip.Show(this, e.Location);
                else if (pressedAction == 3 && RefreshRequested != null) RefreshRequested();
                else if (pressedAction == 4 && LoginRequested != null) LoginRequested();

            }
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragging = false; }
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && !alwaysVisible) dismissTimer.Start();
        }
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            dismissTimer.Stop();
        }
        internal void DismissIfInactive()
        {
            if (alwaysVisible || !Visible) { dismissTimer.Stop(); return; }
            // Let the shared context menu and its submenus finish handling the click.
            if (ContextMenuStrip != null && ContextMenuStrip.Visible) return;
            if (ContainsFocus) { dismissTimer.Stop(); return; }
            SetEnabled(false);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; SetEnabled(false); }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { SystemEvents.DisplaySettingsChanged -= DisplayChanged; dismissTimer.Dispose(); hint.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
