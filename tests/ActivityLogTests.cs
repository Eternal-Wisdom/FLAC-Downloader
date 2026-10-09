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
            reopened.Close();Call(main,"Log","final update");Call(main,"FlushLog");main.Close();
        }
        }
        finally {SynchronizationContext.SetSynchronizationContext(originalContext);}
    }
    private static Form Window(MainForm main){return (Form)typeof(MainForm).GetField("activityWindow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);}
    private static void Call(MainForm main,string name,params object[] args){typeof(MainForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(main,args);}
}
