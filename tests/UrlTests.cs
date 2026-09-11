using System;
using TonyMods;
class UrlTests
{
    static int Main()
    {
        string id;
        string[] good = { "https://www.youtube.com/watch?v=M7lc1UVf-VE&list=anything", "https://youtu.be/M7lc1UVf-VE?t=30", "https://m.youtube.com/watch?v=M7lc1UVf-VE", "https://www.youtube.com/shorts/M7lc1UVf-VE", "https://www.youtube.com/live/M7lc1UVf-VE", "https://www.youtube.com/embed/M7lc1UVf-VE" };
        string[] bad = { null, "", "M7lc1UVf-VE", "file:///tmp/test", "javascript:alert(1)", "https://youtube.com.evil.test/watch?v=M7lc1UVf-VE", "https://youtube.com@evil.test/watch?v=M7lc1UVf-VE", "https://www.youtube.com/watch?v=bad", "https://youtu.be/M7lc1UVf-VE/extra", "https://www.youtube.com/playlist?list=anything", "https://www.youtube.com/watch?v=%22%3Ealert(1)" };
        foreach (string url in good) if (!YouTubeUrl.TryGetVideoId(url, out id) || id != "M7lc1UVf-VE") throw new Exception("Rejected valid URL: " + url);
        foreach (string url in bad) if (YouTubeUrl.TryGetVideoId(url, out id)) throw new Exception("Accepted invalid URL: " + url);
        Console.WriteLine("PASS: " + (good.Length + bad.Length) + " YouTube URL cases");
        return 0;
    }
}
