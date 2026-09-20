using System;
namespace TonyMods
{
    [Serializable] public sealed class JukeboxState
    {
        public int protocol = 1;
        public string op = "state", video = "";
        public ulong box;
        public long revision;
        public double position, stamp;
        public bool paused;
        public static bool Finite(double n) { return !Double.IsNaN(n) && !Double.IsInfinity(n); }
        public bool Valid()
        {
            string id;
            return protocol == 1 && revision >= 0 && Finite(position) && position >= 0 && position <= 604800 &&
                Finite(stamp) && stamp >= 0 && video != null &&
                (video == "" || (video.Length == 11 && YouTubeUrl.TryGetVideoId("https://youtu.be/" + video, out id)));
        }
        public double PositionAt(double now)
        { return Math.Min(604800, position + (paused || video == "" ? 0 : Math.Max(0, now - stamp))); }
        public bool TryApply(JukeboxState request, double now, out JukeboxState next)
        {
            next = this;
            if (request == null || !request.Valid() || !Finite(now) || now < 0) return false;
            string action = request.op;
            if (action != "play" && action != "stop" && action != "pause" && action != "resume" && action != "seek") return false;
            if (action == "play" ? request.video == "" : video == "" || box != request.box || revision != request.revision) return false;
            next = new JukeboxState { box = request.box, video = video, position = PositionAt(now), stamp = now, paused = paused, revision = revision + 1 };
            if (action == "play") { next.video = request.video; next.position = 0; next.paused = false; }
            if (action == "stop") { next.video = ""; next.position = 0; }
            if (action == "pause") next.paused = true;
            if (action == "resume") next.paused = false;
            if (action == "seek") next.position = request.position;
            return true;
        }
        public static int DistanceVolume(double distance, double volume)
        {
            if (!Finite(distance) || !Finite(volume) || distance >= 30) return 0;
            double gain = Math.Max(0, Math.Min(1, (30 - Math.Max(3, distance)) / 27));
            return (int)Math.Round(Math.Max(0, Math.Min(100, volume)) * gain * gain);
        }
    }
}
