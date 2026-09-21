using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

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
        private string message = "Paste a YouTube video URL in the player below.";
        private JukeboxSpeaker speaker;
        private readonly Queue<string> events = new Queue<string>();
        private float nextStart;
        private bool open;
        private bool failed;

        private ManualLogSource log;
        private static readonly string[] payload = { "Tony.JukeboxBrowser.exe", "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll", "WebView2-LICENSE.txt", "WebView2-NOTICE.txt" };
        public void Initialize(ManualLogSource logger)
        {
            instance = this;
            log = logger;
            speaker = new JukeboxSpeaker(logger, SpeakerCommand);
            harmony = new Harmony("Tony.TeammateHealthBars.YouTubeJukebox");
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "OnEnable"), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnJukeboxOpened"));
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "OnDisable"), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnJukeboxClosed"));
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "PlayerStop"), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnNativeStop"));
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "PlayerPlay", Type.EmptyTypes), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnNativeStop"));
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "PlayerNext"), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnNativeStop"));
            harmony.Patch(AccessTools.Method(typeof(JukeboxUI), "PlayerPrevious"), postfix: new HarmonyMethod(typeof(YouTubeJukeboxPanel), "OnNativeStop"));
            log.LogInfo("YouTube speaker ready: shared playback, close-to-background, 3m full / 30m silent.");
        }

        private static void OnJukeboxOpened(JukeboxUI __instance)
        {
            if (instance != null) instance.jukeboxUI = __instance;
        }
        private static void OnJukeboxClosed(JukeboxUI __instance)
        {
            if (instance != null && instance.jukeboxUI == __instance)
            {
                instance.HidePlayer();
                instance.jukeboxUI = null;
            }
        }

        private static void OnNativeStop(JukeboxUI __instance)
        { if (instance != null) instance.speaker.Stop(__instance.jukebox); }

        private Rect PanelRect()
        {
            float w = Mathf.Min(960, Screen.width - 40);
            float h = Mathf.Min(650, Screen.height - 40);
            return new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
        }

        private void OnGUI()
        {
            if (jukeboxUI == null || !jukeboxUI.isActiveAndEnabled) return;
            if (GUI.Button(new Rect(Screen.width / 2 + 100, 35, 175, 36), "Stop YouTube")) speaker.Stop(jukeboxUI.jukebox);
            if (speaker.Message != "") GUI.Label(new Rect(Screen.width / 2 - 250, 78, 500, 40), speaker.Message);
            if (!open)
            {
                if (GUI.Button(new Rect(Screen.width / 2 - 90, 35, 180, 36), "YouTube"))
                {
                    open = true;
                    failed = false;
                    speaker.Selected = jukeboxUI.jukebox;
                    if (host == null || host.HasExited) StartHost(true); else Send("SHOW");
                }
                return;
            }
            Rect r = PanelRect();
            GUI.Box(r, "YouTube Jukebox");
            if (GUI.Button(new Rect(r.xMax - 85, r.y + 7, 75, 25), "Close")) { HidePlayer(); return; }
            if (failed) GUI.Label(new Rect(r.x + 12, r.y + 38, r.width - 24, 60), message);
            if (failed && GUI.Button(new Rect(r.x + 12, r.y + 120, 160, 32), "Retry browser")) { failed = false; StartHost(true); }
        }

        private void StartHost(bool show)
        {
            StopHost();
            nextStart = Time.unscaledTime + 10;
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                if (handle == IntPtr.Zero) throw new InvalidOperationException("Game window handle unavailable.");
                string directory = Path.Combine(Paths.CachePath, "TonyAleTaleMods", "0.8.1");
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
                ProcessStartInfo start = new ProcessStartInfo(Path.Combine(directory, payload[0]), handle.ToInt64().ToString() + (show ? "" : " --background"));
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
                    if (e.Data != null) lock (events) { if (events.Count < 100) events.Enqueue(e.Data); }
                };
                host.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) log.LogWarning("YouTube browser: " + e.Data);
                };
                host.Start();
                host.BeginOutputReadLine();
                host.BeginErrorReadLine();
                open = show;
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
            if (speaker == null) return;
            if (host == null) speaker.ResetPlayer();
            speaker.Update();
            while (true)
            {
                string value;
                lock (events) { if (events.Count == 0) break; value = events.Dequeue(); }
                if (value == "HIDDEN") open = false;
                speaker.HandleBrowser(value);
            }
            if (host != null && host.HasExited)
            {
                StopHost();
                failed = true;
                message = "Browser stopped. Select Retry browser, or reopen this panel.";
                nextStart = Time.unscaledTime + 10;
            }
        }

        private void SpeakerCommand(string command)
        {
            if (command == "EXIT") { open = false; StopHost(); return; }
            if ((command.StartsWith("PLAY ", StringComparison.Ordinal) || command.StartsWith("RESOLVE ", StringComparison.Ordinal)) && (host == null || host.HasExited) && Time.unscaledTime >= nextStart) StartHost(false);
            Send(command);
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
            if (speaker != null) speaker.ResetPlayer();
            lock (events) events.Clear();
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

        private void HidePlayer() { open = false; Send("HIDE"); }
        private void OnDestroy()
        {
            if (speaker != null) speaker.Disconnect();
            StopHost();
            if (harmony != null) harmony.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}
