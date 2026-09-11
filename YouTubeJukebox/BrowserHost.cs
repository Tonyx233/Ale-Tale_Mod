using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TonyMods
{
    internal sealed class BrowserHost : Form
    {
        private readonly WebView2 browser = new WebView2();
        private readonly Label status = new Label();
        private readonly IntPtr parent;
        private readonly bool selfTest;
        private bool ready;
        private string pendingVideo;
        private readonly System.Windows.Forms.Timer parentTimer = new System.Windows.Forms.Timer();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

        [STAThread]
        private static int Main(string[] args)
        {
            bool test = args.Length == 1 && args[0] == "--self-test";
            long handle;
            if (!test && (args.Length != 1 || !Int64.TryParse(args[0], out handle))) return 2;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (BrowserHost host = new BrowserHost(test ? IntPtr.Zero : new IntPtr(Int64.Parse(args[0])), test))
                Application.Run(host);
            return Environment.ExitCode;
        }

        private BrowserHost(IntPtr parentWindow, bool test)
        {
            parent = parentWindow;
            selfTest = test;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(800, 450);
            BackColor = Color.FromArgb(20, 20, 20);
            if (test) Opacity = 0;
            browser.Dock = DockStyle.Fill;
            Controls.Add(browser);
            status.Dock = DockStyle.Fill;
            status.Text = "Loading YouTube player...";
            status.ForeColor = Color.White;
            status.BackColor = BackColor;
            status.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(status);
            Shown += Initialize;
            FormClosed += delegate { browser.Dispose(); parentTimer.Dispose(); };
        }

        private async void Initialize(object sender, EventArgs args)
        {
            try
            {
                if (!selfTest)
                {
                    if (!IsWindow(parent)) { Close(); return; }
                    // The helper owns a real child HWND inside the Unity game window.
                    int style = GetWindowLong(Handle, -16);
                    SetWindowLong(Handle, -16, (style & unchecked((int)~0x80000000)) | 0x40000000);
                    SetParent(Handle, parent);
                    Thread input = new Thread(ReadCommands);
                    input.IsBackground = true;
                    input.Start();
                    parentTimer.Interval = 500;
                    parentTimer.Tick += delegate
                    {
                        if (!IsWindow(parent) || !IsWindowVisible(parent)) Close();
                    };
                    parentTimer.Start();
                }
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TonyAleTaleMods", "WebView2");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, data);
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browser.CoreWebView2.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                browser.CoreWebView2.PermissionRequested += delegate(object s, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                browser.CoreWebView2.DownloadStarting += delegate(object s, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                browser.CoreWebView2.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (selfTest) return;
                    Uri uri;
                    if (e.Uri != "about:blank" && (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) ||
                        uri.Scheme != "https" || uri.Host != "www.youtube.com" || !uri.AbsolutePath.StartsWith("/embed/", StringComparison.Ordinal))) e.Cancel = true;
                };
                browser.CoreWebView2.NavigationCompleted += async delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (selfTest)
                    {
                        string result = await browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('probe') ? document.getElementById('probe').textContent : null");
                        if (result == "null") return;
                        Console.WriteLine("WEBVIEW_SELF_TEST=" + result);
                        Environment.ExitCode = result == "\"ready\"" ? 0 : 1;
                        Close();
                        return;
                    }
                    status.Visible = !e.IsSuccess;
                    if (!e.IsSuccess) status.Text = "Player could not load: " + e.WebErrorStatus;
                    Console.WriteLine(e.IsSuccess ? "NAVIGATION_OK" : "ERROR " + e.WebErrorStatus);
                };
                ready = true;
                Console.WriteLine("READY");
                if (selfTest) browser.NavigateToString("<html><body><p id='probe'>ready</p></body></html>");
                else if (pendingVideo != null) Play(pendingVideo);
                else status.Text = "Paste a YouTube URL above, then press Play.";
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR " + ex.Message.Replace('\n', ' '));
                status.Text = "WebView2 could not start. Verify Microsoft Edge WebView2 Runtime is installed.";
                if (selfTest) { Environment.ExitCode = 1; Close(); }
            }
        }

        private void ReadCommands()
        {
            try
            {
                string line;
                while ((line = Console.ReadLine()) != null)
                {
                    string command = line;
                    if (IsDisposed) return;
                    BeginInvoke((Action)delegate { Command(command); });
                }
                if (!IsDisposed) BeginInvoke((Action)Close);
            }
            catch { if (!IsDisposed) try { BeginInvoke((Action)Close); } catch { } }
        }

        private void Command(string command)
        {
            if (command == "EXIT") { Close(); return; }
            if (command.StartsWith("PLAY ", StringComparison.Ordinal)) { Play(command.Substring(5)); return; }
            if (command == "STOP")
            {
                pendingVideo = null;
                if (ready) browser.CoreWebView2.Navigate("about:blank");
                status.Text = "Playback stopped.";
                status.Visible = true;
                return;
            }
            if (command.StartsWith("RECT ", StringComparison.Ordinal))
            {
                string[] values = command.Substring(5).Split(' ');
                int x, y, w, h;
                if (values.Length == 4 && Int32.TryParse(values[0], out x) && Int32.TryParse(values[1], out y) &&
                    Int32.TryParse(values[2], out w) && Int32.TryParse(values[3], out h) && w >= 200 && h >= 200)
                    Bounds = new Rectangle(x, y, w, h);
            }
        }

        private void Play(string id)
        {
            string checkedId;
            if (!YouTubeUrl.TryGetVideoId("https://youtu.be/" + id, out checkedId)) return;
            pendingVideo = id;
            if (!ready) return;
            status.Text = "Loading YouTube...";
            status.Visible = true;
            string url = "https://www.youtube.com/embed/" + id + "?autoplay=1&playsinline=1&rel=0";
            // YouTube requires application identification for desktop WebView embeds.
            CoreWebView2WebResourceRequest request = browser.CoreWebView2.Environment.CreateWebResourceRequest(
                url, "GET", null, "Referer: https://github.com/Tonyx233/Ale-Tale_Mod\r\n");
            browser.CoreWebView2.NavigateWithWebResourceRequest(request);
        }
    }
}
