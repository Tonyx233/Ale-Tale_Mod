using System;
using System.Text.RegularExpressions;

namespace TonyMods
{
    public static class YouTubeUrl
    {
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
