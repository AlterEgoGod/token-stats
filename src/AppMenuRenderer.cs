using System.Drawing;
namespace TokenStats
{
    sealed class AppMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public AppMenuRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(System.Windows.Forms.ToolStripRenderEventArgs e)
        { e.Graphics.Clear(WidgetTheme.Canvas); }
        protected override void OnRenderImageMargin(System.Windows.Forms.ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(System.Windows.Forms.ToolStripItemRenderEventArgs e)
        {
            using(var brush = new SolidBrush(e.Item.Selected && e.Item.Enabled ? WidgetTheme.Control : WidgetTheme.Canvas))
                e.Graphics.FillRectangle(brush,new Rectangle(Point.Empty,e.Item.Size));
        }
        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
        { e.TextColor=e.Item.Enabled ? WidgetTheme.Text : WidgetTheme.Muted; base.OnRenderItemText(e); }
        protected override void OnRenderItemCheck(System.Windows.Forms.ToolStripItemImageRenderEventArgs e)
        {
            var r=e.ImageRectangle;
            using(var pen=new Pen(WidgetTheme.Accent,1.8f))
                e.Graphics.DrawLines(pen,new[]{new Point(r.Left+3,r.Top+r.Height/2),new Point(r.Left+6,r.Bottom-4),new Point(r.Right-2,r.Top+3)});
        }
        protected override void OnRenderArrow(System.Windows.Forms.ToolStripArrowRenderEventArgs e)
        { e.ArrowColor=WidgetTheme.Text; base.OnRenderArrow(e); }
        protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
        { using(var pen=new Pen(WidgetTheme.Border)) e.Graphics.DrawLine(pen,12,e.Item.Height/2,e.Item.Width-12,e.Item.Height/2); }
        protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
        { using(var pen=new Pen(WidgetTheme.Border)) e.Graphics.DrawRectangle(pen,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1); }
    }
}
