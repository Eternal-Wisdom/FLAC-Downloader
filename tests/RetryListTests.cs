using System;
using System.Collections.Generic;
using System.IO;
using PlaylistFlac;

internal static class RetryListTests
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlaylistFlacRetryTest-"+Guid.NewGuid().ToString("N"));
        var first=new Track {Title="KING",Artist="Artist A"};var other=new Track {Title="KING",Artist="Artist B"};var review=new Track {Title="Version",Artist="Artist A"};
        var playlist=new Playlist {Source="test",Tracks=new List<Track>{first,first,other,review}};
        var status=new Dictionary<string,string> {{IndexStore.Key(first),"Failed"},{IndexStore.Key(other),"Failed"},{IndexStore.Key(review),"Review versions"}};
        var now=new DateTime(2026,10,4,0,0,0,DateTimeKind.Utc);
        try
        {
            var list=new RetryList(root,Path.Combine(root,"music"),playlist);list.Update(playlist,status,null,now);
            Check(list.Count==2,"duplicates share a retry, different artists remain separate, review excluded");
            Check(list.Due(now.AddMinutes(14),8).Count==0,"no immediate polling");
            var due=list.Due(now.AddMinutes(15),8);Check(due.Count==2,"initial wait is fifteen minutes");
            list.Update(playlist,status,due,now.AddMinutes(15));
            var restored=new RetryList(root,Path.Combine(root,"music"),playlist);
            Check(restored.Count==2 && restored.Due(now.AddMinutes(29),8).Count==0,"restart preserves list and schedule");
            due=restored.Due(now.AddMinutes(30),1);Check(due.Count==1,"bounded batch");
            restored.Update(playlist,status,due,now.AddMinutes(30));
            Check(restored.Due(now.AddMinutes(45),8).Count==1,"repeated failure backs off");
            status[IndexStore.Key(first)]="Downloaded";status[IndexStore.Key(other)]="Review versions";
            restored.Update(playlist,status,null,now.AddMinutes(45));Check(restored.Count==0,"success and identity review removed");
            Check(new RetryList(root,Path.Combine(root,"other-folder"),playlist).Count==0,"different destinations isolated");
            status[IndexStore.Key(first)]="Failed";
            restored.Update(playlist,status,null,now);
            var selected=new HashSet<string>{IndexStore.Key(first)};
            var retryTime=now.AddMinutes(15);
            foreach(int delay in new[]{15,30,60,120,240,360,360})
            {
                restored.Update(playlist,status,selected,retryTime);
                Check(!restored.Due(retryTime.AddMinutes(delay).AddSeconds(-1),8).Contains(IndexStore.Key(first)),"backoff does not run early at "+delay+" minutes");
                retryTime=retryTime.AddMinutes(delay);
                Check(restored.Due(retryTime,8).Contains(IndexStore.Key(first)),"due after "+delay+" minutes");
            }
            var queuePath=Directory.GetFiles(Path.Combine(root,"retry-lists"),"*.json")[0];
            var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
            var saved=serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(queuePath));
            saved[0].Paused=true;saved[0].LastReason="Failed: No matching results";
            File.WriteAllText(queuePath,serializer.Serialize(saved));
            var paused=new RetryList(root,Path.Combine(root,"music"),playlist);
            Check(paused.Due(retryTime.AddDays(10),8).Count==0,"paused entry survives restart and never auto retries");
            paused.Update(playlist,status,null,retryTime);
            saved=serializer.Deserialize<List<RetryEntry>>(File.ReadAllText(queuePath));
            Check(saved[0].Paused && saved[0].LastReason=="Failed: No matching results","ordinary updates preserve pause and failure detail");
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static void Check(bool value,string message){if(!value)throw new Exception("Retry list: "+message);}
}
