using System;using System.Collections.Generic;using System.Globalization;using System.Linq;using System.Windows.Forms;
namespace PlaylistFlac {
 internal static class QueueSort {
  private sealed class Key {internal ListViewItem Row;internal int Position,Rank,Ordinal;internal string Text;internal double Duration;internal bool Unknown;}
  internal static List<ListViewItem> Apply(IEnumerable<ListViewItem> rows,int column,bool descending) {
   var list=rows.ToList();if(column<0 || column>4)return list;
   var keys=list.Select((row,index)=>{double duration=column==3?((Track)row.Tag).DurationSeconds:0;string text=column>0 && column!=3?row.SubItems[column].Text:"";return new Key{Row=row,Position=Position(row),Ordinal=index,Text=text,Duration=duration,Unknown=duration<=0 || Double.IsNaN(duration) || Double.IsInfinity(duration),Rank=column==4?Rank(text):0};}).ToList();
   keys.Sort(delegate(Key a,Key b){
    int order=0;
    if(column==0)order=a.Position.CompareTo(b.Position);
    else if(column==3){if(a.Unknown!=b.Unknown)return a.Unknown?1:-1;if(!a.Unknown)order=a.Duration.CompareTo(b.Duration);}
    else if(column==4){order=a.Rank.CompareTo(b.Rank);if(order==0)order=StringComparer.CurrentCultureIgnoreCase.Compare(a.Text,b.Text);}
    else order=StringComparer.CurrentCultureIgnoreCase.Compare(a.Text,b.Text);
    if(order!=0)return descending?-Math.Sign(order):Math.Sign(order);
    order=a.Position.CompareTo(b.Position);return order!=0?order:a.Ordinal.CompareTo(b.Ordinal);
   });return keys.Select(k=>k.Row).ToList();
  }
  private static int Position(ListViewItem row){int n;return Int32.TryParse(row.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out n)?n:0;}
  private static int Rank(string s){if(s=="Downloaded")return 6;if(s=="Failed")return 5;if(s.StartsWith("Review",StringComparison.OrdinalIgnoreCase))return 4;if(s.IndexOf("download",StringComparison.OrdinalIgnoreCase)>=0)return 3;if(s.IndexOf("queue",StringComparison.OrdinalIgnoreCase)>=0 || s.IndexOf("wait",StringComparison.OrdinalIgnoreCase)>=0)return 2;if(s.IndexOf("search",StringComparison.OrdinalIgnoreCase)>=0)return 1;return 0;}
 }
}
