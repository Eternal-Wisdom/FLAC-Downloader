using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace PlaylistFlac {
 internal static class UiIcons {
  internal static Color StatusColor(string text) {string t=(text??"").ToLowerInvariant();return t.Contains("fail")||t.Contains("missing") ? Color.FromArgb(245,162,151) : t.Contains("review")||t.Contains("search")||t.Contains("queue") ? Color.FromArgb(240,204,132) : Color.FromArgb(136,231,185);}
  internal static Bitmap Create(string text,Color color) {
   var b=new Bitmap(18,18);using(var g=Graphics.FromImage(b))using(var p=new Pen(color,1.6f)) {
    g.SmoothingMode=SmoothingMode.AntiAlias;string t=text.ToLowerInvariant();
    if(t.Contains("download")){g.DrawLine(p,9,2,9,11);g.DrawLines(p,new[]{new Point(5,8),new Point(9,12),new Point(13,8)});g.DrawLines(p,new[]{new Point(3,12),new Point(3,15),new Point(15,15),new Point(15,12)});}
    else if(t.Contains("stop")){g.DrawRectangle(p,4,4,10,10);}
    else if(t.Contains("folder")||t=="choose"){g.DrawLines(p,new[]{new Point(2,14),new Point(2,4),new Point(7,4),new Point(9,6),new Point(16,6),new Point(16,14),new Point(2,14)});}
    else if(t.Contains("retry")||t.Contains("retries")){g.DrawArc(p,3,3,12,12,30,290);g.DrawLines(p,new[]{new Point(11,2),new Point(15,4),new Point(15,0)});}
    else if(t.Contains("check")){g.DrawEllipse(p,2,2,14,14);g.DrawLines(p,new[]{new Point(5,9),new Point(8,12),new Point(13,6)});}
    else if(t.Contains("fix")){g.DrawLine(p,4,14,13,5);g.DrawEllipse(p,10,2,5,5);g.DrawEllipse(p,2,12,3,3);}
    else if(t.Contains("activity")){g.DrawLines(p,new[]{new Point(1,10),new Point(5,10),new Point(7,4),new Point(10,14),new Point(13,8),new Point(17,8)});}
    else if(t.Contains("csv")){g.DrawRectangle(p,4,2,10,14);g.DrawLine(p,6,6,12,6);g.DrawLine(p,6,9,12,9);g.DrawLine(p,6,12,12,12);}
    else if(t.Contains("link")||t.Contains("connect")){g.DrawEllipse(p,2,7,9,6);g.DrawEllipse(p,7,3,9,6);}
    else {g.DrawEllipse(p,2,2,14,14);g.DrawLine(p,9,5,9,10);g.DrawEllipse(p,8,12,1,1);}
   }return b;
  }
 }
}
