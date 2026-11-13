using System;
using System.Drawing;
namespace TokenStats
{
    static class WidgetTests
    {
        static void Check(bool value, string name) { if (!value) throw new Exception("Widget: " + name); }
        static void Click(QuotaWidget widget, float x, float y, bool drag)
        {
            var point = new Point((int)(widget.ClientSize.Width * x), (int)(widget.ClientSize.Height * y));
            var args = new System.Windows.Forms.MouseEventArgs(System.Windows.Forms.MouseButtons.Left, 1, point.X, point.Y, 0);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(QuotaWidget).GetMethod("OnMouseDown", flags).Invoke(widget, new object[] { args });
            if (drag) typeof(QuotaWidget).GetField("moved", flags).SetValue(widget, true);
            typeof(QuotaWidget).GetMethod("OnMouseUp", flags).Invoke(widget, new object[] { args });
        }
        public static void Run()
        {
            var size = new Size(220, 144);
            Check(QuotaWidget.Snap(new Point(20, 20), size, new Rectangle(0,0,1920,1040)) == Point.Empty, "snap two edges into corner");
            Check(QuotaWidget.Snap(new Point(400, 400), size, new Rectangle(0,0,1920,1040)) == new Point(400,400), "free position away from edges");
            Check(QuotaWidget.Snap(new Point(20, 300), size, new Rectangle(0, 0, 1920, 1040)) == new Point(0, 300), "snap left edge");
            Check(QuotaWidget.Snap(new Point(-250, 300), size, new Rectangle(-1920, 0, 1920, 1040)) == new Point(-220, 300), "snap negative monitor right edge");
            Check(QuotaWidget.Fit(new Point(1900, 1100), size, new Rectangle(0, 0, 1920, 1040)) == new Point(1700, 896), "stay above taskbar");
            Check(QuotaWidget.Fit(new Point(-5000, -200), size, new Rectangle(-1920, 0, 1920, 1040)) == new Point(-1920, 0), "negative monitor coordinates");
            Check(QuotaWidget.Fit(new Point(5000, 5000), size, new Rectangle(0, 0, 1280, 720)) == new Point(1060, 576), "recover offscreen position");
            using (var widget = new QuotaWidget(false))
            {
                Check(!widget.Visible && !widget.WidgetEnabled, "start hidden");
                Check(QuotaWidget.AutomaticScale(new Size(1920,1080))==1f,"Full HD automatic size");
                Check(QuotaWidget.AutomaticScale(new Size(2560,1440))==1.25f,"QHD automatic size");
                Check(QuotaWidget.AutomaticScale(new Size(1366,768)) < QuotaWidget.AutomaticScale(new Size(2560,1440)), "size restored when monitor grows");
                Check(QuotaWidget.AutomaticScale(new Size(3840,2160))>1.25f,"4K automatic size");
                Check(QuotaWidget.AutomaticScale(new Size(1366,768))<1f,"laptop automatic size");
                widget.SetPinned(false); Check(!widget.TopMost, "unpin");
                widget.SetPinned(true); Check(widget.TopMost, "pin");
                widget.UpdateQuota(new Quota { Remaining = 75 }, null, null);
                Check(widget.AccessibleDescription.Contains("75%"), "show quota");
                widget.UpdateQuota(new Quota { Remaining = 75 }, null, "offline");
                Check(!widget.AccessibleDescription.Contains("75%"), "do not show stale quota after error");
                widget.UpdateQuota(new Quota { Remaining = 75, Reset = DateTimeOffset.Now.AddMinutes(-1) }, null, null);
                Check(!widget.AccessibleDescription.Contains("75%"), "do not show expired quota");
                var fiveHour = new Quota { Remaining = 75, WindowMinutes = 300, Reset = DateTimeOffset.Now.AddHours(2) };
                var week = new Quota { Remaining = 42, WindowMinutes = 10080, Reset = DateTimeOffset.Now.AddDays(4) };
                widget.UpdateQuota(fiveHour, week, null, null, true);
                Check(widget.AccessibleDescription == L.T("Осталось 42% недельного лимита","Remaining 42% of weekly quota"), "Premium weekly value and caption");
                widget.UpdateQuota(null, week, null, null, true);
                Check(widget.AccessibleDescription.Contains("42%"), "weekly-only Premium response");
                widget.UpdateQuota(fiveHour, null, null, null, true);
                Check(!widget.AccessibleDescription.Contains("75%"), "missing weekly data does not display five-hour quota");
                widget.UpdateQuota(fiveHour, week, "offline", null, true);
                Check(!widget.AccessibleDescription.Contains("42%"), "hide weekly data after error");
                widget.UpdateQuota(fiveHour, new Quota { Remaining = 42, Reset = DateTimeOffset.Now.AddMinutes(-1) }, null, null, true);
                Check(!widget.AccessibleDescription.Contains("42%"), "hide expired weekly data");
                widget.UpdateQuota(fiveHour, week, null);
                Check(widget.AccessibleDescription == L.T("Осталось 75% лимита на 5 часов","Remaining 75% of 5-hour quota"), "switching away from Premium restores five-hour display");
                widget.PlaceInitial();
                var compactPosition = widget.Location;
                var compactSize = widget.Size;
                Click(widget, 0.25f, 0.45f, false);
                Check(!widget.Expanded, "background click does not expand");
                Click(widget, 0.9f, 0.14f, true);
                Check(!widget.Expanded, "drag ending on details does not expand");
                Click(widget, 0.9f, 0.14f, false);
                Check(widget.Expanded && widget.Height > compactSize.Height, "details button expands same widget");
                int refreshes = 0;
                widget.RefreshRequested += delegate { refreshes++; };
                Click(widget, 0.25f, 0.94f, false);
                Check(refreshes == 1, "expanded refresh button");
                Click(widget, 0.4f, 0.4f, false);
                Check(widget.Expanded, "expanded background does not collapse");
                Click(widget, 0.91f, 0.06f, false);
                Check(!widget.Expanded && widget.Size == compactSize && widget.Location == compactPosition, "collapse restores compact size and position");
                widget.SetEnabled(false); Check(!widget.Visible && !widget.WidgetEnabled, "hide widget");
                widget.SetEnabled(true);
                widget.SetExpanded(true);
                using (var outside = new System.Windows.Forms.Form { ShowInTaskbar = false, Size = new Size(50, 50) })
                {
                    outside.Show(); outside.Activate();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    typeof(QuotaWidget).GetMethod("OnDeactivate", flags).Invoke(widget, new object[] { EventArgs.Empty });
                    var wait = System.Diagnostics.Stopwatch.StartNew();
                    while (wait.ElapsedMilliseconds < 250) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Check(!widget.Visible && !widget.WidgetEnabled && !widget.Expanded, "outside focus hides expanded popup");
                    widget.SetAlwaysVisible(true);
                    widget.SetExpanded(true);
                    outside.Activate();
                    widget.DismissIfInactive();
                    Check(widget.Visible && widget.Expanded && widget.AlwaysVisible, "always visible survives outside focus");
                    widget.SetAlwaysVisible(false);
                    widget.DismissIfInactive();
                    // Windows may deny focus stealing by the helper form. A focused popup
                    // must stay open; an unfocused popup must dismiss after unchecking.
                    Check(!widget.AlwaysVisible && (widget.ContainsFocus ? widget.Visible : !widget.Visible), "disabling always visible respects current focus");
                }
                widget.SetEnabled(true);
                Check(widget.Visible && !widget.Expanded, "reopen compact popup");
                widget.SetEnabled(false);
            }
            Console.WriteLine("PASS: widget placement, monitor recovery, stale data, visibility, button clicks and expansion.");
        }
    }
}
