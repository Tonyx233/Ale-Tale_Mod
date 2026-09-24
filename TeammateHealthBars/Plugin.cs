using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace TonyMods
{
    [BepInPlugin("Tony.TeammateHealthBars", "Tony Ale & Tale Mods", "0.10.0")]
    public sealed class TeammateHealthBars : BaseUnityPlugin
    {
        private sealed class Entry
        {
            public PlayerNet Player;
            public string Name;
            public int Hp;
            public int Max;
            public bool Ready;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private readonly Dictionary<ulong, Entry> cache = new Dictionary<ulong, Entry>();
        private readonly List<ulong> removed = new List<ulong>();
        private ConfigEntry<bool> panel;
        private ConfigEntry<float> scale, leftMargin;
        private float nextRefresh;
        private GUIStyle label, small, centered;
        private bool reportedError;

        private void Awake()
        {
            panel = Config.Bind("Display", "TeamPanel", true, "Show yourself and teammates at the left-center of the screen, including solo play.");
            scale = Config.Bind("Display", "UIScale", 1f, new ConfigDescription("UI size multiplier.", new AcceptableValueRange<float>(0.5f, 2f)));
            leftMargin = Config.Bind("Display", "LeftMargin", 8f, new ConfigDescription("Team panel distance from the left edge.", new AcceptableValueRange<float>(0f, 200f)));
            Logger.LogInfo("Tony Ale & Tale Mods 0.10.0 loaded (health panel + YouTube jukebox + original horse + five-seat Horse 2 + X horse storage).");
            gameObject.AddComponent<YouTubeJukeboxPanel>().Initialize(Logger);
            gameObject.AddComponent<HorseStable>().Initialize(Config, Logger);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.1f;
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                entries.Clear();
                cache.Clear();
                if (!reportedError) Logger.LogError("Cannot read teammate state: " + ex);
                reportedError = true;
            }
        }

        private void Refresh()
        {
            entries.Clear();
            PlayerManager manager = PlayerManager.Instance;
            if (manager == null || PlayerNet.Instance == null || !PlayerNet.Instance.IsSpawned || manager.players == null)
            {
                cache.Clear();
                return;
            }
            removed.Clear();
            foreach (ulong id in cache.Keys) removed.Add(id);
            foreach (KeyValuePair<ulong, PlayerNet> pair in manager.players)
            {
                PlayerNet player = pair.Value;
                if (player == null || !player.IsSpawned) continue;
                Entry entry;
                if (!cache.TryGetValue(pair.Key, out entry) || entry.Player != player)
                {
                    entry = new Entry();
                    entry.Player = player;
                    cache[pair.Key] = entry;
                }
                removed.Remove(pair.Key);
                string nickname = player.nickname != null ? player.nickname.Value.ToString() : player.pname;
                entry.Name = String.IsNullOrEmpty(nickname) ? "Player " + pair.Key : nickname.Replace('\n', ' ').Replace('\r', ' ');
                // Do not present unavailable owner-only data as a real zero HP value.
                entry.Ready = player.hp != null && player.maxHealth != null &&
                    player.hp.CanClientRead(PlayerNet.Instance.OwnerClientId) &&
                    player.maxHealth.CanClientRead(PlayerNet.Instance.OwnerClientId) && player.GetMaxHpValue() > 0;
                entry.Max = entry.Ready ? player.GetMaxHpValue() : 0;
                entry.Hp = entry.Ready ? Math.Max(0, (int)player.hp.Value) : 0;
                entries.Add(entry);
            }
            foreach (ulong id in removed) cache.Remove(id);
            entries.Sort(delegate(Entry a, Entry b)
            {
                bool aSelf = a.Player == PlayerNet.Instance;
                bool bSelf = b.Player == PlayerNet.Instance;
                if (aSelf != bSelf) return aSelf ? -1 : 1;
                return a.Player.OwnerClientId.CompareTo(b.Player.OwnerClientId);
            });
        }

        private void PrepareStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label);
            label.fontSize = 14;
            label.richText = false;
            label.normal.textColor = Color.white;
            label.clipping = TextClipping.Clip;
            small = new GUIStyle(label);
            small.fontSize = 12;
            centered = new GUIStyle(small);
            centered.alignment = TextAnchor.MiddleCenter;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || entries.Count == 0 || PlayerNet.Instance == null) return;
            PrepareStyles();
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            try
            {
                float s = Mathf.Clamp(Screen.height / 1080f, 0.65f, 2f) * scale.Value;
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1));
                float height = Screen.height / s;
                if (panel.Value) DrawPanel(height);
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldColor;
            }
        }

        private void DrawPanel(float height)
        {
            // Compress rows for expanded multiplayer lobbies so the panel stays on screen.
            float row = Mathf.Min(55f, (height - 48f) / entries.Count);
            float total = row * entries.Count + 12f;
            float x = leftMargin.Value;
            float y = (height - total) * 0.5f;
            Box(new Rect(x, y, 220, total), new Color(0.035f, 0.045f, 0.055f, 0.82f));
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                if (e.Player == null) continue;
                float top = y + 6 + i * row;
                GUI.Label(new Rect(x + 9, top, 202, 21), e.Name, label);
                DrawBar(new Rect(x + 9, top + 23, 202, Mathf.Max(5, Mathf.Min(19, row - 27))), e);
            }
        }

        private void DrawBar(Rect rect, Entry e)
        {
            Box(rect, new Color(0, 0, 0, 0.85f));
            if (e.Ready)
            {
                float ratio = Mathf.Clamp01((float)e.Hp / e.Max);
                Color color = new Color(0.85f, 0.18f, 0.16f);
                Box(new Rect(rect.x + 1, rect.y + 1, (rect.width - 2) * ratio, rect.height - 2), color);
            }
            GUI.Label(rect, e.Ready ? e.Hp + " / " + e.Max : "-- / --", centered);
        }

        private static void Box(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            entries.Clear();
            cache.Clear();
        }
    }
}
