using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlaylistFlac
{
    internal sealed class UiActionButton : Button
    {
        private bool hovering;
        protected override void OnMouseEnter(EventArgs e){hovering=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hovering=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            Color background=Enabled ? (hovering ? FlatAppearance.MouseOverBackColor : BackColor) : Color.FromArgb(30,36,43);
            using(var brush=new SolidBrush(background))e.Graphics.FillRectangle(brush,ClientRectangle);
            using(var pen=new Pen(Enabled ? FlatAppearance.BorderColor : Color.FromArgb(51,60,70)))e.Graphics.DrawRectangle(pen,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
            int left=Image==null ? 12 : 34;
            if(Image!=null)e.Graphics.DrawImage(Image,11,(Height-Image.Height)/2,Image.Width,Image.Height);
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(left,0,Math.Max(0,Width-left-8),Height),Enabled ? ForeColor : Color.FromArgb(132,143,154),TextFormatFlags.SingleLine|TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Math.Max(0,Width-8),Math.Max(0,Height-8)),ForeColor,background);
        }
    }
    internal static class UiStyle
    {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr parameter,string text);
        internal static void Hint(TextBox box,string text)
        {
            box.AccessibleDescription=text;
            box.HandleCreated+=delegate {SendMessage(box.Handle,0x1501,new IntPtr(1),text);};
            if(box.IsHandleCreated)SendMessage(box.Handle,0x1501,new IntPtr(1),text);
        }
        internal static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip {BackColor=Color.FromArgb(33,41,50),ForeColor=Color.White,
                Font=new Font("Segoe UI",10),Renderer=new ToolStripProfessionalRenderer(new MenuColors())};
        }
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground {get {return Color.FromArgb(33,41,50);}}
            public override Color ImageMarginGradientBegin {get {return ToolStripDropDownBackground;}}
            public override Color ImageMarginGradientMiddle {get {return ToolStripDropDownBackground;}}
            public override Color ImageMarginGradientEnd {get {return ToolStripDropDownBackground;}}
            public override Color MenuItemSelected {get {return Color.FromArgb(47,65,66);}}
            public override Color MenuItemSelectedGradientBegin {get {return MenuItemSelected;}}
            public override Color MenuItemSelectedGradientEnd {get {return MenuItemSelected;}}
            public override Color MenuItemPressedGradientBegin {get {return MenuItemSelected;}}
            public override Color MenuItemPressedGradientMiddle {get {return MenuItemSelected;}}
            public override Color MenuItemPressedGradientEnd {get {return MenuItemSelected;}}
            public override Color MenuItemBorder {get {return Color.FromArgb(136,231,185);}}
            public override Color MenuBorder {get {return Color.FromArgb(66,80,95);}}
        }
    }
}
