using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using FMODUnity;
using UnityEngine;

namespace TonyMods
{
    // Unity audio is disabled in this game (AudioManager m_DisableAudio), so the synthesized WAVs
    // are played through FMOD Core and scaled by the game's "bus:/Sound" volume.
    internal static class M4Audio
    {
        private static ManualLogSource log;
        private static ConfigEntry<float> volume;
        private static FMOD.Sound[][] sounds;
        private static FMOD.ChannelGroup master;
        private static FMOD.Studio.Bus bus;
        private static bool ready, failed, busLooked;
        private static readonly System.Random random = new System.Random();

        public static void Initialize(ManualLogSource logger, ConfigEntry<float> gain) { log = logger; volume = gain; }

        private static bool Ensure()
        {
            if (ready) return true;
            if (failed || !RuntimeManager.IsInitialized) return false;
            try
            {
                FMOD.System core = RuntimeManager.CoreSystem;
                var built = new FMOD.Sound[(int)M4Sound.Kind.Mode + 1][];
                for (int k = 0; k < built.Length; k++)
                {
                    int variants = k == (int)M4Sound.Kind.Shot ? M4Sound.ShotVariants : 1;
                    built[k] = new FMOD.Sound[variants];
                    for (int v = 0; v < variants; v++)
                    {
                        byte[] wav = M4Sound.Wav(M4Sound.Synth((M4Sound.Kind)k, v));
                        var info = new FMOD.CREATESOUNDEXINFO();
                        info.cbsize = Marshal.SizeOf(typeof(FMOD.CREATESOUNDEXINFO));
                        info.length = (uint)wav.Length;
                        FMOD.Sound sound;
                        Check(core.createSound(wav, FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE._3D | FMOD.MODE.LOOP_OFF | FMOD.MODE._3D_LINEARSQUAREROLLOFF, ref info, out sound), "createSound");
                        built[k][v] = sound;
                    }
                }
                Check(core.getMasterChannelGroup(out master), "getMasterChannelGroup");
                sounds = built; ready = true;
                log.LogInfo("M4 audio ready: synthesized FMOD samples (shot x" + M4Sound.ShotVariants + ", empty, magazine, charging handle, selector).");
                return true;
            }
            catch (Exception ex)
            {
                failed = true;
                log.LogError("M4 audio unavailable; falling back to the native crossbow sound: " + ex.Message);
                return false;
            }
        }

        private static void Check(FMOD.RESULT result, string call)
        {
            if (result != FMOD.RESULT.OK) throw new InvalidOperationException(call + " returned " + result);
        }

        private static float BusVolume()
        {
            try
            {
                if (!busLooked) { busLooked = true; bus = RuntimeManager.GetBus("bus:/Sound"); }
                float v;
                if (bus.isValid() && bus.getVolume(out v) == FMOD.RESULT.OK) return v;
            }
            catch (Exception) { }
            return 1;
        }

        // Own sounds are non-positional; teammates' sounds use 3D distance attenuation.
        public static void Play(M4Sound.Kind kind, Vector3 position, bool own)
        {
            if (!Ensure()) { Fallback(kind, position); return; }
            try
            {
                FMOD.Sound[] set = sounds[(int)kind];
                FMOD.Sound sound = set[set.Length == 1 ? 0 : random.Next(set.Length)];
                FMOD.Channel channel;
                if (RuntimeManager.CoreSystem.playSound(sound, master, true, out channel) != FMOD.RESULT.OK) return;
                FMOD.VECTOR pos = RuntimeUtils.ToFMODVector(position), vel = new FMOD.VECTOR();
                channel.set3DAttributes(ref pos, ref vel);
                bool shot = kind == M4Sound.Kind.Shot;
                channel.set3DMinMaxDistance(shot ? 4f : 1f, shot ? 140f : 20f);
                if (own) channel.set3DLevel(0);
                channel.setVolume(Mathf.Clamp01(volume.Value) * BusVolume() * (shot ? 1f : .8f));
                if (shot) channel.setPitch(1 + (float)(random.NextDouble() - .5) * .06f);
                channel.setPaused(false);
            }
            catch (Exception ex)
            {
                failed = true; ready = false;
                log.LogError("M4 audio playback failed; using fallback: " + ex.Message);
            }
        }

        private static void Fallback(M4Sound.Kind kind, Vector3 position)
        {
            if (kind != M4Sound.Kind.Shot || SoundManager.Instance == null) return;
            try { SoundManager.Instance.Play(SoundEvent.CrossbowFire, position); } catch (Exception) { }
        }

        public static void Shutdown()
        {
            if (sounds != null)
                foreach (FMOD.Sound[] set in sounds) foreach (FMOD.Sound s in set) { try { s.release(); } catch (Exception) { } }
            sounds = null; ready = false; failed = false; busLooked = false;
        }
    }
}
