using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
class HostTests : Form
{
    Process child;
    string helper;
    bool checkedChild;
    Timer timer = new Timer();
    DateTime deadline;
    delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [STAThread] static int Main(string[] args)
    {
        using (var form = new HostTests(args[0])) Application.Run(form);
        return Environment.ExitCode;
    }
    HostTests(string path)
    {
        helper = path;
        Opacity = 0;
        ShowInTaskbar = false;
        Width = 1000; Height = 700;
        Shown += Start;
    }
    void Start(object sender, EventArgs args)
    {
        child = new Process();
        child.StartInfo = new ProcessStartInfo(helper, Handle.ToInt64().ToString());
        child.StartInfo.UseShellExecute = false;
        child.StartInfo.CreateNoWindow = true;
        child.StartInfo.RedirectStandardInput = true;
        child.StartInfo.RedirectStandardOutput = true;
        child.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
        {
            if (e.Data == "READY") BeginInvoke((Action)CheckChild);
            if (e.Data != null) Console.WriteLine(e.Data);
        };
        child.Start(); child.BeginOutputReadLine();
        child.StandardInput.WriteLine("RECT 20 100 800 450"); child.StandardInput.Flush();
        deadline = DateTime.UtcNow.AddSeconds(15);
        timer.Interval = 100;
        timer.Tick += delegate
        {
            if (child.HasExited) { Environment.ExitCode = checkedChild && child.ExitCode == 0 ? 0 : 1; Finish(); }
            else if (DateTime.UtcNow > deadline) { child.Kill(); Environment.ExitCode = 1; Console.WriteLine("TIMEOUT"); Finish(); }
        };
        timer.Start();
    }
    void CheckChild()
    {
        EnumWindows(delegate(IntPtr hwnd, IntPtr data)
        {
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid == (uint)child.Id) checkedChild = true;
            return true;
        }, IntPtr.Zero);
        Console.WriteLine("TOP_LEVEL_HWND=" + checkedChild);
        child.StandardInput.WriteLine("STOP");
        child.StandardInput.WriteLine("EXIT"); child.StandardInput.Flush();
    }
    void Finish() { timer.Stop(); timer.Dispose(); child.Dispose(); Close(); }
}
