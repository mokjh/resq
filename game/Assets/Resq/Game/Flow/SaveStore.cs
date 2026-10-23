using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Resq.Game
{
    /// <summary>Player settings (U21). Every field persists across restarts.</summary>
    [Serializable]
    public sealed class Settings
    {
        public int TextScale = 100;            // 100 / 125 / 150 %
        public bool LeftHanded;                // mirrors the tool tray and menu control
        public int DwellMs = 800;              // head dwell, 500–1500 ms
        public bool HeadOnly;                  // hands-free Command path: head-gaze dwell selects
        public bool HighContrast;              // opaque panels, stronger outlines
        public bool ReducedEffects;            // fewer particles, no flashes; hazards stay visible
        public bool Captions = true;
        public int Music = 70, Effects = 80, Warnings = 90, Speech = 80;   // 0–100
        public string Pacing = "standard";     // standard | relaxed (Command comes from HeadOnly)
        public bool OpenPlay;                  // unlock every released mission (also an accessibility feature)
    }

    [Serializable]
    public sealed class MissionProgress
    {
        public string Id;
        public bool Completed;
        public int BestStars;
        public long BestScore;
        public int Plays;
    }

    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public bool WelcomeDone;
        public string LastMission = "W1-M1";
        public Settings Settings = new Settings();
        public List<MissionProgress> Missions = new List<MissionProgress>();

        public MissionProgress Get(string id, bool create = false)
        {
            foreach (var m in Missions) if (m.Id == id) return m;
            if (!create) return null;
            var p = new MissionProgress { Id = id };
            Missions.Add(p);
            return p;
        }

        public bool Valid(out string why)
        {
            why = "";
            if (Version < 1 || Version > CurrentVersion) { why = $"unknown save version {Version}"; return false; }
            if (Settings == null || Missions == null) { why = "missing sections"; return false; }
            var s = Settings;
            if (s.TextScale != 100 && s.TextScale != 125 && s.TextScale != 150) { why = "text scale out of range"; return false; }
            if (s.DwellMs < 500 || s.DwellMs > 1500) { why = "dwell out of range"; return false; }
            foreach (var m in Missions)
                if (m == null || string.IsNullOrEmpty(m.Id) || m.BestStars < 0 || m.BestStars > 3) { why = "bad mission record"; return false; }
            return true;
        }
    }

    /// <summary>
    /// Local save (U01 validation, U24 recovery). One JSON file plus the previous good copy as a backup; writes go to
    /// a temporary file that replaces the save only after it's complete, so a crash mid-write can't corrupt it.
    /// Nothing leaves the device: no account, no network, no analytics.
    /// </summary>
    public static class SaveStore
    {
        public enum LoadResult { Fresh, Loaded, PrimaryDamaged }

        /// <summary>Tests point this at a temporary folder so they never touch the player's save.</summary>
        public static string Folder = null;
        static string Dir => Folder ?? Application.persistentDataPath;
        public static string PrimaryPath => Path.Combine(Dir, "resq_save.json");
        public static string BackupPath => Path.Combine(Dir, "resq_save.bak.json");

        public static SaveData Current { get; private set; } = new SaveData();
        /// <summary>When the primary file is damaged: the backup, if it validates (offered on U24, never applied silently).</summary>
        public static SaveData Recoverable { get; private set; }
        public static string DamageReason { get; private set; } = "";

        public static LoadResult Load()
        {
            Recoverable = null; DamageReason = "";
            if (!File.Exists(PrimaryPath))
            {
                // a backup without a primary still counts as damage: offer it instead of silently starting over
                if (TryRead(BackupPath, out var onlyBackup, out _)) { Recoverable = onlyBackup; DamageReason = "the save file is missing"; Current = new SaveData(); return LoadResult.PrimaryDamaged; }
                Current = new SaveData();
                return LoadResult.Fresh;
            }
            if (TryRead(PrimaryPath, out var data, out var why)) { Current = data; return LoadResult.Loaded; }
            DamageReason = why;
            if (TryRead(BackupPath, out var backup, out _)) Recoverable = backup;
            Current = new SaveData();   // in memory only: nothing is overwritten until the player chooses (U24)
            return LoadResult.PrimaryDamaged;
        }

        static bool TryRead(string path, out SaveData data, out string why)
        {
            data = null; why = "";
            try
            {
                if (!File.Exists(path)) { why = "missing"; return false; }
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data == null) { why = "empty file"; return false; }
                return data.Valid(out why);
            }
            catch (Exception e) { why = "unreadable (" + e.GetType().Name + ")"; data = null; return false; }
        }

        /// <summary>U24: use the backup.</summary>
        public static void Recover()
        {
            if (Recoverable == null) return;
            Current = Recoverable; Recoverable = null;
            Save();
        }

        /// <summary>U24: start new, with the player's consent; the damaged file is kept aside, not erased.</summary>
        public static void StartNew()
        {
            try { if (File.Exists(PrimaryPath)) File.Copy(PrimaryPath, PrimaryPath + ".damaged", true); } catch (IOException) { }
            Current = new SaveData(); Recoverable = null;
            Save();
        }

        /// <summary>Settings → Reset progress (after confirmation): clears stars and unlocks, keeps settings.</summary>
        public static void ResetProgress()
        {
            var keep = Current.Settings;
            Current = new SaveData { Settings = keep, WelcomeDone = true };
            Save();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string tmp = PrimaryPath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(Current, true));
                if (File.Exists(PrimaryPath) && TryRead(PrimaryPath, out _, out _)) File.Copy(PrimaryPath, BackupPath, true);   // keep the last good copy
                if (File.Exists(PrimaryPath)) File.Delete(PrimaryPath);
                File.Move(tmp, PrimaryPath);
            }
            catch (Exception e) { Debug.LogWarning("[RESQ] save failed: " + e.Message); }
        }

        /// <summary>Records a finished run; returns true when it improved the best result.</summary>
        public static bool RecordRun(string missionId, bool success, int stars, long score)
        {
            var p = Current.Get(missionId, true);
            p.Plays++;
            bool better = false;
            if (success)
            {
                if (!p.Completed) better = true;
                p.Completed = true;
                if (stars > p.BestStars) { p.BestStars = stars; better = true; }
                if (score > p.BestScore) { p.BestScore = score; better = true; }
            }
            Current.LastMission = missionId;
            Save();
            return better;
        }

        // ------------------------------------------------------------------ campaign unlocks
        /// <summary>
        /// Each world's first mission is always open (so a judge can try every world); later missions open when the
        /// previous one in the same world is completed. Open play opens everything.
        /// </summary>
        public static bool Unlocked(string missionId)
        {
            if (Current.Settings.OpenPlay) return true;
            if (string.IsNullOrEmpty(missionId) || missionId.Length < 5) return false;
            int m = missionId[4] - '0';
            if (m <= 1) return true;
            var prev = Current.Get(missionId.Substring(0, 4) + (m - 1));
            return prev != null && prev.Completed;
        }
    }
}
