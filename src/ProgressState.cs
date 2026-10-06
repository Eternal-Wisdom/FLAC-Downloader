using System;
using System.Collections.Generic;

namespace PlaylistFlac
{
    internal sealed class PlaylistProgressState
    {
        private readonly Dictionary<string,int> copies=new Dictionary<string,int>();
        private readonly Dictionary<string,string> states=new Dictionary<string,string>();
        public int Total {get;private set;}
        public int Completed {get;private set;}
        public int Failed {get;private set;}
        public int Review {get;private set;}
        public int Ready {get {return Total-Completed-Failed-Review;}}
        public PlaylistProgressState(Playlist playlist,IDictionary<string,string> saved)
        {
            foreach(var track in playlist.Tracks)
            {
                string key=IndexStore.Key(track), state;
                int n; copies.TryGetValue(key,out n); copies[key]=n+1;
                state=saved!=null && saved.TryGetValue(key,out state)?state:"Ready";
                states[key]=state;Total++;
                if(state=="Downloaded")Completed++;else if(state=="Failed")Failed++;else if(state=="Review versions")Review++;
            }
        }
        public string Status(string key){string value;return states.TryGetValue(key,out value)?value:"Ready";}
        public bool Set(string key,string state)
        {
            int n;string old;
            if(!copies.TryGetValue(key,out n) || !states.TryGetValue(key,out old) || old==state || old=="Downloaded")return false;
            if(old=="Failed")Failed-=n;
            if(old=="Review versions")Review-=n;
            if(state=="Downloaded")Completed+=n;else if(state=="Failed")Failed+=n;else if(state=="Review versions")Review+=n;
            states[key]=state;return true;
        }
    }
}
