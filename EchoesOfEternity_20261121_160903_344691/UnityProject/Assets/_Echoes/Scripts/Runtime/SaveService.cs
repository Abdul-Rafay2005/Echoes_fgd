using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Echoes
{
    /// <summary>
    /// Versioned run snapshot in Application.persistentDataPath with atomic replacement and a backup.
    /// Settings and best score live in PlayerPrefs so New Game never clears them.
    /// </summary>
    public static class SaveService
    {
        public const string RunFileName = "echoes_run.json";
        const string VolumeKey = "echoes.volume";
        const string SensitivityKey = "echoes.sensitivity";
        const string BestKey = "echoes.bestScore";

        /// <summary>Used by the automated tests to keep player saves untouched.</summary>
        public static string DirectoryOverride;
        /// <summary>Used by the automated tests: settings are kept in memory instead of PlayerPrefs.</summary>
        public static bool MemorySettings;
        static readonly Dictionary<string, float> memory = new Dictionary<string, float>();

        public static string Directory => string.IsNullOrEmpty(DirectoryOverride) ? Application.persistentDataPath : DirectoryOverride;
        public static string RunPath => Path.Combine(Directory, RunFileName);
        static string TempPath => RunPath + ".tmp";
        static string BackupPath => RunPath + ".bak";

        public static bool SaveRun(RunProgress progress)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                string json = JsonUtility.ToJson(progress, true);
                File.WriteAllText(TempPath, json);
                if (File.Exists(RunPath))
                {
                    try
                    {
                        File.Replace(TempPath, RunPath, BackupPath, true);
                    }
                    catch (Exception)
                    {
                        // Some file systems do not support Replace; fall back to copy-then-move.
                        File.Copy(RunPath, BackupPath, true);
                        File.Delete(RunPath);
                        File.Move(TempPath, RunPath);
                    }
                }
                else
                {
                    File.Move(TempPath, RunPath);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Echoes] Save failed: " + e.Message);
                return false;
            }
        }

        public static bool HasRunFile => File.Exists(RunPath) || File.Exists(BackupPath);

        /// <summary>Loads and validates the main save, falling back to the backup. Never throws.</summary>
        public static bool TryLoadRun(ICollection<string> checkpointIds, out RunProgress progress, out string error)
        {
            error = null;
            if (TryLoadFile(RunPath, checkpointIds, out progress, out string mainError)) return true;
            if (TryLoadFile(BackupPath, checkpointIds, out progress, out string backupError))
            {
                Debug.LogWarning("[Echoes] Main save unreadable (" + mainError + "); recovered backup.");
                return true;
            }
            error = mainError ?? backupError ?? "No save";
            progress = null;
            return false;
        }

        static bool TryLoadFile(string path, ICollection<string> checkpointIds, out RunProgress progress, out string error)
        {
            progress = null;
            error = null;
            try
            {
                if (!File.Exists(path)) { error = "No save"; return false; }
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) { error = "Empty save"; return false; }
                var p = JsonUtility.FromJson<RunProgress>(json);
                if (p == null) { error = "Unreadable save"; return false; }
                if (!p.Validate(checkpointIds, out error)) return false;
                progress = p;
                return true;
            }
            catch (Exception e)
            {
                error = "Corrupt save (" + e.GetType().Name + ")";
                return false;
            }
        }

        public static void ClearRun()
        {
            TryDelete(RunPath);
            TryDelete(BackupPath);
            TryDelete(TempPath);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("[Echoes] Could not delete " + path + ": " + e.Message); }
        }

        // ---- settings ---------------------------------------------------------------------------------

        static float GetFloat(string key, float fallback)
        {
            if (MemorySettings) return memory.TryGetValue(key, out float v) ? v : fallback;
            return PlayerPrefs.GetFloat(key, fallback);
        }

        static void SetFloat(string key, float value)
        {
            if (MemorySettings) { memory[key] = value; return; }
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }

        public static float LoadVolume() => Mathf.Clamp01(GetFloat(VolumeKey, 0.8f));
        public static void SaveVolume(float v) => SetFloat(VolumeKey, Mathf.Clamp01(v));
        public static float LoadSensitivity(float fallback) => Mathf.Clamp(GetFloat(SensitivityKey, fallback), 0.2f, 10f);
        public static void SaveSensitivity(float v) => SetFloat(SensitivityKey, Mathf.Clamp(v, 0.2f, 10f));
        public static int LoadBestScore() => Mathf.Max(0, Mathf.RoundToInt(GetFloat(BestKey, 0f)));
        public static void SaveBestScore(int v) => SetFloat(BestKey, Mathf.Max(0, v));
    }
}
