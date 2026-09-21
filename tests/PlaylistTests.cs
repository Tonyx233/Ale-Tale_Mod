using System;
using TonyMods;
class PlaylistTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Main()
    {
        const string video="pEdxU1F-FE8",list="PLdrk_BM8q45oxliginXQPrQuoTk2ppFjV";
        string v,p;long token;
        Check(YouTubeUrl.TryGetSelection("https://www.youtube.com/watch?v="+video+"&list="+list,out v,out p) && v==video && p==list,"video + playlist");
        Check(YouTubeUrl.TryGetSelection("https://www.youtube.com/playlist?list="+list,out v,out p) && v=="" && p==list,"playlist only");
        Check(YouTubeUrl.TryGetSelection("https://youtu.be/"+video,out v,out p) && v==video && p=="","single video");
        foreach(string bad in new[]{"https://youtube.com.evil/watch?v="+video+"&list="+list,"https://youtube.com/watch?v=bad&list="+list,
            "https://youtube.com/playlist?list=abc%27alert","https://youtube.com/playlist?list="+list+"&list="+list,"https://youtube.com/unknown?list="+list})
            Check(!YouTubeUrl.TryGetSelection(bad,out v,out p),"bad URL");
        Check(YouTubeUrl.TryGetPlayback("~"+list+"~7",out v,out p,out token) && token==7,"resolver spec");
        Check(!YouTubeUrl.TryGetPlayback(video+"~~-1",out v,out p,out token),"negative token");
        Check(!YouTubeUrl.TryGetPlayback(video+"~';evil~1",out v,out p,out token),"script rejected");
        Check(JukeboxState.Tracks(video+","+video).Length==2,"duplicates preserved");
        Check(JukeboxState.Tracks("bad")==null && JukeboxState.Tracks("")==null,"bad or empty list");
        var many=new string[201];for(int i=0;i<many.Length;i++)many[i]=video;
        Check(JukeboxState.Tracks(String.Join(",",many))==null,"bounded playlist");
        Console.WriteLine("PASS: "+checks+" playlist URL and resolver validation checks");
    }
}
