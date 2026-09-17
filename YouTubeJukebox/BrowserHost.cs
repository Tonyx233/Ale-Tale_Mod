using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TonyMods
{
    internal sealed class BrowserHost : Form
    {
        private readonly WebView2 browser = new WebView2();
        private readonly Label status = new Label();
        private readonly TextBox address = new TextBox();
        private readonly bool playbackTest;
        private const string PlayerOrigin = "https://tony.teammatehealthbars";
        private readonly System.Windows.Forms.Timer testTimer = new System.Windows.Forms.Timer();
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
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static int Main(string[] args)
        {
            bool playback = args.Length >= 1 && args[0] == "--playback-test";
            bool test = playback || (args.Length == 1 && args[0] == "--self-test");
            long handle;
            if (!test && (args.Length != 1 || !Int64.TryParse(args[0], out handle))) return 2;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (BrowserHost host = new BrowserHost(test ? IntPtr.Zero : new IntPtr(Int64.Parse(args[0])), test, playback))
            {
                if (playback && args.Length == 2)
                {
                    string id;
                    if (!YouTubeUrl.TryGetVideoId("https://youtu.be/" + args[1], out id)) return 2;
                    host.pendingVideo = id;
                }
                Application.Run(host);
            }
            return Environment.ExitCode;
        }

        private BrowserHost(IntPtr parentWindow, bool test, bool playback)
        {
            parent = parentWindow;
            selfTest = test;
            playbackTest = playback;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(800, 450);
            BackColor = Color.FromArgb(20, 20, 20);
            if (test) Opacity = 0;
            browser.Dock = DockStyle.Fill;
            Panel content = new Panel { Dock = DockStyle.Fill };
            Controls.Add(content);
            content.Controls.Add(browser);
            status.Dock = DockStyle.Fill;
            status.Text = "Loading YouTube player...";
            status.ForeColor = Color.White;
            status.BackColor = BackColor;
            status.TextAlign = ContentAlignment.MiddleCenter;
            content.Controls.Add(status);
            TableLayoutPanel toolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 4, Padding = new Padding(2) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            address.Dock = DockStyle.Fill;
            address.MaxLength = 2048;
            address.ShortcutsEnabled = true;
            address.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { PlayAddress(); e.SuppressKeyPress = true; } };
            toolbar.Controls.Add(address, 0, 0);
            Button play = new Button { Text = "Play", Dock = DockStyle.Fill };
            Button stop = new Button { Text = "Stop", Dock = DockStyle.Fill };
            Button clear = new Button { Text = "Clear", Dock = DockStyle.Fill };
            play.Click += delegate { PlayAddress(); };
            stop.Click += delegate { Command("STOP"); };
            clear.Click += delegate { address.Clear(); address.Focus(); };
            toolbar.Controls.Add(play, 1, 0); toolbar.Controls.Add(stop, 2, 0); toolbar.Controls.Add(clear, 3, 0);
            Controls.Add(toolbar);
            Shown += Initialize;
            FormClosed += delegate { browser.Dispose(); parentTimer.Dispose(); testTimer.Dispose(); };
        }

        private async void Initialize(object sender, EventArgs args)
        {
            try
            {
                if (selfTest) TestTextEditing();
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
                if (selfTest) data = Path.Combine(Path.GetTempPath(), "TonyJukeboxTests", Guid.NewGuid().ToString("N"));
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, data);
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.AddWebResourceRequestedFilter(PlayerOrigin + "/*", CoreWebView2WebResourceContext.Document);
                browser.CoreWebView2.WebResourceRequested += delegate(object s, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    Uri requestUri = new Uri(e.Request.Uri);
                    string id;
                    if (requestUri.GetLeftPart(UriPartial.Authority) != PlayerOrigin ||
                        !YouTubeUrl.TryGetVideoId("https://youtu.be/" + requestUri.Query.TrimStart('?'), out id)) return;
                    // A real HTTPS document gives the iframe an automatic browser-generated Referer.
                    byte[] html = Encoding.UTF8.GetBytes(PlayerHtml(id, playbackTest));
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(html), 200, "OK",
                        "Content-Type: text/html; charset=utf-8\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nCache-Control: no-store\r\n");
                };
                browser.CoreWebView2.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
                {
                    if (!e.Source.StartsWith(PlayerOrigin + "/", StringComparison.Ordinal)) return;
                    string value = e.TryGetWebMessageAsString();
                    Console.WriteLine(value);
                    if (playbackTest && (value == "PLAYER_STATE 1" || value.StartsWith("PLAYER_ERROR ", StringComparison.Ordinal)))
                    { Environment.ExitCode = value == "PLAYER_STATE 1" ? 0 : 1; Close(); }
                };
                browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
                browser.CoreWebView2.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                browser.CoreWebView2.PermissionRequested += delegate(object s, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                browser.CoreWebView2.DownloadStarting += delegate(object s, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; };
                browser.CoreWebView2.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (selfTest && !playbackTest) return;
                    Uri uri;
                    if (e.Uri != "about:blank" && (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) ||
                        uri.GetLeftPart(UriPartial.Authority) != PlayerOrigin || uri.AbsolutePath != "/player")) e.Cancel = true;
                };
                browser.CoreWebView2.NavigationCompleted += async delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (selfTest && !playbackTest)
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
                if (selfTest)
                {
                    testTimer.Interval = playbackTest ? 45000 : 12000;
                    testTimer.Tick += delegate { Console.WriteLine("TEST_TIMEOUT"); Environment.ExitCode = 1; Close(); };
                    testTimer.Start();
                    if (playbackTest) Play(pendingVideo ?? "M7lc1UVf-VE");
                    else browser.NavigateToString("<html><body><p id='probe'>ready</p></body></html>");
                }
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
            Console.WriteLine("LOCAL_PLAY " + id);
            browser.CoreWebView2.Navigate(PlayerOrigin + "/player?" + id);
        }

        private void PlayAddress()
        {
            string id;
            if (!YouTubeUrl.TryGetVideoId(address.Text, out id))
            { status.Text = "Enter a valid YouTube video URL."; status.Visible = true; address.Focus(); return; }
            Play(id);
        }

        private void TestTextEditing()
        {
            address.Text = "abcX"; address.Select(4, 0);
            SendMessage(address.Handle, 0x0102, new IntPtr(8), IntPtr.Zero); // WM_CHAR Backspace
            if (address.Text != "abc") throw new InvalidOperationException("Backspace test failed");
            address.Select(1, 0);
            SendMessage(address.Handle, 0x0100, new IntPtr(46), IntPtr.Zero); // WM_KEYDOWN Delete
            if (address.Text != "ac") throw new InvalidOperationException("Delete test failed");
            address.Text = "https://youtu.be/wrong"; address.SelectAll();
            SendMessage(address.Handle, 0x0102, new IntPtr(8), IntPtr.Zero);
            if (address.Text != "") throw new InvalidOperationException("Selection deletion test failed");
            address.Text = "wrong"; address.Clear();
            if (address.Text != "") throw new InvalidOperationException("Clear test failed");
            Console.WriteLine("TEXT_EDIT_SELF_TEST=Backspace,Delete,selection,Clear:PASS");
        }

        private static string PlayerHtml(string id, bool muted)
        {
            return "<!doctype html><html><head><meta name='referrer' content='strict-origin-when-cross-origin'>" +
                "<style>html,body,#player{margin:0;width:100%;height:100%;background:#141414;overflow:hidden}</style></head><body>" +
                "<div id='player'></div><script>function report(s){chrome.webview.postMessage(s)}" +
                "function onYouTubeIframeAPIReady(){new YT.Player('player',{videoId:'" + id + "'," +
                "playerVars:{autoplay:1,playsinline:1,origin:'" + PlayerOrigin + "'},events:{" +
                "onReady:function(e){report('PLAYER_READY');" + (muted ? "e.target.mute();" : "") + "e.target.playVideo()}," +
                "onStateChange:function(e){report('PLAYER_STATE '+e.data)},onError:function(e){report('PLAYER_ERROR '+e.data)}}})}" +
                "</script><script src='https://www.youtube.com/iframe_api'></script></body></html>";
        }
    }
}
