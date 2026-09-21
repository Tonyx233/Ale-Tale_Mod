using System;
using System.Text.RegularExpressions;

namespace TonyMods
{
    public static class YouTubeUrl
    {
        public static bool IsVideo(string value) { return value != null && Regex.IsMatch(value, "^[A-Za-z0-9_-]{11}$"); }
        public static bool IsPlaylist(string value) { return value != null && Regex.IsMatch(value, "^[A-Za-z0-9_-]{10,150}$"); }
        public static bool TryGetSelection(string input, out string video, out string playlist)
        {
            video = ""; playlist = "";
            Uri uri;
            if (!Uri.TryCreate((input ?? "").Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") || !String.IsNullOrEmpty(uri.UserInfo)) return false;
            string host = uri.Host.ToLowerInvariant();
            if (host != "youtu.be" && host != "youtube.com" && host != "www.youtube.com" && host != "m.youtube.com" && host != "music.youtube.com") return false;
            string id;
            bool hasVideo = TryGetVideoId(input, out id);
            if (hasVideo) video = id;
            foreach (string pair in uri.Query.TrimStart('?').Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals > 0 && pair.Substring(0, equals) == "list")
                {
                    string value = Uri.UnescapeDataString(pair.Substring(equals + 1));
                    if (!IsPlaylist(value) || playlist != "") return false;
                    playlist = value;
                }
            }
            if (hasVideo) return true;
            return playlist != "" && uri.AbsolutePath.TrimEnd('/') == "/playlist";
        }

        // Only validated identifiers cross the native/browser boundary; never scripts or URLs.
        public static bool TryGetPlayback(string text, out string video, out string playlist, out long track)
        {
            video = ""; playlist = ""; track = 0;
            string[] parts = (text ?? "").Split('~');
            if (parts.Length == 1 && IsVideo(parts[0])) { video = parts[0]; return true; }
            if (parts.Length != 3 || !Int64.TryParse(parts[2], out track) || track <= 0) return false;
            video = parts[0]; playlist = parts[1];
            return (video == "" || IsVideo(video)) && (playlist == "" || IsPlaylist(playlist)) && (video != "" || playlist != "");
        }
        public static bool TryGetVideoId(string input, out string id)
        {
            id = null;
            Uri uri;
            if (!Uri.TryCreate((input ?? "").Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") || !String.IsNullOrEmpty(uri.UserInfo)) return false;
            string host = uri.Host.ToLowerInvariant();
            string candidate = null;
            if (host == "youtu.be") candidate = uri.AbsolutePath.Trim('/');
            else if (host == "youtube.com" || host == "www.youtube.com" || host == "m.youtube.com" || host == "music.youtube.com")
            {
                string[] parts = uri.AbsolutePath.Trim('/').Split('/');
                if (parts.Length == 2 && (parts[0] == "shorts" || parts[0] == "embed" || parts[0] == "live")) candidate = parts[1];
                else if (uri.AbsolutePath.TrimEnd('/') == "/watch")
                {
                    foreach (string pair in uri.Query.TrimStart('?').Split('&'))
                    {
                        int equals = pair.IndexOf('=');
                        if (equals > 0 && pair.Substring(0, equals) == "v") candidate = Uri.UnescapeDataString(pair.Substring(equals + 1));
                    }
                }
            }
            if (candidate == null || !Regex.IsMatch(candidate, "^[A-Za-z0-9_-]{11}$")) return false;
            id = candidate;
            return true;
        }
    }
}
