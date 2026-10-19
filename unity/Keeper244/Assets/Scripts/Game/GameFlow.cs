using System;
using Keeper244.Controls;
using Keeper244.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Keeper244.Game
{
    /// <summary>
    /// Screens and session: main menu (participant ID, input, level select), pause, results,
    /// unlock progression (pass a level at ≥ its unlock fraction to open the next) and logging.
    ///
    /// Keys while playing: Esc = pause, R = restart level, F1 = debug panel.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public enum ScreenId { Menu, Playing, Paused, Results }

        [SerializeField] GameManager game;
        [SerializeField] KeyboardInput keyboard;
        [Tooltip("In menu order: L0 Free Play, L1, L2, L3 …")]
        [SerializeField] LevelConfig[] levels = Array.Empty<LevelConfig>();
        [Tooltip("Levels at or below this index are always open (1 = L0 and L1).")]
        [SerializeField] int alwaysUnlocked = 1;

        const string PrefParticipant = "k244.participant";
        const string PrefUnlockedPrefix = "k244.unlocked.";

        ScreenId screen = ScreenId.Menu;
        string participant = "P01";
        string sessionParticipant;
        bool unlockAll;
        int unlocked;
        int currentIndex = -1;
        LevelResult lastResult;
        SessionLog log;
        Vector2 menuScroll;

        public ScreenId CurrentScreen => screen;
        public string Participant => participant;
        public LevelConfig CurrentLevel => currentIndex >= 0 && currentIndex < levels.Length ? levels[currentIndex] : null;
        public string LogFolder => log?.Folder;

        IInputProvider Input => keyboard; // P3 adds EMG over UDP

        void Awake()
        {
            participant = PlayerPrefs.GetString(PrefParticipant, "P01");
            LoadUnlocks();
        }

        void OnEnable()
        {
            if (game == null) return;
            game.ShotResolved += OnShotResolved;
            game.LevelCompleted += OnLevelCompleted;
        }

        void OnDisable()
        {
            if (game == null) return;
            game.ShotResolved -= OnShotResolved;
            game.LevelCompleted -= OnLevelCompleted;
        }

        void Start()
        {
            game.SetInput(Input);
            ShowMenu();
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            log?.Dispose();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (screen == ScreenId.Playing) Pause();
                else if (screen == ScreenId.Paused) Resume();
            }
            if (screen == ScreenId.Playing && kb.rKey.wasPressedThisFrame) StartLevel(currentIndex);
        }

        // ---------------- actions ----------------

        public void StartLevel(int index)
        {
            if (index < 0 || index >= levels.Length || levels[index] == null) return;
            participant = SessionLog.SafeName(participant);
            PlayerPrefs.SetString(PrefParticipant, participant);

            // One log folder per participant per run of the game.
            if (log == null || sessionParticipant != participant)
            {
                log?.Dispose();
                log = null;
                try { log = SessionLog.Create(participant, Input.SourceName); }
                catch (Exception e) { Debug.LogError($"[Keeper244] Could not create the session log: {e.Message}"); }
                sessionParticipant = participant;
            }

            currentIndex = index;
            lastResult = null;
            Time.timeScale = 1f;
            screen = ScreenId.Playing;
            game.StartLevel(levels[index], Input);
        }

        public void Pause()
        {
            screen = ScreenId.Paused;
            game.Paused = true;
            Time.timeScale = 0f;
        }

        public void Resume()
        {
            screen = ScreenId.Playing;
            game.Paused = false;
            Time.timeScale = 1f;
        }

        public void ShowMenu()
        {
            game.StopLevel();
            Time.timeScale = 1f;
            screen = ScreenId.Menu;
            LoadUnlocks();
        }

        bool IsUnlocked(int index) => unlockAll || index <= Mathf.Max(unlocked, alwaysUnlocked);

        void LoadUnlocks() => unlocked = PlayerPrefs.GetInt(PrefUnlockedPrefix + SessionLog.SafeName(participant), alwaysUnlocked);

        void OnShotResolved(ShotResult r)
        {
            var level = CurrentLevel;
            log?.WriteShot(r, participant, Input.SourceName, level != null ? level.settings.levelId : "");
        }

        void OnLevelCompleted(LevelResult r)
        {
            lastResult = r;
            log?.WriteLevel(r, participant, Input.SourceName);
            if (r.Passed && currentIndex + 1 > unlocked)
            {
                unlocked = currentIndex + 1;
                PlayerPrefs.SetInt(PrefUnlockedPrefix + SessionLog.SafeName(participant), unlocked);
                PlayerPrefs.Save();
            }
            screen = ScreenId.Results;
        }

        // ---------------- screens ----------------

        void OnGUI()
        {
            GUI.depth = 0; // above the HUD
            switch (screen)
            {
                case ScreenId.Menu: DrawMenu(); break;
                case ScreenId.Paused: DrawPause(); break;
                case ScreenId.Results: DrawResults(); break;
            }
        }

        void DrawMenu()
        {
            float w = 820f, h = 900f;
            float x = (Ui.Width - w) / 2f, y = (Ui.RefHeight - h) / 2f;
            Ui.Fill(0, 0, Ui.Width, Ui.RefHeight, new Color(0f, 0f, 0f, 0.35f));
            Ui.PanelBox(x, y, w, h);

            Ui.Label(x + 40, y + 28, w - 80, 70, "KEEPER244", 56, Ui.Text, TextAnchor.UpperLeft, bold: true);
            Ui.Label(x + 42, y + 98, w - 80, 34, "EMG goalkeeper · ES244 SSRP · Team 20", 22, Ui.Muted);

            GUILayout.BeginArea(Ui.R(x + 40, y + 150, w - 80, h - 190));

            GUILayout.Label("Participant ID", Ui.LabelStyle(22, Ui.Muted));
            string edited = GUILayout.TextField(participant, 24, Ui.FieldStyle(26));
            if (edited != participant)
            {
                participant = edited;
                LoadUnlocks();
            }

            GUILayout.Space(Ui.S(14));
            GUILayout.Label("Input", Ui.LabelStyle(22, Ui.Muted));
            GUILayout.BeginHorizontal();
            GUILayout.Toggle(true, "  Keyboard (A/D near, Shift = far)", Ui.ToggleStyle(22));
            GUI.enabled = false;
            GUILayout.Toggle(false, "  EMG via UDP (Phase 3)", Ui.ToggleStyle(22));
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(Ui.S(18));
            GUILayout.Label("Levels", Ui.LabelStyle(22, Ui.Muted));
            menuScroll = GUILayout.BeginScrollView(menuScroll, GUILayout.Height(Ui.S(380)));
            for (int i = 0; i < levels.Length; i++)
            {
                var lv = levels[i];
                if (lv == null) continue;
                var s = lv.settings;
                string detail = s.freePlay
                    ? "practice · no shots · keeper follows your muscles"
                    : $"{s.TotalShots} shots · {s.zones} zones · cue {s.cueSeconds:0.#} s · flight {s.flightSeconds:0.#} s";
                bool open = IsUnlocked(i);
                GUI.enabled = open;
                string label = open
                    ? $"<b>{s.levelName}</b>   <size={(int)Ui.S(18)}>{detail}</size>"
                    : $"<b>{s.levelName}</b>   <size={(int)Ui.S(18)}>locked · pass the previous level (≥ {levels[Mathf.Max(0, i - 1)].settings.unlockSaveFraction:P0})</size>";
                if (GUILayout.Button(label, Ui.ButtonStyle(24))) StartLevel(i);
                GUI.enabled = true;
                GUILayout.Space(Ui.S(6));
            }
            GUILayout.EndScrollView();

            unlockAll = GUILayout.Toggle(unlockAll, "  Unlock all levels (operator / experiments)", Ui.ToggleStyle(20));
            GUILayout.Space(Ui.S(10));
            GUILayout.Label("In game: Esc = pause · R = restart · F1 = debug panel", Ui.LabelStyle(18, Ui.Muted));
            GUILayout.Label($"Logs: {SessionLog.RootFolder}", Ui.LabelStyle(16, Ui.Muted));
            GUILayout.EndArea();
        }

        void DrawPause()
        {
            float w = 460f, h = 360f;
            float x = (Ui.Width - w) / 2f, y = (Ui.RefHeight - h) / 2f;
            Ui.Fill(0, 0, Ui.Width, Ui.RefHeight, new Color(0f, 0f, 0f, 0.45f));
            Ui.PanelBox(x, y, w, h, Ui.Info);
            Ui.Label(x, y + 26, w, 50, "Paused", 40, Ui.Text, TextAnchor.UpperCenter, bold: true);

            GUILayout.BeginArea(Ui.R(x + 50, y + 100, w - 100, h - 120));
            if (GUILayout.Button("Resume  (Esc)", Ui.ButtonStyle(24))) Resume();
            GUILayout.Space(Ui.S(8));
            if (GUILayout.Button("Restart level  (R)", Ui.ButtonStyle(24))) StartLevel(currentIndex);
            GUILayout.Space(Ui.S(8));
            if (GUILayout.Button("Main menu", Ui.ButtonStyle(24))) ShowMenu();
            GUILayout.EndArea();
        }

        void DrawResults()
        {
            var r = lastResult;
            if (r == null) { ShowMenu(); return; }

            float w = 820f, h = 760f;
            float x = (Ui.Width - w) / 2f, y = (Ui.RefHeight - h) / 2f;
            Ui.Fill(0, 0, Ui.Width, Ui.RefHeight, new Color(0f, 0f, 0f, 0.45f));
            Ui.PanelBox(x, y, w, h, r.Passed ? Ui.Good : Ui.Bad);

            Ui.Label(x + 40, y + 28, w - 80, 50, r.LevelName, 40, Ui.Text, bold: true);
            Ui.Label(x + 40, y + 90, w - 80, 70, $"{r.Saves} / {r.Shots} saves  ({r.SaveFraction:P0})", 48, Ui.Text, bold: true);
            Ui.Label(x + 40, y + 160, w - 80, 40,
                r.Passed ? "PASSED: next level unlocked" : $"Not passed: need {r.UnlockSaveFraction:P0} to unlock the next level",
                26, r.Passed ? Ui.Good : Ui.Bad, bold: true);

            string rt = double.IsNaN(r.MeanReactionTime) ? "—" : $"{r.MeanReactionTime * 1000:0} ms";
            Ui.Label(x + 40, y + 220, 360, 120, $"Score\n<size={(int)Ui.S(44)}><b>{r.Score}</b></size>", 22, Ui.Muted);
            Ui.Label(x + 300, y + 220, 240, 120, $"Best streak\n<size={(int)Ui.S(44)}><b>{r.BestStreak}</b></size>", 22, Ui.Muted);
            Ui.Label(x + 540, y + 220, 260, 120, $"Mean reaction\n<size={(int)Ui.S(44)}><b>{rt}</b></size>", 22, Ui.Muted);

            // Per-zone saves, left to right
            Ui.Label(x + 40, y + 350, w - 80, 30, "Saves per target zone", 22, Ui.Muted);
            string[] names = r.Zones == 3 ? new[] { "LEFT", "CENTRE", "RIGHT" } : new[] { "L2", "L1", "C", "R1", "R2" };
            float cellW = (w - 80) / r.Zones;
            for (int i = 0; i < r.Zones && i < r.ZoneShots.Length; i++)
            {
                float cx = x + 40 + i * cellW;
                float frac = r.ZoneShots[i] == 0 ? 0f : (float)r.ZoneSaves[i] / r.ZoneShots[i];
                Ui.Fill(cx + 4, y + 388, cellW - 8, 90, Ui.PanelLight);
                Ui.Fill(cx + 4, y + 388 + 90 * (1f - frac), cellW - 8, 90 * frac, new Color(Ui.Good.r, Ui.Good.g, Ui.Good.b, 0.45f));
                Ui.Label(cx, y + 398, cellW, 40, $"{r.ZoneSaves[i]}/{r.ZoneShots[i]}", 30, Ui.Text, TextAnchor.UpperCenter, bold: true);
                Ui.Label(cx, y + 440, cellW, 30, names[i], 20, Ui.Muted, TextAnchor.UpperCenter);
            }

            if (log != null)
                Ui.Label(x + 40, y + 500, w - 80, 60, $"Logged to {log.ShotsPath}", 16, Ui.Muted);

            GUILayout.BeginArea(Ui.R(x + 40, y + h - 170, w - 80, 150));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Retry", Ui.ButtonStyle(24))) StartLevel(currentIndex);
            int next = currentIndex + 1;
            GUI.enabled = next < levels.Length && IsUnlocked(next);
            if (GUILayout.Button(next < levels.Length ? $"Next: {levels[next].settings.levelName}" : "Last level", Ui.ButtonStyle(24))) StartLevel(next);
            GUI.enabled = true;
            if (GUILayout.Button("Main menu", Ui.ButtonStyle(24))) ShowMenu();
            GUILayout.EndHorizontal();
            if (log != null)
            {
                GUILayout.Space(Ui.S(8));
                if (GUILayout.Button("Open log folder", Ui.ButtonStyle(18)))
                    Application.OpenURL(new Uri(log.Folder).AbsoluteUri);
            }
            GUILayout.EndArea();
        }
    }
}
