using System;
using System.Collections.Generic;
namespace TonyMods
{
    [Serializable] public sealed class JukeboxEntry
    {
        public long id;
        public string video = "", title = "";
        public ulong addedBy;
        public JukeboxEntry Copy() { return (JukeboxEntry)MemberwiseClone(); }
        public bool Valid() { return id > 0 && YouTubeUrl.IsVideo(video) && title != null && title.Length <= 100 && title.IndexOfAny(new[] {'\r','\n','\0'}) < 0; }
    }
    [Serializable] public sealed class JukeboxRequest
    {
        public int protocol = 3;
        public string op = "", video = "", playlist = "";
        public ulong box;
        public long item, before, track, playRevision;
        public double position;
        public bool Valid()
        {
            return protocol == 3 && op != null && op.Length <= 16 && item >= 0 && before >= 0 && track >= 0 && playRevision >= 0 &&
                JukeboxState.Finite(position) && position >= 0 && position <= 604800 && video != null && playlist != null &&
                (video == "" || YouTubeUrl.IsVideo(video)) && (playlist == "" || YouTubeUrl.IsPlaylist(playlist));
        }
    }
    [Serializable] public sealed class JukeboxState
    {
        public const int MaxTracks = 200, MaxHistory = 50;
        public int protocol = 3, repeat, failures;
        public ulong box;
        public long revision, playRevision, track, serial;
        public double position, stamp;
        public bool paused, stopped;
        public string notice = "";
        public JukeboxEntry current;
        public JukeboxEntry[] pending = new JukeboxEntry[0], history = new JukeboxEntry[0];
        public string video { get { return current == null ? "" : current.video; } }
        public bool Active { get { return current != null && !stopped; } }
        public static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        public bool Valid()
        {
            if (protocol != 3 || revision < 0 || playRevision < 0 || track < 0 || serial < 0 || repeat < 0 || repeat > 2 || failures < 0 || failures > 3 ||
                !Finite(position) || position < 0 || position > 604800 || !Finite(stamp) || stamp < 0 || notice == null || notice.Length > 160 ||
                pending == null || history == null || pending.Length > MaxTracks || history.Length > MaxHistory) return false;
            var ids = new HashSet<long>();
            if (current != null && (!current.Valid() || current.id > serial || !ids.Add(current.id))) return false;
            foreach (var entries in new[] {pending, history}) foreach (var e in entries)
                if (e == null || !e.Valid() || e.id > serial || !ids.Add(e.id)) return false;
            return true;
        }
        public static string[] Tracks(string csv)
        {
            if (String.IsNullOrEmpty(csv) || csv.Length > MaxTracks * 12 - 1) return null;
            string[] songs = csv.Split(',');
            foreach (string song in songs) if (!YouTubeUrl.IsVideo(song)) return null;
            return songs;
        }
        public double PositionAt(double now)
        { return Math.Min(604800, position + (!Active || paused ? 0 : Math.Max(0, now - stamp))); }
        private JukeboxState Copy()
        {
            var copy = (JukeboxState)MemberwiseClone();
            copy.pending = (JukeboxEntry[])pending.Clone(); copy.history = (JukeboxEntry[])history.Clone();
            copy.revision++; copy.notice = ""; return copy;
        }
        private void Playback(double now, bool newTrack)
        { position = newTrack ? 0 : PositionAt(now); stamp = now; playRevision++; if (newTrack) track++; }
        private void Remember()
        {
            if (current == null) return;
            var h = new List<JukeboxEntry>(history); h.Add(current);
            if (h.Count > MaxHistory) h.RemoveAt(0); history = h.ToArray();
        }
        private void SelectNext(double now, bool natural)
        {
            if (natural && repeat == 1 && current != null) { Playback(now, true); paused = stopped = false; return; }
            var q = new List<JukeboxEntry>(pending);
            JukeboxEntry previous = current;
            Remember(); current = q.Count == 0 ? null : q[0];
            if (q.Count > 0) q.RemoveAt(0);
            if (repeat == 2 && previous != null)
            {
                var again = previous.Copy(); again.id = ++serial;
                if (current == null) current = again; else q.Add(again);
            }
            pending = q.ToArray(); Playback(now, true); paused = stopped = false;
        }
        public bool TryInsert(string mode, string[] videos, ulong requester, ulong targetBox, double now, out JukeboxState next, out string error)
        {
            next = this; error = "Invalid selection.";
            if ((mode != "append" && mode != "insert" && mode != "now") || videos == null || videos.Length == 0 || videos.Length > MaxTracks || !Finite(now) || now < 0) return false;
            foreach (string id in videos) if (!YouTubeUrl.IsVideo(id)) return false;
            if ((current != null || pending.Length > 0) && targetBox != box) { error = "Use the active jukebox to edit its queue."; return false; }
            int consumed = mode == "now" || (current == null && !stopped) ? 1 : 0;
            if (pending.Length + videos.Length - consumed > MaxTracks) { error = "Queue full: maximum 200 pending songs. Nothing was added."; return false; }
            next = Copy(); next.box = targetBox;
            var incoming = new List<JukeboxEntry>();
            foreach (string id in videos) incoming.Add(new JukeboxEntry { id=++next.serial, video=id, addedBy=requester });
            var q = new List<JukeboxEntry>(pending);
            if (mode == "append") q.AddRange(incoming); else q.InsertRange(0,incoming);
            if (consumed == 1)
            {
                next.Remember(); next.current = q[0]; q.RemoveAt(0); next.Playback(now,true);
                next.paused = next.stopped = false; next.failures = 0;
            }
            next.pending = q.ToArray(); error = ""; return true;
        }
        public bool TryApply(JukeboxRequest request, double now, out JukeboxState next, out string error)
        {
            next = this; error = "Queue changed. Refresh and try again.";
            if (request == null || !request.Valid() || !Finite(now) || now < 0 || request.box != box) return false;
            string action = request.op;
            bool queueEdit = action == "move" || action == "remove" || action == "clear" || action == "repeat" || action == "playitem";
            if (!queueEdit && (request.track != track || request.playRevision != playRevision)) return false;
            next = Copy(); var q = new List<JukeboxEntry>(pending);
            int selected = q.FindIndex(e => e.id == request.item);
            switch (action)
            {
                case "move":
                    if (selected < 0 || request.item == request.before) return false;
                    JukeboxEntry moving = q[selected]; q.RemoveAt(selected);
                    int before = request.before == 0 ? q.Count : q.FindIndex(e => e.id == request.before);
                    if (before < 0) return false;
                    q.Insert(before,moving); next.pending=q.ToArray(); break;
                case "remove":
                    if (selected < 0) return false;
                    q.RemoveAt(selected); next.pending=q.ToArray(); break;
                case "clear": next.pending = new JukeboxEntry[0]; break;
                case "repeat": next.repeat=(repeat+1)%3; break;
                case "playitem":
                    if (selected < 0) return false;
                    next.Remember(); next.current=q[selected]; q.RemoveAt(selected); next.pending=q.ToArray();
                    next.Playback(now,true); next.paused=next.stopped=false; next.failures=0; break;
                case "previous":
                    if (history.Length == 0 || (current != null && pending.Length == MaxTracks)) return false;
                    if (current != null) q.Insert(0,current);
                    var h = new List<JukeboxEntry>(history); next.current=h[h.Count-1]; h.RemoveAt(h.Count-1);
                    next.history=h.ToArray(); next.pending=q.ToArray(); next.Playback(now,true); next.paused=next.stopped=false; next.failures=0; break;
                case "next": next.SelectNext(now,false); next.failures=0; break;
                case "stop": next.Playback(now,true); next.stopped=true; next.paused=false; break;
                case "pause":
                    if (!Active) return false;
                    next.Playback(now,false); next.paused=true; break;
                case "resume":
                    if (current == null) { if (pending.Length == 0) return false; next.SelectNext(now,false); }
                    else { next.Playback(now,false); next.stopped=next.paused=false; }
                    break;
                case "seek":
                    if (current == null) return false;
                    next.Playback(now,false); next.position=request.position; break;
                default: return false;
            }
            error=""; return true;
        }
        public bool TryFinish(long token, string error, double now, out JukeboxState next)
        {
            next=this;
            if (!Active || token != track || !Finite(now) || now < 0 || (error == "" && paused)) return false;
            next=Copy(); bool unavailable=error=="100" || error=="101" || error=="150";
            if (error=="" || (unavailable && failures<2))
            {
                next.SelectNext(now,error==""); next.failures=error==""?0:failures+1;
                if (error!="") next.notice="Skipped unavailable video (YouTube "+error+").";
            }
            else { next.Playback(now,true); next.stopped=true; next.notice="Playback stopped: "+error+". Queue retained."; }
            return true;
        }
        public JukeboxState WithNotice(string text)
        { var next=Copy(); next.notice=text.Length>160?text.Substring(0,160):text; return next; }
        public JukeboxState WithTitle(string id, string title)
        {
            if (!YouTubeUrl.IsVideo(id) || String.IsNullOrWhiteSpace(title)) return this;
            title=title.Replace('\r',' ').Replace('\n',' ').Replace('\0',' '); if(title.Length>100)title=title.Substring(0,100);
            var next=Copy(); bool changed=false;
            if(current!=null && current.video==id && current.title!=title) {next.current=current.Copy();next.current.title=title;changed=true;}
            foreach(var entries in new[]{next.pending,next.history}) for(int i=0;i<entries.Length;i++)
                if(entries[i].video==id && entries[i].title!=title) {entries[i]=entries[i].Copy();entries[i].title=title;changed=true;}
            return changed?next:this;
        }
        public static int DistanceVolume(double distance, double volume)
        {
            if (!Finite(distance) || !Finite(volume) || distance >= 30) return 0;
            double gain=Math.Max(0,Math.Min(1,(30-Math.Max(3,distance))/27));
            return (int)Math.Round(Math.Max(0,Math.Min(100,volume))*gain*gain);
        }
    }
}
