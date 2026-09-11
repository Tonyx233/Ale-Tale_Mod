using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TonyMods
{
    public sealed class YouTubeJukeboxPanel : MonoBehaviour
    {
        private static YouTubeJukeboxPanel instance;
        private JukeboxUI jukeboxUI;
        private Harmony harmony;
        private Process host;
        private string url = "";
        private string message = "Paste a YouTube video URL. Playback is local to this player.";
        private bool open;
        private bool failed;
        private float nextBounds;
        private ManualLogSource log;
        private static readonly string[] payload = { "Tony.JukeboxBrowser.exe", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll", "WebView2-LICENSE.txt", "WebView2-NOTICE.txt" };
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle, out NativeRect rect);
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

        public void Initialize(ManualLogSource logger)
        {
            instance = this;
            log = logger;
            harmony = new Harmony("Tony.TeammateHealthBars.YouTubeJukebox");
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "OnEnable"), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnJukeboxOpened"));
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "OnDisable"), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnJukeboxClosed"));
            log.LogInfo("YouTube jukebox panel ready; open a jukebox and select YouTube.");
        }

        private static void OnJukeboxOpened(JukeboxUI __instance)
        {
            if (instance != null) instance.jukeboxUI = __instance;
        }
        private static void OnJukeboxClosed(JukeboxUI __instance)
        {
            if (instance != null && instance.jukeboxUI == __instance)
            {
                instance.ClosePlayer();
                instance.jukeboxUI = null;
            }
        }

        private Rect PanelRect()
        {
            float w = Mathf.Min(960, Screen.width - 40);
            float h = Mathf.Min(650, Screen.height - 40);
            return new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        }

        private void OnGUI()
        {
            if (jukeboxUI == null || !jukeboxUI.isActiveAndEnabled) return;
            if (!open)
            {
                if (GUI.Button(new Rect(Screen.width / 2 - 90, 35, 180, 36), "YouTube"))
                {
                    open = true;
                    failed = false;
                    StartHost();
                }
                return;
            }
            Rect r = PanelRect();
            GUI.Box(r, "YouTube Jukebox");
            if (GUI.Button(new Rect(r.xMax - 85, r.y + 7, 75, 25), "Close")) { ClosePlayer(); return; }
            url = GUI.TextField(new Rect(r.x + 12, r.y + 38, r.width - 192, 28), url, 2048);
            if (GUI.Button(new Rect(r.xMax - 172, r.y + 38, 75, 28), "Play")) Play();
            if (GUI.Button(new Rect(r.xMax - 90, r.y + 38, 78, 28), "Stop")) Send("STOP");
            GUI.Label(new Rect(r.x + 12, r.y + 70, r.width - 24, 44), message);
            if (failed && GUI.Button(new Rect(r.x + 12, r.y + 120, 160, 32), "Retry browser")) { failed = false; StartHost(); }
        }

        private void Play()
        {
            string id;
            if (!YouTubeUrl.TryGetVideoId(url, out id))
            {
                message = "Enter a valid youtube.com/watch or youtu.be video URL.";
                return;
            }
            if (host == null || host.HasExited) StartHost();
            if (host == null) return;
            // Stop this machine's native jukebox audio without sending a server command.
            if (jukeboxUI != null && jukeboxUI.jukebox != null) jukeboxUI.jukebox.Stop();
            Send("PLAY " + id);
            message = "Use the YouTube controls below. Some videos cannot be embedded.";
        }

        private void StartHost()
        {
            StopHost();
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle == IntPtr.Zero) throw new InvalidOperationException("Game window handle unavailable.");
                string directory = Path.Combine(Paths.CachePath, "TonyAleTaleMods", "0.2.0");
                Directory.CreateDirectory(directory);
                foreach (string file in payload)
                {
                    string destination = Path.Combine(directory, file);
                    using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tony.Payload." + file))
                    {
                        if (input == null) throw new FileNotFoundException("Missing embedded component: " + file);
                        using (FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write)) input.CopyTo(output);
                    }
                }
                ProcessStartInfo start = new ProcessStartInfo(Path.Combine(directory, payload[0]), handle.ToInt64().ToString());
                start.WorkingDirectory = directory;
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.RedirectStandardInput = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                host = new Process();
                host.StartInfo = start;
                host.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) log.LogInfo("YouTube browser: " + e.Data);
                };
                host.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) log.LogWarning("YouTube browser: " + e.Data);
                };
                host.Start();
                host.BeginOutputReadLine();
                host.BeginErrorReadLine();
                SendBounds();
            }
            catch (Exception ex)
            {
                StopHost();
                failed = true;
                message = "Browser could not start. Check BepInEx log and WebView2 Runtime.";
                log.LogError(ex);
            }
        }

        private void Update()
        {
            if (!open) return;
            if (jukeboxUI == null || !jukeboxUI.isActiveAndEnabled || PlayerNet.Instance == null) { ClosePlayer(); return; }
            if (host != null && host.HasExited)
            {
                StopHost();
                failed = true;
                message = "Browser stopped. Select Retry browser, or reopen this panel.";
            }
            if (Time.unscaledTime >= nextBounds)
            {
                nextBounds = Time.unscaledTime + 0.2f;
                SendBounds();
            }
        }

        private void SendBounds()
        {
            if (host == null || host.HasExited) return;
            NativeRect client;
            IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
            if (!GetClientRect(handle, out client) || Screen.width <= 0 || Screen.height <= 0) return;
            Rect r = PanelRect();
            float sx = (client.Right - client.Left) / (float)Screen.width;
            float sy = (client.Bottom - client.Top) / (float)Screen.height;
            Send("RECT " + (int)((r.x + 12) * sx) + " " + (int)((r.y + 118) * sy) + " " +
                Math.Max(200, (int)((r.width - 24) * sx)) + " " + Math.Max(200, (int)((r.height - 130) * sy)));
        }

        private void Send(string command)
        {
            try { if (host != null && !host.HasExited) { host.StandardInput.WriteLine(command); host.StandardInput.Flush(); } }
            catch (Exception ex) { log.LogWarning("Browser communication: " + ex.Message); }
        }

        private void StopHost()
        {
            Process running = host;
            host = null;
            if (running == null) return;
            try
            {
                if (!running.HasExited)
                {
                    running.StandardInput.WriteLine("EXIT");
                    running.StandardInput.Flush();
                    if (!running.WaitForExit(1000)) running.Kill();
                }
            }
            catch (Exception ex) { if (log != null) log.LogWarning(ex.Message); }
            finally { running.Dispose(); }
        }

        private void ClosePlayer() { open = false; StopHost(); }
        private void OnDestroy()
        {
            ClosePlayer();
            if (harmony != null) harmony.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}
