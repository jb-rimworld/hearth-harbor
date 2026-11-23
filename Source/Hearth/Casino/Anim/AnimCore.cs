// SPDX-License-Identifier: MIT
// The small animation kit of the casino windows (1.1.0, F30). Rules:
//  * outcome first: every game is settled (money moved, history written, sealed stream advanced) BEFORE an animation starts; an animation
//    only shows a result that is already known, so closing a window in the middle loses nothing;
//  * real time: animations run on Time.realtimeSinceStartup (they play while the game is paused and ignore the game speed);
//  * skippable: a click on the table, Space/Enter or pressing the action button again jumps to the end; Shift plays at 300 %;
//  * the setting casino.animations (global) turns them off: the final frame shows at once.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Hearth.CasinoMath;

namespace Hearth.Casino.Anim
{
    /// <summary>The clock of all casino animations. Tests set <see cref="Override"/> to drive time by hand.</summary>
    public static class AnimClock
    {
        public static Func<float> Override;
        public static float Now => Override != null ? Override() : Time.realtimeSinceStartup;
    }

    /// <summary>Player preferences for the animations (global settings, see CS).</summary>
    public static class AnimPrefs
    {
        /// <summary>Tests force animations on or off regardless of the setting (null = use the setting).</summary>
        public static bool? ForceOn;
        public static bool On
        {
            get
            {
                if (ForceOn.HasValue) return ForceOn.Value;
                try { return HSettings.GetBool(CS.Animations); } catch (Exception) { return true; }
            }
        }

        public static float Speed
        {
            get
            {
                float s = 1f;
                try { s = HSettings.Get(CS.AnimSpeed); } catch (Exception) { }
                if (s <= 0.05f || float.IsNaN(s)) s = 1f;
                bool shift = false;
                try { shift = Event.current != null ? Event.current.shift : (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)); } catch (Exception) { }
                return shift ? Mathf.Max(3f, s) : s;
            }
        }
    }

    /// <summary>A timed event inside an animation: a sound and/or a callback. Fired once; a skipped animation only fires the cues marked <c>Final</c>.</summary>
    public sealed class Cue
    {
        public float At;          // seconds of designed time
        public SoundDef Sound;
        public float Pitch = 1f, Volume = 1f;
        public Action Do;
        public bool Final;        // also fired when the animation is skipped (the result sound)
        public bool Fired;
    }

    /// <summary>
    /// Base of every table animation. Time is accumulated per frame with the current speed (so Shift can speed up a running animation);
    /// <see cref="T"/> is the progress 0 … 1 and <see cref="Elapsed"/> the designed seconds that have passed.
    /// </summary>
    public abstract class TableAnim
    {
        public float Duration = 1f;
        public float Elapsed;
        public bool Skipped;
        float last = -1f;
        public readonly List<Cue> Cues = new List<Cue>();
        public static int CuesFired;     // tests

        public float T => Duration <= 0f ? 1f : Mathf.Clamp01(Elapsed / Duration);
        public bool Done => Elapsed >= Duration;

        public Cue At(float seconds, SoundDef sound, float pitch = 1f, bool final = false, Action act = null, float volume = 1f)
        {
            var c = new Cue { At = seconds, Sound = sound, Pitch = pitch, Final = final, Do = act, Volume = volume };
            Cues.Add(c); return c;
        }

        /// <summary>Advances the clock once per frame (call before drawing) and fires due cues.</summary>
        public void Update()
        {
            float now = AnimClock.Now;
            if (last < 0f) last = now;
            float dt = Mathf.Clamp(now - last, 0f, 0.25f);      // a hitch or a hidden window never jumps more than a quarter second
            last = now;
            if (!Done) Elapsed = Mathf.Min(Duration, Elapsed + dt * AnimPrefs.Speed);
            Fire(false);
            if (Done && !finished) { finished = true; Guard.Run(ModuleId.Casino, "Casino.Anim.Finish", OnFinish); }
        }
        bool finished;

        /// <summary>Jumps to the end: only the final cues sound.</summary>
        public void Skip()
        {
            if (Done) return;
            Skipped = true; Elapsed = Duration;
            Fire(true);
            if (!finished) { finished = true; Guard.Run(ModuleId.Casino, "Casino.Anim.Finish", OnFinish); }
        }

        void Fire(bool skipping)
        {
            for (int i = 0; i < Cues.Count; i++)
            {
                var c = Cues[i]; if (c.Fired) continue;
                if (skipping) { c.Fired = true; if (!c.Final) continue; }
                else if (Elapsed < c.At) continue;
                c.Fired = true; CuesFired++;
                if (c.Sound != null) CasinoSfx.Play(c.Sound, c.Pitch, c.Volume);
                if (c.Do != null) Guard.Run(ModuleId.Casino, "Casino.Anim.Cue", c.Do);
            }
        }

        /// <summary>Called once when the animation ends (also when skipped).</summary>
        protected virtual void OnFinish() { }

        /// <summary>Local progress of a part that runs from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
        public float Part(float from, float to) => to <= from ? (Elapsed >= to ? 1f : 0f) : Mathf.Clamp01((Elapsed - from) / (to - from));
    }

    /// <summary>Sounds of the casino UI. Every sound has a vanilla fallback, so a missing clip never silences the game.</summary>
    [StaticConstructorOnStartup]
    public static class CasinoSfx
    {
        public static int Plays;              // tests
        public static string LastPlayed;
        static readonly Dictionary<string, SoundDef> cache = new Dictionary<string, SoundDef>();
        static readonly Dictionary<string, string> fallback = new Dictionary<string, string>
        {
            { "HH_Casino_Chip", "Tick_Tiny" }, { "HH_Slot_Lever", "Click" }, { "HH_Slot_Spin", "Tick_Tiny" }, { "HH_Slot_ReelStop", "Click" },
            { "HH_Slot_NearMiss", "Tick_Tiny" }, { "HH_Slot_WinSmall", "TinyBell" }, { "HH_Slot_WinBig", "TinyBell" }, { "HH_Slot_Jackpot", "TinyBell" },
            { "HH_Roulette_Spin", "Tick_Tiny" }, { "HH_Roulette_Drop", "Click" }, { "HH_Card_Deal", "Tick_Tiny" }, { "HH_Card_Flip", "Tick_Tiny" },
            { "HH_Card_Shuffle", "Tick_Tiny" }, { "HH_Chips_Place", "Tick_Tiny" }, { "HH_Chips_Push", "Tick_Tiny" }, { "HH_Casino_Win", "TinyBell" },
            { "HH_Casino_Lose", "ClickReject" }, { "HH_Casino_Bust", "ClickReject" }
        };

        public static SoundDef Def(string name)
        {
            if (name == null) return null;
            if (cache.TryGetValue(name, out var d)) return d;
            d = DefDatabase<SoundDef>.GetNamedSilentFail(name);
            if (d == null && fallback.TryGetValue(name, out var fb)) { d = DefDatabase<SoundDef>.GetNamedSilentFail(fb); HLog.Once("casino.sfx." + name, LogLevel.Warn, "Casino", "sound " + name + " is missing; using vanilla " + fb); }
            cache[name] = d;
            return d;
        }

        public static SoundDef Chip => Def("HH_Casino_Chip");
        public static SoundDef Lever => Def("HH_Slot_Lever");
        public static SoundDef Spin => Def("HH_Slot_Spin");
        public static SoundDef ReelStop => Def("HH_Slot_ReelStop");
        public static SoundDef NearMiss => Def("HH_Slot_NearMiss");
        public static SoundDef WinSmall => Def("HH_Slot_WinSmall");
        public static SoundDef WinBig => Def("HH_Slot_WinBig");
        public static SoundDef Jackpot => Def("HH_Slot_Jackpot");
        public static SoundDef WheelSpin => Def("HH_Roulette_Spin");
        public static SoundDef BallDrop => Def("HH_Roulette_Drop");
        public static SoundDef CardDeal => Def("HH_Card_Deal");
        public static SoundDef CardFlip => Def("HH_Card_Flip");
        public static SoundDef Shuffle => Def("HH_Card_Shuffle");
        public static SoundDef ChipsPlace => Def("HH_Chips_Place");
        public static SoundDef ChipsPush => Def("HH_Chips_Push");
        public static SoundDef Win => Def("HH_Casino_Win");
        public static SoundDef Lose => Def("HH_Casino_Lose");
        public static SoundDef Bust => Def("HH_Casino_Bust");

        /// <summary>Plays a UI sound on the camera (no-op without a def or outside a game).</summary>
        public static void Play(SoundDef def, float pitch = 1f, float volume = 1f)
        {
            if (def == null) return;
            Plays++; LastPlayed = def.defName;
            try
            {
                var info = SoundInfo.OnCamera(MaintenanceType.None);
                info.pitchFactor = pitch; info.volumeFactor = volume;
                def.PlayOneShot(info);
            }
            catch (Exception e) { HLog.Once("casino.sfx.play." + def.defName, LogLevel.Warn, "Casino", "could not play " + def.defName + ": " + e.Message); }
        }

        public static void Clear() { cache.Clear(); }
        static CasinoSfx() { HearthGame.OnGameInit += () => { Plays = 0; }; }
    }

    /// <summary>A tiny particle pool for coins, chips and sparkles inside a window (no allocation per frame, at most 96 alive).</summary>
    public sealed class Particles
    {
        public enum Kind { Coin, Sparkle, Chip, Confetti }
        struct P { public Vector2 Pos, Vel; public float Age, Life, Rot, Spin, Size; public Kind K; public Color Tint; public bool Alive; }
        readonly P[] pool = new P[96];
        float last = -1f;
        public int Alive { get { int n = 0; for (int i = 0; i < pool.Length; i++) if (pool[i].Alive) n++; return n; } }

        public void Clear() { for (int i = 0; i < pool.Length; i++) pool[i].Alive = false; }

        public void Spawn(Kind k, Vector2 pos, Vector2 vel, float life, float size, Color tint, float spin = 0f)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].Alive) continue;
                pool[i] = new P { Pos = pos, Vel = vel, Life = life, Size = size, K = k, Tint = tint, Spin = spin, Rot = 0f, Age = 0f, Alive = true };
                return;
            }
        }

        /// <summary>A burst of <paramref name="n"/> coins from a point, flying up and falling with gravity.</summary>
        public void Fountain(Vector2 from, int n, float power, System.Random ui, Kind k = Kind.Coin)
        {
            for (int i = 0; i < n; i++)
            {
                float a = (float)(-Math.PI / 2 + (ui.NextDouble() - 0.5) * 1.6);
                float v = power * (0.55f + (float)ui.NextDouble() * 0.6f);
                var tint = k == Kind.Confetti ? Color.HSVToRGB((float)ui.NextDouble(), 0.55f, 0.95f) : Color.white;
                Spawn(k, from, new Vector2(Mathf.Cos(a) * v, Mathf.Sin(a) * v), 1.3f + (float)ui.NextDouble() * 0.7f, k == Kind.Coin ? 18f + (float)ui.NextDouble() * 8f : 10f, tint, (float)(ui.NextDouble() - 0.5) * 720f);
            }
        }

        public void Sparkles(Rect area, int n, System.Random ui)
        {
            for (int i = 0; i < n; i++)
            {
                var p = new Vector2(area.x + (float)ui.NextDouble() * area.width, area.y + (float)ui.NextDouble() * area.height);
                Spawn(Kind.Sparkle, p, new Vector2(0, -14f), 0.5f + (float)ui.NextDouble() * 0.5f, 10f + (float)ui.NextDouble() * 10f, Color.white, 180f);
            }
        }

        /// <summary>Moves and draws the particles; positions are relative to <paramref name="origin"/> (the stage's top left).</summary>
        public void UpdateAndDraw(Vector2 origin, Rect clip)
        {
            float now = AnimClock.Now; if (last < 0) last = now;
            float dt = Mathf.Clamp(now - last, 0f, 0.1f); last = now;
            var old = GUI.color;
            for (int i = 0; i < pool.Length; i++)
            {
                if (!pool[i].Alive) continue;
                ref P p = ref pool[i];
                p.Age += dt;
                if (p.Age >= p.Life) { p.Alive = false; continue; }
                if (p.K != Kind.Sparkle) p.Vel.y += 900f * dt;
                p.Pos += p.Vel * dt; p.Rot += p.Spin * dt;
                var pos = origin + p.Pos;
                if (!clip.Contains(pos)) { if (pos.y > clip.yMax + 40f) p.Alive = false; continue; }
                float fade = Mathf.Clamp01((p.Life - p.Age) / 0.35f);
                float s = p.Size * (p.K == Kind.Sparkle ? Mathf.Sin(p.Age / p.Life * Mathf.PI) : 1f);
                var tex = p.K == Kind.Coin ? CasinoAnimTex.Coin : p.K == Kind.Chip ? CasinoAnimTex.Chip : p.K == Kind.Sparkle ? CasinoAnimTex.Sparkle : CasinoAnimTex.Confetti;
                GUI.color = new Color(p.Tint.r, p.Tint.g, p.Tint.b, fade);
                var r = new Rect(pos.x - s / 2, pos.y - s / 2, s, s);
                if (p.K == Kind.Coin)
                {
                    // a tossed coin turns: squash its width with the spin
                    float w = Mathf.Abs(Mathf.Cos(p.Rot * Mathf.Deg2Rad)) * s; r = new Rect(pos.x - w / 2, pos.y - s / 2, Mathf.Max(2f, w), s);
                    GUI.DrawTexture(r, tex);
                }
                else AnimDraw.Rotated(r, tex, p.Rot);
            }
            GUI.color = old;
        }
    }

    /// <summary>Textures of the casino animations (UI/HH/Casino/Anim/…); missing ones fall back to a plain white texture.</summary>
    [StaticConstructorOnStartup]
    public static class CasinoAnimTex
    {
        static Texture2D T(Texture2D t) => t ?? BaseContent.WhiteTex;
        public static readonly Texture2D Felt = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Felt", false));
        public static readonly Texture2D SlotCabinet = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/SlotCabinet", false));
        public static readonly Texture2D SlotGlass = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/SlotGlass", false));
        public static readonly Texture2D LeverArm = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/LeverArm", false));
        public static readonly Texture2D LeverKnob = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/LeverKnob", false));
        public static readonly Texture2D WheelEU = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/WheelEU", false));
        public static readonly Texture2D WheelUS = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/WheelUS", false));
        public static readonly Texture2D WheelRim = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/WheelRim", false));
        public static readonly Texture2D WheelTurret = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/WheelTurret", false));
        public static readonly Texture2D Ball = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Ball", false));
        public static readonly Texture2D CardFace = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/CardFace", false));
        public static readonly Texture2D CardBack = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/CardBack", false));
        public static readonly Texture2D Shoe = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Shoe", false));
        public static readonly Texture2D Coin = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Coin", false));
        public static readonly Texture2D Chip = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Chip", false));
        public static readonly Texture2D Sparkle = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Sparkle", false));
        public static readonly Texture2D Confetti = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Confetti", false));
        public static readonly Texture2D BigWin = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/BigWinPlate", false));
        public static readonly Texture2D ThrumboRun = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/ThrumboRun", false));
        public static readonly Texture2D Glow = T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Glow", false));
        public static readonly Texture2D[] Suits = { T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Suit0", false)), T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Suit1", false)), T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Suit2", false)), T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/Suit3", false)) };
        public static readonly Texture2D[] Faces = { T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/FaceJ", false)), T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/FaceQ", false)), T(ContentFinder<Texture2D>.Get("UI/HH/Casino/Anim/FaceK", false)) };
    }

    /// <summary>Drawing helpers that work in every supported game version.</summary>
    public static class AnimDraw
    {
        /// <summary>Draws a texture rotated by <paramref name="degrees"/> (clockwise) around the rect's centre.</summary>
        public static void Rotated(Rect r, Texture tex, float degrees)
        {
            if (tex == null) return;
            if (Mathf.Abs(degrees) < 0.01f) { GUI.DrawTexture(r, tex); return; }
            Widgets.DrawTextureRotated(r, tex, degrees);
        }

        /// <summary>Tiles a texture over a rect (felt). Drawn tile by tile: some engines clamp texture coordinates instead of repeating them.</summary>
        public static void Tiled(Rect r, Texture tex, float tile = 128f)
        {
            if (tex == null || r.width <= 0 || r.height <= 0) return;
            for (float y = r.y; y < r.yMax - 0.5f; y += tile)
            {
                float h = Mathf.Min(tile, r.yMax - y);
                for (float x = r.x; x < r.xMax - 0.5f; x += tile)
                {
                    float w = Mathf.Min(tile, r.xMax - x);
                    // texture v runs bottom-up: the visible part of a cut tile is its top
                    GUI.DrawTextureWithTexCoords(new Rect(x, y, w, h), tex, new Rect(0, 1f - h / tile, w / tile, h / tile));
                }
            }
        }

        /// <summary>A soft glow behind something (a radial texture, tinted).</summary>
        public static void Glow(Vector2 c, float radius, Color col)
        {
            var old = GUI.color; GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2, radius * 2), CasinoAnimTex.Glow);
            GUI.color = old;
        }

        /// <summary>Label with a soft dark shadow, centred (for numbers on felt).</summary>
        public static void ShadowLabel(Rect r, string text, Color col, GameFont font = GameFont.Small, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var of = Text.Font; var oa = Text.Anchor; var oc = GUI.color;
            Text.Font = font; Text.Anchor = anchor;
            GUI.color = new Color(0.06f, 0.05f, 0.04f, col.a * 0.8f); Widgets.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text);
            GUI.color = col; Widgets.Label(r, text);
            Text.Font = of; Text.Anchor = oa; GUI.color = oc;
        }

        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * t;

        /// <summary>A point on a soft arc from a to b (cards fly in a small curve).</summary>
        public static Vector2 Arc(Vector2 a, Vector2 b, float t, float lift)
        {
            var p = Lerp(a, b, t); p.y -= Mathf.Sin(t * Mathf.PI) * lift; return p;
        }

        /// <summary>Fits a w:h box into r, centred.</summary>
        public static Rect Fit(Rect r, float aspect)
        {
            float w = r.width, h = r.width / aspect;
            if (h > r.height) { h = r.height; w = h * aspect; }
            return new Rect(r.x + (r.width - w) / 2, r.y + (r.height - h) / 2, w, h);
        }

        /// <summary>True when the player asked to skip this frame: Space or Enter (the event is used), or a click inside <paramref name="stage"/>.</summary>
        public static bool SkipRequested(Rect stage)
        {
            var e = Event.current; if (e == null) return false;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { e.Use(); return true; }
            if (e.type == EventType.MouseDown && e.button == 0 && stage.Contains(e.mousePosition)) { e.Use(); return true; }
            return false;
        }
    }
}
