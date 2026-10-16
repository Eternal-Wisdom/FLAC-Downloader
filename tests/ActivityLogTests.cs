using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PlaylistFlac;

internal static class ActivityLogTests
{
    internal static void Run()
    {
        var originalContext=SynchronizationContext.Current;
        try
        {
        using(var main=new MainForm(true))
        {
            main.ShowInTaskbar=false;main.Opacity=0;main.Show();
            Call(main,"ShowActivityLog");
            var first=Window(main);first.ShowInTaskbar=false;first.Opacity=0;
            Task.Run(()=>Call(main,"Log","live worker update")).GetAwaiter().GetResult();
            var clock=Stopwatch.StartNew();
            while(!((TextBox)first.Controls[0]).Text.Contains("live worker update") && clock.ElapsedMilliseconds<3000)
            {Application.DoEvents();Thread.Sleep(10);}
            if(!((TextBox)first.Controls[0]).Text.Contains("live worker update"))throw new Exception("Open activity log did not receive timer-flushed worker output.");
            Call(main,"ShowActivityLog");
            if(Window(main)!=first || !main.Enabled)throw new Exception("Activity must reuse its window without blocking app controls.");
            first.Close();
            Call(main,"Log","after closing log");Call(main,"FlushLog");Call(main,"ShowActivityLog");
            var reopened=Window(main);reopened.Opacity=0;reopened.ShowInTaskbar=false;
            if(reopened==first || !((TextBox)reopened.Controls[0]).Text.Contains("after closing log"))throw new Exception("Reopened activity log lost recent entries.");
            for(int i=0;i<300;i++)Call(main,"Log","bounded entry "+i);
            Call(main,"FlushLog");
            var text=((TextBox)reopened.Controls[0]).Text;
            if(text.Split(new[]{Environment.NewLine},StringSplitOptions.None).Length>160 || !text.Contains("bounded entry 299"))throw new Exception("Live activity log did not retain bounded recent output.");
            var follow=(CheckBox)reopened.Controls[1];follow.Checked=false;
            Call(main,"Log","held while reading");Call(main,"FlushLog");
            if(((TextBox)reopened.Controls[0]).Text.Contains("held while reading"))throw new Exception("Paused log moved while reading.");
            follow.Checked=true;if(!((TextBox)reopened.Controls[0]).Text.Contains("held while reading"))throw new Exception("Following log did not catch up.");
            var search=(TextBox)typeof(ActivityLogWindow).GetField("search",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reopened);
            search.Text="HELD WHILE";if(!((TextBox)reopened.Controls[0]).Text.Contains("held while reading") || ((TextBox)reopened.Controls[0]).Text.Contains("bounded entry"))throw new Exception("Activity search did not filter case-insensitively.");
            var visible=(TextBox)reopened.Controls[0];visible.Select(0,4);int redraws=0;EventHandler changed=delegate{redraws++;};visible.TextChanged+=changed;
            Call(main,"Log","unrelated message");Call(main,"FlushLog");visible.TextChanged-=changed;
            if(redraws!=0 || visible.SelectionStart!=0 || visible.SelectionLength!=4)throw new Exception("Unrelated activity repainted the filtered view or lost selection.");
            Call(main,"Log","held while searching 日本語");Call(main,"FlushLog");if(!((TextBox)reopened.Controls[0]).Text.Contains("日本語"))throw new Exception("Filtered activity stopped updating live.");
            follow.Checked=false;Call(main,"Log","held while paused 日本語");Call(main,"FlushLog");search.Text="日本語";
            if(((TextBox)reopened.Controls[0]).Text.Contains("paused"))throw new Exception("Searching changed the paused activity snapshot.");
            search.Text="no matching synthetic message";if(((TextBox)reopened.Controls[0]).TextLength!=0)throw new Exception("Activity search retained unmatched rows.");
            search.Clear();if(!((TextBox)reopened.Controls[0]).Text.Contains("bounded entry 299"))throw new Exception("Clearing search lost the activity snapshot.");
            follow.Checked=true;if(!((TextBox)reopened.Controls[0]).Text.Contains("held while paused"))throw new Exception("Following after search did not catch up.");
            reopened.Close();Call(main,"Log","final update");Call(main,"FlushLog");main.Close();
        }
        }
        finally {SynchronizationContext.SetSynchronizationContext(originalContext);}
    }
    private static Form Window(MainForm main){return (Form)typeof(MainForm).GetField("activityWindow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);}
    private static void Call(MainForm main,string name,params object[] args){typeof(MainForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(main,args);}
}
