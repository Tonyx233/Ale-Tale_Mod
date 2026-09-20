using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
class SpeakerPairTests : Form
{
    readonly Process[] children = new Process[2];
    readonly bool[] ready = new bool[2];
    readonly string[] probes = new string[2];
    readonly Timer timer = new Timer();
    readonly Stopwatch elapsed = new Stopwatch();
    string helper;
    bool started, late, asked, checkedPlay, askedPause, checkedPause, finished;
    [STAThread] static int Main(string[] args) { using(var f=new SpeakerPairTests(args[0])) Application.Run(f); return Environment.ExitCode; }
    SpeakerPairTests(string path)
    {
        helper=path; Opacity=0; ShowInTaskbar=false;
        Shown+=delegate { for(int i=0;i<2;i++) StartChild(i); elapsed.Start(); timer.Interval=250; timer.Tick+=TickTest; timer.Start(); };
    }
    void StartChild(int index)
    {
        var p=new Process(); children[index]=p;
        p.StartInfo=new ProcessStartInfo(helper,Handle.ToInt64()+" --background") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
        p.OutputDataReceived+=delegate(object s,DataReceivedEventArgs e)
        {
            if(e.Data==null||IsDisposed)return;
            BeginInvoke((Action)delegate {
                Console.WriteLine("Peer"+index+" "+e.Data);
                if(e.Data=="READY")ready[index]=true;
                if(e.Data.StartsWith("PROBE ")) probes[index]=e.Data.Substring(6).Trim('"');
                if(e.Data.StartsWith("PLAYER_ERROR")) Finish("Player "+index+": "+e.Data);
            });
        };
        p.Start();p.BeginOutputReadLine();
    }
    void Send(int i,string text) { children[i].StandardInput.WriteLine(text);children[i].StandardInput.Flush(); }
    void TickTest(object sender,EventArgs e)
    {
        try
        {
            double t=elapsed.Elapsed.TotalSeconds;
            if(t>50){Finish("Timeout");return;}
            if(!started)
            {
                if(!ready[0]||!ready[1])return;
                started=true; elapsed.Restart(); Send(0,"PLAY M7lc1UVf-VE"); return;
            }
            if(t>=4&&!late){late=true;Send(1,"PLAY M7lc1UVf-VE");}
            for(int i=0;i<2;i++)
            {
                if(i==1&&!late)continue;
                Send(i,"VOLUME "+(i==0?65:0));
                Send(i,"SYNC "+(checkedPlay?12:t).ToString("F3",CultureInfo.InvariantCulture)+" "+(checkedPlay?"1":"0")+" 0");
            }
            if(t>12&&!asked){asked=true;Send(0,"PROBE");Send(1,"PROBE");}
            if(asked&&!checkedPlay&&probes[0]!=null&&probes[1]!=null)
            {
                double[] a=Parse(probes[0]),b=Parse(probes[1]);
                if(a[1]!=1||b[1]!=1||Math.Abs(a[0]-b[0])>2.5||a[2]!=65||b[2]!=0) {Finish("Playing mismatch: "+probes[0]+" / "+probes[1]);return;}
                Console.WriteLine("PAIR_PLAY=near:"+probes[0]+" far:"+probes[1]);checkedPlay=true;probes[0]=probes[1]=null;
            }
            if(t>16&&checkedPlay&&!askedPause){askedPause=true;Send(0,"PROBE");Send(1,"PROBE");}
            if(askedPause&&!checkedPause&&probes[0]!=null&&probes[1]!=null)
            {
                double[] a=Parse(probes[0]),b=Parse(probes[1]);
                if(a[1]!=2||b[1]!=2||Math.Abs(a[0]-b[0])>2.5){Finish("Paused mismatch");return;}
                checkedPause=true; Console.WriteLine("PAIR_PAUSE=PASS");Finish(null);
            }
        }
        catch(Exception ex){Finish(ex.Message);}
    }
    double[] Parse(string s){return Array.ConvertAll(s.Split(','),x=>Double.Parse(x,CultureInfo.InvariantCulture));}
    void Finish(string error)
    {
        if(finished)return;finished=true;timer.Stop();
        for(int i=0;i<2;i++) if(children[i]!=null)
        {
            try {if(!children[i].HasExited){Send(i,"STOP");Send(i,"EXIT");if(!children[i].WaitForExit(3000))children[i].Kill();}}catch{}
            children[i].Dispose();
        }
        Console.WriteLine(error==null?"SPEAKER_PAIR_TEST=late-join,clock,independent-volume,pause,cleanup:PASS":"SPEAKER_PAIR_TEST_FAIL "+error);
        Environment.ExitCode=error==null?0:1;Close();
    }
}
