using System;
using Keeper244.Controls;
using Keeper244.Core;
using Keeper244.Look;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Keeper244.Game
{
    /// <summary>
    /// Screens and session: main menu (participant ID, input, level cards with best score and
    /// stars), a short intro card before each level, pause, results, unlock progression
    /// (pass a level at ≥ its unlock fraction to open the next) and logging.
    ///
    /// Keys while playing: Esc = pause, R = restart level, C = switch camera, H = assist on/off,
    /// F1 = debug panel. The camera (behind the goal / front) and assist choices are remembered.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public enum ScreenId { Menu, Intro, Playing, Paused, Results }

        [SerializeField] GameManager game;
        [SerializeField] KeyboardInput keyboard;
        [SerializeField] CameraDirector cameraDirector;
        [Tooltip("In menu order: L0 Free Play, L1, L2, L3 …")]
        [SerializeField] LevelConfig[] levels = Array.Empty<LevelConfig>();
        [Tooltip("Levels at or below this index are always open (1 = L0 and L1).")]
        [SerializeField] int alwaysUnlocked = 1;
        [SerializeField] float introSeconds = 2.2f;

        [Header("Fonts")]
        [SerializeField] Font displayFont;
        [SerializeField] Font bodyFont;

        /// <summary>Bump in SceneBuilder-facing changes: the menu then asks for a scene rebuild.</summary>
        public const int SceneVersionCurrent = 2;
        [Tooltip("Written by Keeper244 → Build Main Scene. If it is not SceneVersionCurrent, the menu shows a rebuild banner.")]
        [SerializeField] int sceneVersion;

        const string PrefParticipant = "k244.participant";
        const string PrefUnlockedPrefix = "k244.unlocked.";
        const string PrefBestPrefix = "k244.best.";
        const string PrefCamera = "k244.camera";
        const string PrefAssist = "k244.assist";

        ScreenId screen = ScreenId.Menu;
        string participant = "P01";
        string sessionParticipant;
        bool unlockAll;
        int unlocked;
        int currentIndex = -1;
        float introStart;
        LevelResult lastResult;
        bool lastWasBest;
        SessionLog log;
        Vector2 menuScroll;

        public ScreenId CurrentScreen => screen;
        public string Participant => participant;
        public LevelConfig CurrentLevel => currentIndex >= 0 && currentIndex < levels.Length ? levels[currentIndex] : null;
        public string LogFolder => log?.Folder;

        /// <summary>Front camera: the keeper faces the player, so screen left = keeper's right.</summary>
        public bool Mirrored => cameraDirector != null && cameraDirector.Mirrored;

        CameraDirector.View CameraView => cameraDirector != null ? cameraDirector.Current : CameraDirector.View.BehindGoal;

        IInputProvider Input => keyboard; // P3 adds EMG over UDP

        void Awake()
        {
            Ui.Display = displayFont;
            Ui.Body = bodyFont;
            participant = PlayerPrefs.GetString(PrefParticipant, "P01");
            LoadUnlocks();
        }

        void OnEnable()
        {
            if (cameraDirector != null) cameraDirector.ViewChanged += OnViewChanged;
            if (game == null) return;
            game.ShotResolved += OnShotResolved;
            game.LevelCompleted += OnLevelCompleted;
        }

        void OnDisable()
        {
            if (cameraDirector != null) cameraDirector.ViewChanged -= OnViewChanged;
            if (game == null) return;
            game.ShotResolved -= OnShotResolved;
            game.LevelCompleted -= OnLevelCompleted;
        }

        void Start()
        {
            game.SetInput(Input);
            game.Assist = PlayerPrefs.GetInt(PrefAssist, 1) == 1;
            if (cameraDirector != null)
            {
                var saved = PlayerPrefs.GetInt(PrefCamera, 0) == 1 ? CameraDirector.View.Front : CameraDirector.View.BehindGoal;
                cameraDirector.SetView(saved, instant: true);
            }
            ApplyMirror();
            ShowMenu();
            if (sceneVersion != SceneVersionCurrent)
                Debug.LogWarning($"[Keeper244] Main.unity was built by an older SceneBuilder (scene version {sceneVersion}, scripts expect {SceneVersionCurrent}). " +
                                 "Stop Play and run Keeper244 → Build Main Scene → Rebuild.");
        }

        void OnViewChanged(CameraDirector.View view)
        {
            PlayerPrefs.SetInt(PrefCamera, view == CameraDirector.View.Front ? 1 : 0);
            ApplyMirror();
        }

        void ApplyMirror()
        {
            if (keyboard != null) keyboard.MirrorLeftRight = Mirrored;
        }

        public void SetCamera(CameraDirector.View view)
        {
            if (cameraDirector != null) cameraDirector.SetView(view);
        }

        public void ToggleCamera()
        {
            if (cameraDirector != null) cameraDirector.ToggleView();
        }

        /// <summary>Assist: show where the ball is going (call-out, ground marker, strip outline).</summary>
        public void SetAssist(bool on)
        {
            game.Assist = on;
            PlayerPrefs.SetInt(PrefAssist, on ? 1 : 0);
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            log?.Dispose();
        }

        void Update()
        {
            if (screen == ScreenId.Intro && Time.unscaledTime - introStart >= introSeconds)
                BeginPlay();

            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (screen == ScreenId.Playing) Pause();
                else if (screen == ScreenId.Paused) Resume();
            }
            if (screen == ScreenId.Playing && kb.rKey.wasPressedThisFrame) StartLevel(currentIndex);
            // not on the menu: the participant ID field may be taking typed letters
            if (screen != ScreenId.Menu && kb.cKey.wasPressedThisFrame) ToggleCamera();
            if (screen != ScreenId.Menu && kb.hKey.wasPressedThisFrame) SetAssist(!game.Assist);
        }

        // ---------------- actions ----------------

        /// <summary>Shows the intro card, then starts the level.</summary>
        public void StartLevel(int index)
        {
            if (index < 0 || index >= levels.Length || levels[index] == null) return;
            participant = SessionLog.SafeName(participant);
            PlayerPrefs.SetString(PrefParticipant, participant);

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
            game.StopLevel();
            if (cameraDirector != null) cameraDirector.SetMenu(false);
            screen = ScreenId.Intro;
            introStart = Time.unscaledTime;
        }

        void BeginPlay()
        {
            screen = ScreenId.Playing;
            game.StartLevel(levels[currentIndex], Input);
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
            if (cameraDirector != null) cameraDirector.SetMenu(true);
            LoadUnlocks();
        }

        bool IsUnlocked(int index) => unlockAll || index <= Mathf.Max(unlocked, alwaysUnlocked);

        string Key(string prefix) => prefix + SessionLog.SafeName(participant);

        void LoadUnlocks() => unlocked = PlayerPrefs.GetInt(Key(PrefUnlockedPrefix), alwaysUnlocked);

        int Best(LevelConfig lv) => lv == null ? 0 : PlayerPrefs.GetInt($"{Key(PrefBestPrefix)}.{lv.settings.levelId}", 0);

        static int Stars(double saveFraction, float unlock) =>
            saveFraction >= 0.999 ? 3 : saveFraction >= Math.Max(unlock, 0.85) ? 2 : saveFraction >= unlock ? 1 : 0;

        void OnShotResolved(ShotResult r)
        {
            var level = CurrentLevel;
            log?.WriteShot(r, participant, Input.SourceName, level != null ? level.settings.levelId : "",
                           CameraDirector.Name(CameraView), game.AssistAtCue);
        }

        void OnLevelCompleted(LevelResult r)
        {
            lastResult = r;
            log?.WriteLevel(r, participant, Input.SourceName);

            lastWasBest = r.Score > Best(CurrentLevel);
            if (lastWasBest) PlayerPrefs.SetInt($"{Key(PrefBestPrefix)}.{r.LevelId}", r.Score);
            if (r.Passed && currentIndex + 1 > unlocked)
            {
                unlocked = currentIndex + 1;
                PlayerPrefs.SetInt(Key(PrefUnlockedPrefix), unlocked);
            }
            PlayerPrefs.Save();
            screen = ScreenId.Results;
        }

        // ---------------- screens ----------------

        void OnGUI()
        {
            GUI.depth = 0; // above the HUD
            switch (screen)
            {
                case ScreenId.Menu: DrawMenu(); break;
                case ScreenId.Intro: DrawIntro(); break;
                case ScreenId.Paused: DrawPause(); break;
                case ScreenId.Results: DrawResults(); break;
            }
        }

        void DrawMenu()
        {
            Ui.Fill(0, 0, Ui.Width, Ui.RefHeight, new Color(0f, 0f, 0f, 0.25f));

            // ---- title, left ----
            float lx = 90f;
            Ui.ShadowLabel(lx, 150, 900, 170, "KEEPER<color=#FF9420>244</color>", 170, Ui.Text, TextAnchor.UpperLeft);
            Ui.ShadowLabel(lx + 6, 320, 900, 50, "THE EMG GOALKEEPER", 44, Ui.Text, TextAnchor.UpperLeft);
            Ui.Label(lx + 8, 380, 700, 120,
                "Flex your biceps to dive right, your triceps to dive left, stay relaxed to hold the centre. " +
                "Flex harder to reach the far post.", 24, Ui.Text);
            Ui.Label(lx + 8, Ui.RefHeight - 90, 900, 30, "ES244 SENSOR SYSTEMS · TEAM 20 · IIT GANDHINAGAR", 22, Ui.Muted, display: true);

            if (Application.isEditor && sceneVersion != SceneVersionCurrent)
            {
                // the scene is generated by code: scripts changed since it was last built
                float bw = 980f, bx = lx, by = 40f;
                Ui.RoundBox(bx, by, bw, 64, new Color(Ui.Bad.r, Ui.Bad.g, Ui.Bad.b, 0.92f));
                Ui.Label(bx + 24, by + 14, bw - 48, 40, "SCENE OUT OF DATE · STOP PLAY, THEN KEEPER244 > BUILD MAIN SCENE > REBUILD", 26, Color.white, display: true);
            }
            Ui.Label(lx + 8, Ui.RefHeight - 60, 900, 30, "ESC PAUSE · R RESTART · C CAMERA · H ASSIST · F1 DEBUG · A/D NEAR · SHIFT FAR", 20, Ui.Muted, display: true);

            // ---- panel, right ----
            float w = 640f, h = 900f;
            float x = Ui.Width - w - 80f, y = (Ui.RefHeight - h) / 2f;
            Ui.RoundBox(x, y, w, h, Ui.Panel);

            GUILayout.BeginArea(Ui.R(x + 36, y + 30, w - 72, h - 60));
            GUILayout.Label("PARTICIPANT", Ui.LabelStyle(22, Ui.Muted, TextAnchor.UpperLeft, display: true));
            string edited = GUILayout.TextField(participant, 24, Ui.FieldStyle(30));
            if (edited != participant)
            {
                participant = edited;
                LoadUnlocks();
            }

            GUILayout.Space(Ui.S(14));
            GUILayout.Label("INPUT", Ui.LabelStyle(22, Ui.Muted, TextAnchor.UpperLeft, display: true));
            GUILayout.BeginHorizontal();
            GUILayout.Toggle(true, "  Keyboard", Ui.ToggleStyle(24));
            GUI.enabled = false;
            GUILayout.Toggle(false, "  EMG (Phase 3)", Ui.ToggleStyle(24));
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(Ui.S(14));
            GUILayout.Label("VIEW  (C)", Ui.LabelStyle(22, Ui.Muted, TextAnchor.UpperLeft, display: true));
            GUILayout.BeginHorizontal();
            var view = CameraView;
            bool behind = GUILayout.Toggle(view == CameraDirector.View.BehindGoal, "  Behind the goal", Ui.ToggleStyle(24));
            bool front = GUILayout.Toggle(view == CameraDirector.View.Front, "  Front (TV view)", Ui.ToggleStyle(24));
            GUILayout.EndHorizontal();
            if (behind && view != CameraDirector.View.BehindGoal) SetCamera(CameraDirector.View.BehindGoal);
            else if (front && view != CameraDirector.View.Front) SetCamera(CameraDirector.View.Front);
            GUILayout.Space(Ui.S(4));
            bool assist = GUILayout.Toggle(game.Assist, "  Assist: show where the ball is going  (H)", Ui.ToggleStyle(24));
            if (assist != game.Assist) SetAssist(assist);

            GUILayout.Space(Ui.S(14));
            GUILayout.Label("CHOOSE A LEVEL", Ui.LabelStyle(22, Ui.Muted, TextAnchor.UpperLeft, display: true));
            menuScroll = GUILayout.BeginScrollView(menuScroll, GUIStyle.none, GUIStyle.none, GUILayout.Height(Ui.S(410)));
            for (int i = 0; i < levels.Length; i++)
            {
                var lv = levels[i];
                if (lv == null) continue;
                var s = lv.settings;
                bool open = IsUnlocked(i);
                string detail = s.freePlay
                    ? "Practice · no shots · keeper follows your muscles"
                    : $"{s.TotalShots} shots · {s.zones} zones · cue {s.cueSeconds:0.#} s · flight {s.flightSeconds:0.#} s";
                if (!open) detail = $"LOCKED · save {levels[Mathf.Max(0, i - 1)].settings.unlockSaveFraction:P0} in the previous level";
                int best = Best(lv);
                string bestTxt = !s.freePlay && best > 0 ? $"   <color=#FF9420>BEST {best}</color>" : "";
                string label = $"<size={(int)Ui.S(34)}>{s.levelName.ToUpperInvariant()}</size>{bestTxt}\n<color=#AAB4C0>{detail}</color>";

                GUI.enabled = open;
                var style = Ui.CardStyle();
                if (GUILayout.Button(label, style)) StartLevel(i);
                GUI.enabled = true;
                GUILayout.Space(Ui.S(10));
            }
            GUILayout.EndScrollView();

            unlockAll = GUILayout.Toggle(unlockAll, "  Unlock all levels (operator / experiments)", Ui.ToggleStyle(20));
            GUILayout.Space(Ui.S(6));
            GUILayout.Label($"Logs: {SessionLog.RootFolder}", Ui.LabelStyle(16, Ui.Muted));
            GUILayout.EndArea();
        }

        void DrawIntro()
        {
            var lv = CurrentLevel;
            if (lv == null) return;
            var s = lv.settings;
            float t = Time.unscaledTime - introStart;
            float a = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((introSeconds - t) / 0.3f);

            float h = 300f, y = (Ui.RefHeight - h) / 2f - 60f;
            Ui.Fill(0, y, Ui.Width, h, new Color(0.03f, 0.05f, 0.08f, 0.82f * a));
            Ui.Fill(0, y, Ui.Width, 6, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, a));
            Ui.Fill(0, y + h - 6, Ui.Width, 6, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, a));

            Ui.ShadowLabel(0, y + 30, Ui.Width, 50, $"LEVEL {s.levelId.TrimStart('L')}", 40, new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, a));
            string title = s.levelName.Length > 3 ? s.levelName.Substring(s.levelName.IndexOf(' ') + 1) : s.levelName;
            Ui.ShadowLabel(0, y + 76, Ui.Width, 130, title.ToUpperInvariant(), 120, new Color(1f, 1f, 1f, a));
            string sub = s.freePlay
                ? "PRACTICE — FLEX AND WATCH THE KEEPER FOLLOW"
                : $"{s.TotalShots} SHOTS · {s.zones} ZONES · SAVE {s.unlockSaveFraction:P0} TO PASS";
            Ui.ShadowLabel(0, y + 210, Ui.Width, 50, sub, 34, new Color(Ui.Muted.r, Ui.Muted.g, Ui.Muted.b, a));
        }

        void DrawPause()
        {
            float w = 560f, h = 580f;
            float x = (Ui.Width - w) / 2f, y = (Ui.RefHeight - h) / 2f;
            Ui.Fill(0, 0, Ui.Width, Ui.RefHeight, new Color(0f, 0f, 0f, 0.5f));
            Ui.RoundBox(x, y, w, h, Ui.Panel);
            Ui.Label(x, y + 26, w, 80, "PAUSED", 72, Ui.Text, TextAnchor.UpperCenter, display: true);

            GUILayout.BeginArea(Ui.R(x + 60, y + 130, w - 120, h - 150));
            if (GUILayout.Button("RESUME  (ESC)", Ui.ButtonStyle(30, primary: true))) Resume();
            GUILayout.Space(Ui.S(12));
            if (GUILayout.Button("RESTART LEVEL  (R)", Ui.ButtonStyle(30))) StartLevel(currentIndex);
            GUILayout.Space(Ui.S(12));
            string cameraLabel = Mirrored ? "CAMERA: FRONT  (C)" : "CAMERA: BEHIND GOAL  (C)";
            if (GUILayout.Button(cameraLabel, Ui.ButtonStyle(30))) ToggleCamera();
            GUILayout.Space(Ui.S(12));
            if (GUILayout.Button(game.Assist ? "ASSIST: ON  (H)" : "ASSIST: OFF  (H)", Ui.ButtonStyle(30))) SetAssist(!game.Assist);
            GUILayout.Space(Ui.S(12));
            if (GUILayout.Button("MAIN MENU", Ui.ButtonStyle(30))) ShowMenu();
            GUILayout.EndArea();
        }

        void DrawResults()
        {
            var r = lastResult;
            if (r == null) { ShowMenu(); return; }

            float w = 900f, h = 820f;
            float x = (Ui.Width - w) / 2f, y = (Ui.RefHeight - h) / 2f;
            Ui.Fill(0, 0, Ui.Width, Ui.RefHeight, new Color(0f, 0f, 0f, 0.45f));
            Ui.RoundBox(x, y, w, h, Ui.Panel);
            Ui.RoundBox(x, y, w, 10, r.Passed ? Ui.Good : Ui.Bad);

            Ui.Label(x, y + 30, w, 40, "FULL TIME · " + r.LevelName.ToUpperInvariant(), 34, Ui.Muted, TextAnchor.UpperCenter, display: true);

            // stars
            int stars = Stars(r.SaveFraction, r.UnlockSaveFraction);
            float sx = x + w / 2f - 1.5f * 110f;
            for (int i = 0; i < 3; i++)
            {
                bool lit = i < stars;
                float size = i == 1 ? 110f : 90f;
                float yy = y + (i == 1 ? 74f : 92f);
                Ui.Star(sx + i * 110f + (110f - size) / 2f, yy, size, lit ? Ui.Cue : new Color(1f, 1f, 1f, 0.12f));
            }

            Ui.Label(x, y + 196, w, 90, $"{r.Saves} / {r.Shots} SAVES", 80, Ui.Text, TextAnchor.UpperCenter, display: true);
            Ui.Label(x, y + 280, w, 40,
                r.Passed ? $"PASSED ({r.SaveFraction:P0}) · NEXT LEVEL UNLOCKED" : $"{r.SaveFraction:P0} · NEED {r.UnlockSaveFraction:P0} TO UNLOCK THE NEXT LEVEL",
                28, r.Passed ? Ui.Good : Ui.Bad, TextAnchor.UpperCenter, display: true);

            // stat tiles
            string rt = double.IsNaN(r.MeanReactionTime) ? "—" : $"{r.MeanReactionTime * 1000:0} ms";
            Tile(x + 60, y + 340, 250, "SCORE", r.Score.ToString(), lastWasBest ? "NEW BEST!" : null);
            Tile(x + 325, y + 340, 250, "BEST STREAK", r.BestStreak.ToString(), null);
            Tile(x + 590, y + 340, 250, "MEAN REACTION", rt, null);

            // per-zone saves, in screen order (the front camera shows the keeper's right on the left)
            Ui.Label(x + 60, y + 480, w - 120, 30, "SAVES PER TARGET ZONE", 22, Ui.Muted, display: true);
            string[] names = r.Zones == 3 ? new[] { "LEFT", "CENTRE", "RIGHT" } : new[] { "L2", "L1", "C", "R1", "R2" };
            float cellW = (w - 120) / r.Zones;
            bool mirrored = Mirrored;
            for (int i = 0; i < r.Zones && i < r.ZoneShots.Length; i++)
            {
                int z = mirrored ? r.Zones - 1 - i : i; // ZoneShots is indexed by keeper-relative slot
                float cx = x + 60 + i * cellW;
                float frac = r.ZoneShots[z] == 0 ? 0f : (float)r.ZoneSaves[z] / r.ZoneShots[z];
                Ui.RoundBox(cx + 4, y + 514, cellW - 8, 92, Ui.PanelLight);
                if (frac > 0.05f) Ui.RoundBox(cx + 4, y + 514 + 92 * (1f - frac), cellW - 8, 92 * frac, new Color(Ui.Good.r, Ui.Good.g, Ui.Good.b, 0.5f));
                Ui.Label(cx, y + 522, cellW, 44, $"{r.ZoneSaves[z]}/{r.ZoneShots[z]}", 40, Ui.Text, TextAnchor.UpperCenter, display: true);
                Ui.Label(cx, y + 566, cellW, 30, names[i], 22, Ui.Muted, TextAnchor.UpperCenter, display: true);
            }

            GUILayout.BeginArea(Ui.R(x + 60, y + h - 190, w - 120, 180));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("RETRY", Ui.ButtonStyle(30))) StartLevel(currentIndex);
            int next = currentIndex + 1;
            bool canNext = next < levels.Length && IsUnlocked(next);
            GUI.enabled = canNext;
            if (GUILayout.Button(next < levels.Length ? $"NEXT: {levels[next].settings.levelId}" : "LAST LEVEL", Ui.ButtonStyle(30, primary: canNext))) StartLevel(next);
            GUI.enabled = true;
            if (GUILayout.Button("MENU", Ui.ButtonStyle(30))) ShowMenu();
            GUILayout.EndHorizontal();
            if (log != null)
            {
                GUILayout.Space(Ui.S(10));
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Logged to {log.ShotsPath}", Ui.LabelStyle(16, Ui.Muted));
                if (GUILayout.Button("OPEN FOLDER", Ui.ButtonStyle(18), GUILayout.Width(Ui.S(200))))
                    Application.OpenURL(new Uri(log.Folder).AbsoluteUri);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        static void Tile(float x, float y, float w, string label, string value, string badge)
        {
            Ui.RoundBox(x, y, w, 120, Ui.PanelLight);
            Ui.Label(x + 20, y + 14, w - 40, 26, label, 22, Ui.Muted, display: true);
            Ui.Label(x + 20, y + 40, w - 40, 70, value, 60, Ui.Text, display: true);
            if (badge != null)
            {
                Ui.RoundBox(x + w - 130, y + 12, 118, 32, Ui.Accent);
                Ui.Label(x + w - 130, y + 14, 118, 32, badge, 22, Color.black, TextAnchor.UpperCenter, display: true);
            }
        }
    }
}
