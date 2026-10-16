using System;using System.Linq;using System.Collections.Generic;using System.Reflection;using System.Windows.Forms;using System.Threading;using PlaylistFlac;
internal static class QueueSortTests {
 internal static void Run(){
  var previous=SynchronizationContext.Current;
  try{using(var f=new MainForm(true)){f.ShowInTaskbar=false;f.Opacity=0;f.Show();f.LoadPreview();
   var list=(ListView)typeof(MainForm).GetField("tracks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f);
   if(list.HeaderStyle!=ColumnHeaderStyle.Clickable)throw new Exception("Sorting headers cannot be clicked.");
   var selection=typeof(MainForm).GetMethod("SelectVisibleTracks",BindingFlags.Instance|BindingFlags.NonPublic);var copy=typeof(MainForm).GetMethod("SelectedTrackText",BindingFlags.Instance|BindingFlags.NonPublic);
   var search=(TextBox)typeof(MainForm).GetField("queueSearch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f);search.Text="Open Water";
   selection.Invoke(f,new object[]{true});if(list.SelectedItems.Count!=2 || ((string)copy.Invoke(f,null)).Split(new[]{Environment.NewLine},StringSplitOptions.None).Length!=2)throw new Exception("Bulk selection/copy escaped visible search results.");
   var export=(Playlist)typeof(MainForm).GetMethod("SelectedTracklist",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f,null);
   if(export.Tracks.Count!=2 || export.Tracks.Any(t=>t.Title!="Open Water") || export.Source!=null)throw new Exception("Selected export included hidden rows or private source paths.");
   string exportPath=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"SelectedExport-"+Guid.NewGuid().ToString("N")+".csv");
   try{CsvPlaylist.Write(exportPath,export);var restored=CsvPlaylist.Read(exportPath);if(restored.Tracks.Count!=2 || !restored.Tracks.Select(t=>t.Artist).SequenceEqual(export.Tracks.Select(t=>t.Artist)))throw new Exception("Selected CSV did not preserve distinct artists and display order.");}finally{if(System.IO.File.Exists(exportPath))System.IO.File.Delete(exportPath);}
   selection.Invoke(f,new object[]{false});if(list.SelectedItems.Count!=0 || (string)copy.Invoke(f,null)!="")throw new Exception("Clear selection retained copied tracks.");search.Clear();
   var sort=typeof(MainForm).GetMethod("SortQueue",BindingFlags.Instance|BindingFlags.NonPublic);
   var original=list.Items.Cast<ListViewItem>().ToArray();var playlist=(Playlist)typeof(MainForm).GetField("playlist",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f);var source=playlist.Tracks.ToArray();var chosen=original[1];chosen.Selected=true;
   sort.Invoke(f,new object[]{1});var ascending=list.Items.Cast<ListViewItem>().ToArray();for(int i=1;i<ascending.Length;i++)if(StringComparer.CurrentCultureIgnoreCase.Compare(ascending[i-1].SubItems[1].Text,ascending[i].SubItems[1].Text)>0)throw new Exception("Title sorting failed.");
   if(!chosen.Selected || !source.SequenceEqual(playlist.Tracks))throw new Exception("Sorting changed selection or playlist order.");
   sort.Invoke(f,new object[]{1});var descending=list.Items.Cast<ListViewItem>().ToArray();for(int i=1;i<descending.Length;i++)if(StringComparer.CurrentCultureIgnoreCase.Compare(descending[i-1].SubItems[1].Text,descending[i].SubItems[1].Text)<0)throw new Exception("Reverse title sorting failed.");
   sort.Invoke(f,new object[]{0});if(!original.SequenceEqual(list.Items.Cast<ListViewItem>()))throw new Exception("Source-order restoration failed.");
   sort.Invoke(f,new object[]{4});typeof(MainForm).GetMethod("OnProgress",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f,new object[]{new EngineProgress{TrackKey=chosen.Name,Status="Succeeded",Type="download_complete"}});
   if(!(bool)typeof(MainForm).GetField("filterDirty",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f))throw new Exception("Status sorting did not schedule live reorder.");
   typeof(MainForm).GetMethod("ApplyQueueFilter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f,null);if(list.Items[list.Items.Count-1]!=chosen)throw new Exception("Completed row did not sort to completed group.");
   var a=new ListViewItem("01"){Tag=new Track{Title="夜",DurationSeconds=0}};a.SubItems.AddRange(new[]{"夜","例","--","Ready"});var b=new ListViewItem("02"){Tag=new Track{Title="A",DurationSeconds=180}};b.SubItems.AddRange(new[]{"A","例","3:00","Ready"});
   if(QueueSort.Apply(new[]{a,b},3,false).Last()!=a || QueueSort.Apply(new[]{a,b},3,true).Last()!=a)throw new Exception("Unknown durations must remain last in either direction.");
   if(QueueSort.Apply(new[]{a,b},2,true)[0]!=a)throw new Exception("Equal sort values lost stable source order.");
   foreach(double unknown in new[]{Double.NaN,Double.PositiveInfinity,Double.NegativeInfinity}){((Track)a.Tag).DurationSeconds=unknown;if(QueueSort.Apply(new[]{a,b},3,false).Last()!=a || QueueSort.Apply(new[]{a,b},3,true).Last()!=a)throw new Exception("Non-finite duration sorted as playable audio.");}
   f.Close();
  }}finally{SynchronizationContext.SetSynchronizationContext(previous);}
 }
}
