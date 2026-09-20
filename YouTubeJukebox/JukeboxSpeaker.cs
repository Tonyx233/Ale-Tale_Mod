using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace TonyMods
{
    // The host owns one shared YouTube track. Peers receive state, never arbitrary URLs or scripts.
    internal sealed class JukeboxSpeaker
    {
        private const string Channel = "Tony.YouTubeSpeaker.v1";
        private readonly ManualLogSource log;
        private readonly Action<string> emit;
        private NetworkManager network;
        private JukeboxState state = new JukeboxState();
        private readonly Dictionary<ulong, float> peers = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> requests = new Dictionary<ulong, float>();
        private float nextNetwork, nextAudio, lastSnapshot;
        private string loadedVideo = "";
        private long loadedRevision = -1;
        private Jukebox ambientBox;
        private bool actuallyPlaying;
        public Jukebox Selected;
        public string Message = "";
        public JukeboxSpeaker(ManualLogSource logger, Action<string> output) { log = logger; emit = output; }
        private double Now { get { return network.ServerTime.Time; } }
        public void ResetPlayer() { loadedVideo = ""; loadedRevision = -1; actuallyPlaying = false; SetAmbient(null); }
        public void Update()
        {
            NetworkManager net = NetworkManager.Singleton;
            if (net == null || !net.IsListening) { if (!System.Object.ReferenceEquals(network, null)) Disconnect(); return; }
            if (net != network)
            {
                Disconnect(); network = net;
                network.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
                lastSnapshot = Time.unscaledTime; nextNetwork = 0;
                log.LogInfo("YouTube sync bound; server=" + network.IsServer);
            }
            if (network.IsServer && state.video != "" && Resolve(state.box) == null)
            { state = new JukeboxState { revision = state.revision + 1, stamp = Now }; Broadcast(); }
            if (Time.unscaledTime >= nextNetwork)
            {
                nextNetwork = Time.unscaledTime + 2;
                if (network.IsServer) Broadcast(); else Send(NetworkManager.ServerClientId, new JukeboxState { op = "hello" });
            }
            if (!network.IsServer && Time.unscaledTime - lastSnapshot > 8)
            {
                if (loadedVideo != "") { emit("STOP"); ResetPlayer(); }
                Message = "Waiting for host sync. All players need the same mod version."; return;
            }
            if (Time.unscaledTime >= nextAudio) { nextAudio = Time.unscaledTime + .25f; ApplyAudio(); }
        }
        private Jukebox Resolve(ulong id)
        {
            NetworkObject obj;
            return network != null && network.SpawnManager != null && network.SpawnManager.SpawnedObjects.TryGetValue(id, out obj)
                ? obj.GetComponent<Jukebox>() ?? obj.GetComponentInChildren<Jukebox>() : null;
        }
        private bool Nearby(ulong id, Jukebox box)
        {
            if (box == null || PlayerManager.Instance == null) return false;
            foreach (PlayerNet player in PlayerManager.Instance.players.Values)
                if (player != null && player.OwnerClientId == id && player.IsSpawned && player.hp.Value > 0)
                    return Vector3.Distance(player.transform.position, box.transform.position) <= 8;
            return false;
        }
        public void Request(string op, string video, double seconds, Jukebox box)
        {
            if (network == null || box == null || !box.IsSpawned) return;
            if (!Nearby(network.LocalClientId, box)) { Message = "Move within 8m of the jukebox to control shared music."; return; }
            var packet = new JukeboxState { op = op, video = video, position = seconds, box = box.NetworkObjectId, revision = state.revision };
            if (network.IsServer) ServerRequest(network.LocalClientId, packet); else Send(NetworkManager.ServerClientId, packet);
        }
        public void Stop(Jukebox box) { if (box != null && state.box == box.NetworkObjectId) Request("stop", "", 0, box); }
        public void HandleBrowser(string value)
        {
            if (value == "PLAYER_STATE 1") actuallyPlaying = true;
            if (value == "PLAYER_STATE 0" || value == "PLAYER_STATE 2" || value.StartsWith("PLAYER_ERROR", StringComparison.Ordinal)) actuallyPlaying = false;
            if (value.StartsWith("REQUEST_PLAY ", StringComparison.Ordinal)) { Request("play", value.Substring(13), 0, Selected); return; }
            Jukebox box = Resolve(state.box);
            if (value == "REQUEST_STOP") Stop(box);
            if (value == "REQUEST_PAUSE") Request("pause", "", 0, box);
            if (value == "REQUEST_RESUME") Request("resume", "", 0, box);
            if (value.StartsWith("REQUEST_SEEK ", StringComparison.Ordinal))
            { double seconds; if (Double.TryParse(value.Substring(13), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) Request("seek", "", seconds, box); }
            if (value.StartsWith("PLAYER_ERROR", StringComparison.Ordinal) || value == "AUTOPLAY_BLOCKED")
                Message = "YouTube playback blocked. Open YouTube for details.";
        }
        private void Receive(ulong sender, FastBufferReader reader)
        {
            try
            {
                if (reader.Length > 4096) return;
                string json; reader.ReadValueSafe(out json, false);
                if (json.Length > 1800) return;
                var packet = HorseJson.Deserialize<JukeboxState>(json);
                if (packet == null || !packet.Valid()) return;
                if (network.IsServer) ServerRequest(sender, packet);
                else if (sender == NetworkManager.ServerClientId && packet.op == "state" && packet.revision >= state.revision)
                { state = packet; lastSnapshot = Time.unscaledTime; Message = ""; }
            }
            catch (Exception ex) { log.LogWarning("YouTube packet rejected: " + ex.Message); }
        }
        private void ServerRequest(ulong sender, JukeboxState packet)
        {
            bool connected = false;
            foreach (ulong id in network.ConnectedClientsIds) if (id == sender) connected = true;
            if (!connected || !packet.Valid()) return;
            if (packet.op == "hello") { peers[sender] = Time.unscaledTime; Send(sender, state); return; }
            Jukebox box = Resolve(packet.box);
            if (!Nearby(sender, box)) return;
            float previous;
            if (requests.TryGetValue(sender, out previous) && Time.unscaledTime - previous < .2f) return;
            JukeboxState next;
            if (!state.TryApply(packet, Now, out next)) return;
            requests[sender] = Time.unscaledTime; state = next;
            if (packet.op == "play") { box.currentTrackId.Value = 0; box.Stop(); }
            Broadcast(); log.LogInfo("YouTube shared " + packet.op + "; revision=" + state.revision);
        }
        private void Send(ulong id, JukeboxState packet)
        {
            using (FastBufferWriter writer = new FastBufferWriter(4096, Allocator.Temp))
            { writer.WriteValueSafe(HorseJson.Serialize(packet)); network.CustomMessagingManager.SendNamedMessage(Channel, id, writer, NetworkDelivery.ReliableSequenced); }
        }
        private void Broadcast()
        {
            foreach (ulong id in network.ConnectedClientsIds)
            { float seen; if (id != network.LocalClientId && peers.TryGetValue(id, out seen) && Time.unscaledTime - seen < 10) Send(id, state); }
        }
        private void ApplyAudio()
        {
            if (state.video == "") { if (loadedVideo != "") emit("STOP"); ResetPlayer(); return; }
            Jukebox box = Resolve(state.box); PlayerNet player = PlayerNet.Instance;
            if (box == null || player == null || !player.IsSpawned) { emit("VOLUME 0"); SetAmbient(null); return; }
            int volume = JukeboxState.DistanceVolume(Vector3.Distance(player.transform.position, box.transform.position), box.volume.Value);
            SetAmbient(volume > 0 && !state.paused && actuallyPlaying ? box : null);
            // Do not start a new WebView for someone outside the audible radius.
            if (loadedVideo == "" && volume == 0) return;
            if (box.isLocallyPlaying) box.Stop();
            if (loadedVideo != state.video)
            { emit("PLAY " + state.video); loadedVideo = state.video; }
            emit("VOLUME " + volume);
            // Revision changes force a seek, including restarting the same URL.
            emit("SYNC " + state.PositionAt(Now).ToString("F3", CultureInfo.InvariantCulture) + " " + (state.paused ? "1" : "0") + " " + (loadedRevision == state.revision ? "0" : "1"));
            loadedRevision = state.revision;
            emit("CONTROL " + (Nearby(network.LocalClientId, box) ? "1" : "0"));
        }
        private void SetAmbient(Jukebox box)
        {
            JukeboxManager manager = JukeboxManager.Instance;
            if (!System.Object.ReferenceEquals(ambientBox, box))
            {
                if (manager != null && !System.Object.ReferenceEquals(ambientBox, null) &&
                    !(ambientBox != null && ambientBox.currentTrackId.Value > 0 && ambientBox.isLocallyPlaying)) manager.RemoveJukeboxAround(ambientBox);
                ambientBox = box;
            }
            // The native track may clear its own nearby flag on the first stopped frame.
            if (manager != null && box != null) manager.AddJukeboxAround(box);
        }
        public void Disconnect()
        {
            if (network != null && network.CustomMessagingManager != null) network.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
            emit("EXIT"); network = null; state = new JukeboxState(); peers.Clear(); requests.Clear(); Selected = null; ResetPlayer();
        }
    }
}
