using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Writes one folder per play session (brief §5.4 layout, Unity side):
    ///
    ///   data/sessions/&lt;YYYYMMDD_HHMMSS&gt;_&lt;participant&gt;_unity/
    ///     session.json   who, when, input, versions
    ///     shots.csv      one row per shot, every §3.9 field (format: Core.ShotCsv)
    ///     levels.csv     one row per finished level
    ///
    /// In the Editor the root is the repo's git-ignored data/sessions/ folder; in a build it is
    /// Application.persistentDataPath/sessions. From P4 on, the Python bridge keeps the
    /// authoritative log on its own clock; this file lets the game be tested on its own.
    /// </summary>
    public sealed class SessionLog : IDisposable
    {
        static readonly CultureInfo C = CultureInfo.InvariantCulture;

        StreamWriter shots;
        StreamWriter levels;

        public string Folder { get; }
        public string ShotsPath => Path.Combine(Folder, "shots.csv");

        public static string RootFolder =>
            Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "data", "sessions"))
                : Path.Combine(Application.persistentDataPath, "sessions");

        [Serializable]
        sealed class SessionMeta
        {
            public string app = "Keeper244";
            public string appVersion;
            public string participant;
            public string input;
            public string startedLocal;
            public string unityVersion;
            public string platform;
            public string device;
        }

        SessionLog(string folder)
        {
            Folder = folder;
        }

        public static SessionLog Create(string participant, string input)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", C);
            string folder = Path.Combine(RootFolder, $"{stamp}_{SafeName(participant)}_unity");
            Directory.CreateDirectory(folder);

            var meta = new SessionMeta
            {
                appVersion = Application.version,
                participant = participant,
                input = input,
                startedLocal = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", C),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                device = SystemInfo.deviceModel,
            };
            File.WriteAllText(Path.Combine(folder, "session.json"), JsonUtility.ToJson(meta, true));

            var log = new SessionLog(folder)
            {
                shots = new StreamWriter(Path.Combine(folder, "shots.csv"), false),
                levels = new StreamWriter(Path.Combine(folder, "levels.csv"), false),
            };
            log.shots.WriteLine(ShotCsv.Header);
            log.levels.WriteLine("participant,input,level,zones,shots,saves,save_fraction,passed,score,best_streak,mean_reaction_time,ended_local");
            log.shots.Flush();
            log.levels.Flush();
            Debug.Log($"[Keeper244] Logging to {folder}");
            return log;
        }

        public void WriteShot(ShotResult r, string participant, string input, string levelId, string camera, bool assist)
        {
            shots.WriteLine(ShotCsv.Row(r, participant, input, levelId, camera, assist));
            shots.Flush(); // never lose a shot if the game is closed mid-level
        }

        public void WriteLevel(LevelResult r, string participant, string input)
        {
            string rt = double.IsNaN(r.MeanReactionTime) ? "" : r.MeanReactionTime.ToString("0.000", C);
            levels.WriteLine(string.Join(",",
                participant, input, r.LevelId, r.Zones.ToString(C), r.Shots.ToString(C), r.Saves.ToString(C),
                r.SaveFraction.ToString("0.000", C), r.Passed ? "1" : "0", r.Score.ToString(C), r.BestStreak.ToString(C),
                rt, DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", C)));
            levels.Flush();
        }

        public void Dispose()
        {
            shots?.Dispose();
            levels?.Dispose();
            shots = levels = null;
        }

        /// <summary>Participant IDs end up in folder names and CSV cells: keep letters, digits, - and _.</summary>
        public static string SafeName(string s)
        {
            string cleaned = Regex.Replace(s ?? "", "[^A-Za-z0-9_-]", "");
            return string.IsNullOrEmpty(cleaned) ? "anon" : cleaned;
        }
    }
}
