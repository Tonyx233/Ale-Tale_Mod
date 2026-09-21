using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: System.Reflection.AssemblyVersion("0.8.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.8.1.0")]

namespace TonyMods
{
    internal sealed partial class BrowserHost : Form
    {
        private readonly WebView2 browser = new WebView2();
        private readonly Label status = new Label();
        private readonly TextBox address = new TextBox();
        private readonly Label playlistStatus = new Label();
        private readonly bool playbackTest;
        private const string PlayerOrigin = "https://tony.teammatehealthbars";
        private readonly System.Windows.Forms.Timer testTimer = new System.Windows.Forms.Timer();
        private readonly IntPtr parent;
        private readonly bool selfTest;
        private bool background, allowClose, playerReady, syncBusy, syncDirty, forceSeek;
        private bool desiredPaused, canControl;
        private int desiredVolume;
        private double desiredPosition;
        private DateTime syncAt = DateTime.UtcNow;
        private bool speakerTest, speakerTestStarted;
        private bool hideWhenPlaying;
        private int playerState = -1;
        private DateTime hideDeadline;
        protected override bool ShowWithoutActivation { get { return selfTest || background || hideWhenPlaying; } }
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
            bool speaker = args.Length >= 1 && args[0] == "--speaker-test";
            bool playback = speaker || (args.Length >= 1 && args[0] == "--playback-test");
            bool test = playback || (args.Length == 1 && args[0] == "--self-test");
            long handle;
            if (!test && (args.Length < 1 || args.Length > 2 || !Int64.TryParse(args[0], out handle) || (args.Length == 2 && args[1] != "--background"))) return 2;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (BrowserHost host = new BrowserHost(test ? IntPtr.Zero : new IntPtr(Int64.Parse(args[0])), test, playback))
            {
                host.speakerTest = speaker;
                host.background = !test && args.Length == 2;
                if (host.background) { host.Opacity = 0; host.ShowInTaskbar = false; }
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
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = !test;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 650);
            MinimumSize = new Size(1000, 600);
            Text = "YouTube Jukebox - Tony";
            BackColor = Color.FromArgb(20, 20, 20);
            if (test) Opacity = 0;
            browser.Dock = DockStyle.Fill;
            Panel content = new Panel { Dock = DockStyle.Fill };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            var split = new SplitContainer { Width = 1100, Dock = DockStyle.Fill, SplitterDistance = 650, Panel1MinSize = 400, Panel2MinSize = 340 };
            layout.Controls.Add(split, 0, 3);
            split.Panel1.Controls.Add(content);
            BuildQueuePanel(split.Panel2);
            content.Controls.Add(browser);
            status.Dock = DockStyle.Fill;
            status.Text = "Loading YouTube player...";
            status.ForeColor = Color.White;
            status.BackColor = BackColor;
            status.TextAlign = ContentAlignment.MiddleCenter;
            content.Controls.Add(status);
            TableLayoutPanel toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(4) };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            address.Dock = DockStyle.Fill;
            address.MaxLength = 2048;
            address.ShortcutsEnabled = true;
            address.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { PlayAddress(); e.SuppressKeyPress = true; } };
            toolbar.Controls.Add(address, 0, 0);
            Button play = new Button { Text = "Add last", Dock = DockStyle.Fill };
            Button insert = new Button { Text = "Play next", Dock = DockStyle.Fill };
            Button immediate = new Button { Text = "Play now", Dock = DockStyle.Fill };
            Button clear = new Button { Text = "Clear", Dock = DockStyle.Fill };
            play.Click += delegate { PlayAddress(); };
            insert.Click += delegate { PlayAddress("insert"); };
            immediate.Click += delegate { PlayAddress("now"); };
            clear.Click += delegate { address.Clear(); address.Focus(); };
            Button paste = new Button { Text = "Paste", Dock = DockStyle.Fill };
            paste.Click += delegate { try { address.Paste(); address.Focus(); } catch (Exception ex) { Console.WriteLine("PASTE_ERROR " + ex.Message); } };
            toolbar.Controls.Add(paste, 1, 0); toolbar.Controls.Add(play, 2, 0); toolbar.Controls.Add(insert, 3, 0); toolbar.Controls.Add(immediate, 4, 0); toolbar.Controls.Add(clear, 5, 0);
            layout.Controls.Add(toolbar, 0, 0);
            FlowLayoutPanel transport = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            Button pause = new Button { Text = "Pause", Width = 65 };
            Button resume = new Button { Text = "Resume", Width = 70 };
            NumericUpDown seconds = new NumericUpDown { Maximum = 604800, Width = 80 };
            Button seek = new Button { Text = "Seek (sec)", Width = 85 };
            Button hide = new Button { Text = "Back to game", Width = 125 };
            Button previous = new Button { Text = "Previous", Width = 75 };
            Button next = new Button { Text = "Next", Width = 60 };
            Button loop = new Button { Text = "Repeat mode", Width = 100 };
            Button stop = new Button { Text = "Stop", Width = 60 };
            previous.Click += delegate { QueueRequest("previous"); };
            next.Click += delegate { QueueRequest("next"); };
            loop.Click += delegate { QueueRequest("repeat"); };
            stop.Click += delegate { QueueRequest("stop"); };
            pause.Click += delegate { QueueRequest("pause"); };
            resume.Click += delegate { QueueRequest("resume"); };
            seek.Click += delegate { QueueRequest("seek",0,0,(double)seconds.Value); };
            hide.Click += delegate { HidePlayer(); };
            transport.Controls.AddRange(new Control[] { previous, next, pause, resume, stop, seconds, seek, loop, hide });
            layout.Controls.Add(transport, 0, 1);
            playlistStatus.Dock = DockStyle.Fill; playlistStatus.ForeColor = Color.White;
            playlistStatus.Text = "Add last / Play next keep the current song. Play now interrupts it. Controls require 8m proximity.";
            layout.Controls.Add(playlistStatus, 0, 2);
            Shown += Initialize;
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!selfTest && !allowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePlayer(); }
            };
            FormClosed += delegate { resolver.Dispose(); titleClient.Dispose(); browser.Dispose(); parentTimer.Dispose(); testTimer.Dispose(); };
        }

        private async void Initialize(object sender, EventArgs args)
        {
            try
            {
                if (selfTest) { TestTextEditing(); TestQueueControls(); }
                if (!selfTest)
                {
                    if (!IsWindow(parent)) { ExitPlayer(); return; }
                    // Keep a normal top-level window so Unity cannot capture textbox input.


                    if (background) PrepareBackground(); else { Activate(); address.Focus(); }
                    Thread input = new Thread(ReadCommands);
                    input.IsBackground = true;
                    input.Start();
                    parentTimer.Interval = 500;
                    parentTimer.Tick += delegate
                    {
                        if (!IsWindow(parent)) ExitPlayer();
                        else if (hideWhenPlaying && DateTime.UtcNow > hideDeadline) { hideWhenPlaying = false; Hide(); }
                    };
                    parentTimer.Start();
                }
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TonyAleTaleMods", "WebView2Speaker");
                if (selfTest) data = Path.Combine(Path.GetTempPath(), "TonyJukeboxTests", Guid.NewGuid().ToString("N"));
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, data,
                    new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
                await browser.EnsureCoreWebView2Async(environment);
                await InitializeResolver(environment);
                browser.CoreWebView2.AddWebResourceRequestedFilter(PlayerOrigin + "/*", CoreWebView2WebResourceContext.Document);
                browser.CoreWebView2.WebResourceRequested += delegate(object s, CoreWebView2WebResourceRequestedEventArgs e)
                {
                    Uri requestUri = new Uri(e.Request.Uri);
                    string id, list; long token;
                    if (requestUri.GetLeftPart(UriPartial.Authority) != PlayerOrigin ||
                        !YouTubeUrl.TryGetPlayback(requestUri.Query.TrimStart('?'), out id, out list, out token)) return;
                    // A real HTTPS document gives the iframe an automatic browser-generated Referer.
                    byte[] html = Encoding.UTF8.GetBytes(PlayerHtml(id, list, token, playbackTest));
                    e.Response = environment.CreateWebResourceResponse(new MemoryStream(html), 200, "OK",
                        "Content-Type: text/html; charset=utf-8\r\nReferrer-Policy: strict-origin-when-cross-origin\r\nCache-Control: no-store\r\n");
                };
                browser.CoreWebView2.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
                {
                    if (!e.Source.StartsWith(PlayerOrigin + "/", StringComparison.Ordinal)) return;
                    string value = e.TryGetWebMessageAsString();
                    // Reject queued events from a page replaced by a newer track.
                    if (e.Source != PlayerOrigin + "/player?" + pendingVideo) return;
                    Console.WriteLine(value);
                    if (value.StartsWith("EVENT ", StringComparison.Ordinal))
                    { int split = value.IndexOf(' ', 6); if (split < 0) return; value = value.Substring(split + 1); }
                    if (value.StartsWith("PLAYER_STATE ", StringComparison.Ordinal)) Int32.TryParse(value.Substring(13), out playerState);
                    if (value == "PLAYER_STATE 1" && hideWhenPlaying && resolveSpec == null) { hideWhenPlaying = false; Hide(); Console.WriteLine("HIDDEN"); }
                    if (value.StartsWith("PLAYER_ERROR ", StringComparison.Ordinal) && hideWhenPlaying) { hideWhenPlaying = false; Hide(); }
                    if (value == "PLAYER_READY") { playerReady = true; if (!selfTest) ApplySync(); }
                    if (speakerTest && value == "PLAYER_STATE 1" && !speakerTestStarted) { speakerTestStarted = true; TestSpeaker(); }
                    if (playbackTest && ((!speakerTest && value == "PLAYER_STATE 1") || value.StartsWith("PLAYER_ERROR ", StringComparison.Ordinal)))
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
                if (resolveSpec != null) StartResolve(resolveSpec);
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
                else status.Text = "Paste a YouTube video or playlist URL, then choose Add last, Play next, or Play now.";
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
                if (!IsDisposed) BeginInvoke((Action)ExitPlayer);
            }
            catch { if (!IsDisposed) try { BeginInvoke((Action)ExitPlayer); } catch { } }
        }

        private async void Command(string command)
        {
            if (command.StartsWith("QUEUE ", StringComparison.Ordinal)) { ReadQueue(command.Substring(6)); return; }
            if (command.StartsWith("RESOLVE ", StringComparison.Ordinal)) { StartResolve(command.Substring(8)); return; }
            if (command == "CANCEL_RESOLVE") { CancelResolve(); return; }
            if (command == "TRACK_PROBE")
            {
                if (playerReady)
                {
                    try { Console.WriteLine("TRACK_PROBE " + await browser.CoreWebView2.ExecuteScriptAsync("[player.getVideoData().video_id,player.getCurrentTime(),player.getPlayerState(),player.getDuration()].join(',')")); }
                    catch (Exception ex) { Console.WriteLine("PROBE_ERROR " + ex.Message); }
                }
                return;
            }
            if (command == "PROBE")
            {
                if (playerReady)
                {
                    try { Console.WriteLine("PROBE " + await browser.CoreWebView2.ExecuteScriptAsync("[player.getCurrentTime(),player.getPlayerState(),player.getVolume()].join(',')")); }
                    catch (Exception ex) { Console.WriteLine("PROBE_ERROR " + ex.Message); }
                }
                return;
            }
            if (command == "EXIT") { ExitPlayer(); return; }
            if (command.StartsWith("META ", StringComparison.Ordinal)) { playlistStatus.Text = command.Substring(5); return; }
            if (command == "HIDE") { HidePlayer(); return; }
            if (command == "SHOW") { hideWhenPlaying = false; background = false; ShowInTaskbar = true; if (!selfTest) Opacity = 1; Show(); WindowState = FormWindowState.Normal; if (!selfTest) Activate(); return; }
            if (command.StartsWith("VOLUME ", StringComparison.Ordinal))
            { int volume; if (Int32.TryParse(command.Substring(7), out volume)) { desiredVolume = Math.Max(0, Math.Min(100, volume)); ApplySync(); } return; }
            if (command.StartsWith("CONTROL ", StringComparison.Ordinal))
            { canControl = command == "CONTROL 1"; return; }
            if (command.StartsWith("SYNC ", StringComparison.Ordinal))
            {
                string[] values = command.Substring(5).Split(' '); double position;
                if (values.Length == 3 && Double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out position) && !Double.IsNaN(position) && !Double.IsInfinity(position) && position >= 0 && position <= 604800)
                { desiredPosition = position; desiredPaused = values[1] == "1"; forceSeek |= values[2] == "1"; syncAt = DateTime.UtcNow; ApplySync(); }
                return;
            }
            if (command.StartsWith("PLAY ", StringComparison.Ordinal)) { Play(command.Substring(5)); return; }
            if (command == "STOP")
            {
                pendingVideo = null;
                playerReady = false;
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
                    return; // Independent window keeps its user-selected bounds.
            }
        }

        private void Play(string id)
        {
            string checkedId, list; long token;
            if (!YouTubeUrl.TryGetPlayback(id, out checkedId, out list, out token)) return;
            pendingVideo = id;
            if (!ready) return;
            playerReady = false;
            playerState = -1;
            if (!Visible) PrepareBackground();
            status.Text = "Loading YouTube...";
            status.Visible = true;
            Console.WriteLine("LOCAL_PLAY " + id);
            browser.CoreWebView2.Navigate(PlayerOrigin + "/player?" + id);
        }

        private void PlayAddress(string mode = "append")
        {
            string id, list;
            if (!YouTubeUrl.TryGetSelection(address.Text, out id, out list))
            { Console.WriteLine("INVALID_URL"); status.Text = "Enter a valid YouTube video or playlist URL."; status.Visible = true; address.Focus(); return; }
            SendRequest(new JukeboxRequest { op=mode, video=id, playlist=list });
        }

        private void PrepareBackground()
        {
            // YouTube initializes playback only while its WebView is rendering.
            // A transparent, non-activating window lets loading finish without stealing game input.
            hideWhenPlaying = true; hideDeadline = DateTime.UtcNow.AddSeconds(30);
            Opacity = 0; ShowInTaskbar = false; if (!Visible) Show();
        }
        private void HidePlayer()
        {
            if ((pendingVideo != null && playerState == -1) || resolveSpec != null) PrepareBackground(); else { hideWhenPlaying = false; Hide(); }
            ShowInTaskbar = false; Console.WriteLine("HIDDEN");
        }
        private void ExitPlayer() { allowClose = true; Close(); }
        private async void ApplySync()
        {
            syncDirty = true;
            if (!playerReady || syncBusy || IsDisposed) return;
            syncBusy = true;
            try
            {
                while (syncDirty && playerReady && !IsDisposed)
                {
                syncDirty = false;
                bool force = forceSeek; forceSeek = false;
                double position = desiredPosition + (desiredPaused ? 0 : Math.Max(0, (DateTime.UtcNow - syncAt).TotalSeconds));
                await browser.CoreWebView2.ExecuteScriptAsync("syncPlayer(" + position.ToString("F3", CultureInfo.InvariantCulture) + "," +
                    (desiredPaused ? "true" : "false") + "," + desiredVolume + "," + (force ? "true" : "false") + "," + (canControl && Visible ? "true" : "false") + ")");
                }
            }
            catch (Exception ex) { Console.WriteLine("SYNC_ERROR " + ex.Message); }
            finally { syncBusy = false; }
        }

        private async void TestSpeaker()
        {
            try
            {
                double before = Double.Parse(await browser.CoreWebView2.ExecuteScriptAsync("player.getCurrentTime()"), CultureInfo.InvariantCulture);
                Command("HIDE"); await Task.Delay(3500);
                double after = Double.Parse(await browser.CoreWebView2.ExecuteScriptAsync("player.getCurrentTime()"), CultureInfo.InvariantCulture);
                if (Visible || after < before + 1) throw new Exception("Hidden playback did not advance");
                Command("VOLUME 25"); Command("SYNC 15 1 1"); await Task.Delay(1800);
                string volume = await browser.CoreWebView2.ExecuteScriptAsync("player.getVolume()");
                string state = await browser.CoreWebView2.ExecuteScriptAsync("player.getPlayerState()");
                double at = Double.Parse(await browser.CoreWebView2.ExecuteScriptAsync("player.getCurrentTime()"), CultureInfo.InvariantCulture);
                if (volume != "25" || state != "2" || Math.Abs(at - 15) > 2) throw new Exception("Pause/seek/volume failed: " + volume + "/" + state + "/" + at);
                Command("SYNC 20 0 1"); await Task.Delay(2500);
                at = Double.Parse(await browser.CoreWebView2.ExecuteScriptAsync("player.getCurrentTime()"), CultureInfo.InvariantCulture);
                if (at < 20.5) throw new Exception("Resume did not advance");
                Command("SHOW");
                if (!Visible) throw new Exception("Reopen failed");
                Command("STOP"); await Task.Delay(500);
                if (browser.CoreWebView2.Source != "about:blank") throw new Exception("Stop failed");
                Console.WriteLine("SPEAKER_TEST=hidden-progress,pause,seek,volume,resume,reopen,stop:PASS");
                Environment.ExitCode = 0; ExitPlayer();
            }
            catch (Exception ex) { Console.WriteLine("SPEAKER_TEST_FAIL " + ex.Message); Environment.ExitCode = 1; ExitPlayer(); }
        }

        private void TestTextEditing()
        {
            foreach (Size size in new[] { new Size(1100, 650), new Size(1000, 600) })
            {
                ClientSize = size; PerformLayout();
                TableLayoutPanel layout = (TableLayoutPanel)Controls[0];
                layout.PerformLayout();
                TableLayoutPanel toolbar = (TableLayoutPanel)layout.GetControlFromPosition(0, 0);
                toolbar.PerformLayout();
                foreach (Control control in toolbar.Controls)
                    if (!control.Visible || control.Width < 30 || control.Height < 18 || !toolbar.ClientRectangle.Contains(control.Bounds))
                        throw new InvalidOperationException("Toolbar clipped: " + control.Text);
                FlowLayoutPanel transport = (FlowLayoutPanel)layout.GetControlFromPosition(0, 1);
                transport.PerformLayout();
                foreach (Control control in transport.Controls)
                    if (!control.Visible || !transport.ClientRectangle.Contains(control.Bounds))
                        throw new InvalidOperationException("Transport clipped: " + control.Text);
                if (browser.ClientSize.Width < 200 || browser.ClientSize.Height < 200) throw new InvalidOperationException("Player viewport below 200x200");
            }
            Console.WriteLine("TOOLBAR_LAYOUT=1100x650,1000x600:PASS");
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

        private static string PlayerHtml(string id, string list, long token, bool muted)
        {
            if (list != "") return PlaylistHtml(list, token);
            return "<!doctype html><html><head><meta name='referrer' content='strict-origin-when-cross-origin'>" +
                "<style>html,body,#player{margin:0;width:100%;height:100%;background:#141414;overflow:hidden}</style></head><body>" +
                "<div id='player'></div><script>var player,control=false,suppress=0,pending=0,lastTime=0,lastTick=0;function report(s){chrome.webview.postMessage('" + (token > 0 ? "EVENT " + token + " " : "") + "'+s)}" +
                "function syncPlayer(t,paused,v,force,edit){control=edit;if(!player||!player.getCurrentTime)return;player.setVolume(v);" +
                "if(Date.now()<pending)return;var state=player.getPlayerState();if(state===0&&!force)return;" +
                "if(!paused&&(state===-1||state===5)){suppress=Date.now()+1800;player.playVideo();return;}" +
                "if(force||Math.abs(player.getCurrentTime()-t)>2){suppress=Date.now()+1800;player.seekTo(t,true);lastTime=t;lastTick=Date.now()}" +
                "if(paused&&state!==2){suppress=Date.now()+1800;player.pauseVideo()}else if(!paused&&(state===2||(state===0&&force))){suppress=Date.now()+1800;player.playVideo()}}" +
                "setInterval(function(){if(!player||!player.getCurrentTime)return;var t=player.getCurrentTime(),now=Date.now();" +
                "if(control&&now>suppress&&now>pending&&lastTick&&Math.abs(t-lastTime-(now-lastTick)/1000)>3&&player.getPlayerState()===1){pending=now+1200;report('REQUEST_SEEK '+t)}lastTime=t;lastTick=now},500);" +
                "function onYouTubeIframeAPIReady(){player=new YT.Player('player',{videoId:'" + id + "'," +
                "playerVars:{autoplay:1,playsinline:1,origin:'" + PlayerOrigin + "'},events:{" +
                "onReady:function(e){e.target.setVolume(0);report('PLAYER_READY');" + (muted ? "e.target.mute();" : "") + "e.target.playVideo()}," +
                "onStateChange:function(e){report('PLAYER_STATE '+e.data);if(control&&Date.now()>suppress&&(e.data===1||e.data===2)){pending=Date.now()+1200;report(e.data===2?'REQUEST_PAUSE':'REQUEST_RESUME')}}," +
                "onAutoplayBlocked:function(){report('AUTOPLAY_BLOCKED')},onError:function(e){report('PLAYER_ERROR '+e.data)}}})}" +
                "</script><script src='https://www.youtube.com/iframe_api'></script></body></html>";
        }
        private static string PlaylistHtml(string list, long token)
        {
            // Resolve on the host, then replace this page with a single-video player.
            // Letting each peer's iframe advance independently would desynchronize the room.
            return "<!doctype html><html><head><meta name='referrer' content='strict-origin-when-cross-origin'>" +
                "<style>html,body,#player{margin:0;width:100%;height:100%;overflow:hidden}</style></head>" +
                "<body style='margin:0;background:#141414;color:white'><div id='player'></div><script>var player,done=false;" +
                "function report(s){chrome.webview.postMessage('EVENT " + token + " '+s)}" +
                "function finish(s){if(done)return;done=true;report(s)}" +
                "function onYouTubeIframeAPIReady(){player=new YT.Player('player',{width:'100%',height:'100%',playerVars:{origin:'" + PlayerOrigin + "'}," +
                "events:{onReady:function(e){e.target.mute();e.target.cuePlaylist({list:'" + list + "',listType:'playlist'});}," +
                "onError:function(e){finish('PLAYER_ERROR '+e.data)}}})}" +
                "setInterval(function(){if(done||!player||!player.getPlaylist)return;var q=player.getPlaylist();if(!q||!q.length)return;" +
                "if(q.length>200){finish('PLAYER_ERROR PLAYLIST_LIMIT_200');return;}" +
                "if(q.some(function(v){return !/^[A-Za-z0-9_-]{11}$/.test(v)})){finish('PLAYER_ERROR INVALID_PLAYLIST');return;}" +
                "finish('PLAYLIST '+q.join(','))},500);setTimeout(function(){finish('PLAYER_ERROR PLAYLIST_TIMEOUT')},20000);" +
                "</script><script src='https://www.youtube.com/iframe_api'></script></body></html>";
        }
    }
}
