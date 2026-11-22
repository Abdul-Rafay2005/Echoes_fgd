using System;
using System.Collections.Generic;
using UnityEngine;

namespace Echoes
{
    public enum KeyId { Sun = 0, Leaf = 1, Wave = 2 }
    public enum Timeline { Present = 0, Past = 1 }
    public enum GameState { Menu, Playing, Transitioning, Paused, Lost, Won }

    /// <summary>Physics layers used by gameplay. The project builder names these layers in TagManager.</summary>
    public static class EchoesLayers
    {
        public const int Default = 0;
        public const int IgnoreRaycast = 2;
        public const int Player = 8;
        public const int QueryPast = 9;       // always-enabled proxies of Past-only blockers (query only)
        public const int QueryPresent = 10;   // always-enabled proxies of Present-only blockers (query only)
        public const int Zones = 11;          // trigger volumes read by explicit overlap queries
        public const int Platform = 12;       // moving platform support

        public static readonly int[] Indices = { Player, QueryPast, QueryPresent, Zones, Platform };
        public static readonly string[] Names = { "EE_Player", "EE_QueryPast", "EE_QueryPresent", "EE_Zones", "EE_Platform" };

        public const int ZoneMask = 1 << Zones;
        public const int GroundMask = (1 << Default) | (1 << Platform);
        public const int InteractMask = ~((1 << Player) | (1 << QueryPast) | (1 << QueryPresent) | (1 << Zones) | (1 << IgnoreRaycast));

        public static int QueryMask(Timeline t) => 1 << (t == Timeline.Past ? QueryPast : QueryPresent);

        /// <summary>Query and zone layers never physically collide with anything.</summary>
        public static void ApplyCollisionMatrix()
        {
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(QueryPast, i, true);
                Physics.IgnoreLayerCollision(QueryPresent, i, true);
                Physics.IgnoreLayerCollision(Zones, i, true);
            }
        }
    }

    /// <summary>Stable identifiers used by saves, rewards and permanent flags.</summary>
    public static class EchoesIds
    {
        public static readonly KeyId[] AllKeys = { KeyId.Sun, KeyId.Leaf, KeyId.Wave };

        public const string PuzzleRune = "rune_gallery";
        public const string PuzzleChest = "platform_chest";
        public static readonly string[] Puzzles = { PuzzleRune, PuzzleChest };

        public const string DoorHeart = "heart_door";
        public const string ShortcutLeaf = "leaf_shortcut";
        public const string ShortcutWave = "wave_shortcut";
        public static readonly string[] Doors = { DoorHeart, ShortcutLeaf, ShortcutWave };

        /// <summary>Virtual flag that maps to RunProgress.heartOwned.</summary>
        public const string FlagHeart = "heart";

        public const string AwardHeart = "heart";
        public const string AwardEscape = "escape";
        public static readonly string[] Awards = { "key_sun", "key_leaf", "key_wave", AwardHeart, AwardEscape };

        public static string KeyName(KeyId k)
        {
            switch (k)
            {
                case KeyId.Sun: return "sun";
                case KeyId.Leaf: return "leaf";
                default: return "wave";
            }
        }

        public static string KeyTitle(KeyId k) => KeyName(k).ToUpperInvariant();
        public static string KeyAward(KeyId k) => "key_" + KeyName(k);

        public static bool TryParseKey(string s, out KeyId key)
        {
            foreach (var k in AllKeys)
            {
                if (KeyName(k) == s) { key = k; return true; }
            }
            key = KeyId.Sun;
            return false;
        }

        public static string TimelineName(Timeline t) => t == Timeline.Past ? "past" : "present";

        public static bool TryParseTimeline(string s, out Timeline t)
        {
            if (s == "past") { t = Timeline.Past; return true; }
            t = Timeline.Present;
            return s == "present";
        }

        public static Color KeyColor(KeyId k)
        {
            switch (k)
            {
                case KeyId.Sun: return new Color(0.95f, 0.73f, 0.33f);
                case KeyId.Leaf: return new Color(0.45f, 0.72f, 0.28f);
                default: return new Color(0.21f, 0.68f, 1f);
            }
        }
    }

    /// <summary>Plain serializable snapshot of an active run. Saved as JSON.</summary>
    [Serializable]
    public class RunProgress
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string checkpointId = "hub";
        public string timeline = "present";
        public List<string> keys = new List<string>();
        public List<string> puzzles = new List<string>();
        public List<string> doors = new List<string>();
        public bool heartOwned;
        public List<string> awarded = new List<string>();
        public int score;
        public int deaths;

        public int KeyCount => keys.Count;
        public bool HasKey(KeyId k) => keys.Contains(EchoesIds.KeyName(k));

        public bool HasFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return true;
            if (flag == EchoesIds.FlagHeart) return heartOwned;
            return puzzles.Contains(flag) || doors.Contains(flag);
        }

        public RunProgress Clone() => JsonUtility.FromJson<RunProgress>(JsonUtility.ToJson(this));

        public bool Validate(ICollection<string> checkpointIds, out string error)
        {
            error = null;
            if (version != CurrentVersion) { error = "Incompatible save version " + version; return false; }
            if (keys == null || puzzles == null || doors == null || awarded == null) { error = "Missing lists"; return false; }
            if (string.IsNullOrEmpty(checkpointId) || (checkpointIds != null && !checkpointIds.Contains(checkpointId)))
            { error = "Unknown checkpoint '" + checkpointId + "'"; return false; }
            if (!EchoesIds.TryParseTimeline(timeline, out _)) { error = "Unknown timeline '" + timeline + "'"; return false; }
            if (!AllKnownAndDistinct(keys, k => EchoesIds.TryParseKey(k, out _))) { error = "Invalid keys"; return false; }
            if (!AllKnownAndDistinct(puzzles, p => Array.IndexOf(EchoesIds.Puzzles, p) >= 0)) { error = "Invalid puzzles"; return false; }
            if (!AllKnownAndDistinct(doors, d => Array.IndexOf(EchoesIds.Doors, d) >= 0)) { error = "Invalid doors"; return false; }
            if (!AllKnownAndDistinct(awarded, a => Array.IndexOf(EchoesIds.Awards, a) >= 0)) { error = "Invalid awards"; return false; }
            if (score < 0 || score > 1000000) { error = "Score out of range"; return false; }
            if (deaths < 0 || deaths > 1000000) { error = "Deaths out of range"; return false; }
            if (doors.Contains(EchoesIds.DoorHeart) && keys.Count < 3) { error = "Heart door open without three keys"; return false; }
            if (heartOwned && !doors.Contains(EchoesIds.DoorHeart)) { error = "Heart owned but chamber locked"; return false; }
            if (awarded.Contains(EchoesIds.AwardEscape)) { error = "Run already completed"; return false; }
            return true;
        }

        static bool AllKnownAndDistinct(List<string> list, Func<string, bool> known)
        {
            var seen = new HashSet<string>();
            foreach (var s in list)
            {
                if (s == null || !known(s) || !seen.Add(s)) return false;
            }
            return true;
        }
    }
}
