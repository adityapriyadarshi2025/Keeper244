using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Broadcast-style HUD (brief §3.7):
    ///  - top: scoreboard bar (level, shot n/N, saves, score, streak multiplier)
    ///  - top-left: input + signal chip (OK / NO SIGNAL)
    ///  - centre: big call-outs (RELAX, SHOT INCOMING, SAVE!, GOAL) and the points popup
    ///  - bottom-left: activation bars, triceps left and biceps right, with θ_on and θ_far lines
    ///  - bottom-centre: slot strip (keeper filled, target outlined during CUE/FLIGHT)
    ///
    /// With assist off (GameManager.Assist) the target is not shown: no direction call-out, no strip
    /// outline until the shot is decided.
    ///
    /// Everything left/right is drawn as it appears on SCREEN. In the front camera the keeper faces
    /// the player, so his right (biceps, positive slots) is on the left: the strip, the bars and the
    /// LEFT/RIGHT call-outs flip with it (GameFlow.Mirrored).
    /// </summary>
    public sealed class HudOverlay : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField] GameFlow flow;
        [Tooltip("Activation shown at the top of the bars (fraction of MVC).")]
        [SerializeField] float barFullScale = 0.8f;
        [SerializeField] float popupSeconds = 1.4f;

        ShotResult popup;
        float popupStart = -10f;
        float shownBiceps, shownTriceps;

        bool Mirrored => flow != null && flow.Mirrored;

        void OnEnable()
        {
            if (game != null) game.ShotResolved += OnShot;
        }

        void OnDisable()
        {
            if (game != null) game.ShotResolved -= OnShot;
        }

        void OnShot(ShotResult r)
        {
            popup = r;
            popupStart = Time.unscaledTime;
        }

        void OnGUI()
        {
            if (game == null || !game.IsRunning) return;
            if (flow != null && (flow.CurrentScreen == GameFlow.ScreenId.Menu || flow.CurrentScreen == GameFlow.ScreenId.Intro)) return;
            GUI.depth = 10;

            Ui.TopShade(220f);
            DrawScoreboard();
            DrawSignal();
            DrawCallout();
            DrawBars();
            DrawSlotStrip();
            DrawPopup();
        }

        // ---------------- top ----------------

        void DrawScoreboard()
        {
            var s = game.Level.settings;
            var m = game.Machine;
            float h = 74f;
            float w = game.IsFreePlay ? 420f : 900f;
            float x = (Ui.Width - w) / 2f, y = 22f;
            Ui.RoundBox(x, y, w, h, Ui.Panel);

            // level tag
            Ui.RoundBox(x + 8, y + 8, 92, h - 16, Ui.Accent);
            Ui.Label(x + 8, y + 12, 92, h - 16, s.levelId, 40, Color.black, TextAnchor.MiddleCenter, display: true);
            Ui.Label(x + 116, y + 10, 300, 30, s.levelName.ToUpperInvariant(), 26, Ui.Text, display: true);
            Ui.Label(x + 116, y + 40, 300, 26, s.freePlay ? "FREE PLAY · ESC = MENU" : $"{s.zones} ZONES · CUE {s.cueSeconds:0.0} S", 18, Ui.Muted);
            if (m == null) return;

            float cx = x + 420;
            Stat(cx, y, "SHOT", $"{Mathf.Min(m.ShotIndex + 1, m.ShotCount)}/{m.ShotCount}");
            Stat(cx + 120, y, "SAVES", m.Saves.ToString());
            Stat(cx + 220, y, "SCORE", m.Score.ToString(), Ui.Accent);

            double mult = ScoreRules.Multiplier(m.Streak);
            Color chip = m.Streak > 0 ? Ui.Good : Ui.PanelLight;
            Ui.RoundBox(x + w - 100, y + 14, 86, h - 28, chip);
            Ui.Label(x + w - 100, y + 16, 86, h - 28, $"×{mult:0.0}", 30, m.Streak > 0 ? Color.black : Ui.Muted, TextAnchor.MiddleCenter, display: true);
        }

        static void Stat(float x, float y, string label, string value, Color? valueColour = null)
        {
            Ui.Label(x, y + 8, 110, 20, label, 16, Ui.Muted, TextAnchor.UpperLeft);
            Ui.Label(x, y + 26, 130, 46, value, 40, valueColour ?? Ui.Text, TextAnchor.UpperLeft, display: true);
        }

        void DrawSignal()
        {
            var c = game.Controls;
            bool ok = c != null && c.HasSignal;
            float x = 24, y = 30, w = 250, h = 48;
            Ui.RoundBox(x, y, w, h, Ui.PanelSoft);
            Ui.RoundBox(x + 14, y + 16, 16, 16, ok ? Ui.Good : Ui.Bad);
            Ui.Label(x + 42, y + 10, w - 50, 30, ok ? $"{c.SourceName.ToUpperInvariant()} · SIGNAL OK" : "NO SIGNAL", 22, ok ? Ui.Text : Ui.Bad, display: true);

            // chips for the two toggles: which state, and the key that switches it
            if (flow == null) return;
            y += h + 10;
            KeyChip(x, y, w, "C", flow.Mirrored ? "CAMERA · FRONT" : "CAMERA · BEHIND GOAL", Ui.Muted);
            if (game.IsFreePlay) return;
            y += 50;
            KeyChip(x, y, w, "H", game.Assist ? "ASSIST · ON" : "ASSIST · OFF", game.Assist ? Ui.Muted : Ui.Accent);
        }

        static void KeyChip(float x, float y, float w, string key, string text, Color textColour)
        {
            Ui.RoundBox(x, y, w, 40, Ui.PanelSoft);
            Ui.RoundBox(x + 10, y + 7, 26, 26, Ui.PanelLight);
            Ui.Label(x + 10, y + 8, 26, 26, key, 20, Ui.Text, TextAnchor.UpperCenter, display: true);
            Ui.Label(x + 46, y + 8, w - 54, 26, text, 20, textColour, display: true);
        }

        // ---------------- centre ----------------

        void DrawCallout()
        {
            var m = game.Machine;
            string big = null, small = null;
            Color col = Ui.Text;
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);

            if (game.IsFreePlay)
            {
                small = "FLEX TO MOVE THE KEEPER";
            }
            else
            {
                switch (m.State)
                {
                    case RoundState.WaitRest:
                        big = "RELAX";
                        small = $"HOLD STILL  {m.RestTime:0.0} / {m.Settings.restGateSeconds:0.0} S";
                        col = Ui.Info;
                        break;
                    case RoundState.Cue:
                        if (game.Assist)
                        {
                            big = Direction(m.CurrentTarget, m.Settings.zones, Mirrored);
                            small = "SHOT INCOMING";
                        }
                        else small = "WATCH THE STRIKER";       // assist off: timing only, no direction
                        col = new Color(Ui.Cue.r, Ui.Cue.g, Ui.Cue.b, pulse);
                        break;
                    case RoundState.Flight:
                        if (game.Assist) big = Direction(m.CurrentTarget, m.Settings.zones, Mirrored);
                        col = Ui.Cue;
                        break;
                    case RoundState.Complete:
                        big = "FULL TIME";
                        col = Ui.Good;
                        break;
                }
            }
            if (big != null) Ui.ShadowLabel(0, 150, Ui.Width, 110, big, 92, col);
            if (small != null) Ui.ShadowLabel(0, big != null ? 250 : 150, Ui.Width, 40, small, 30, Ui.Text);
        }

        void DrawPopup()
        {
            if (popup == null) return;
            float t = Time.unscaledTime - popupStart;
            if (t > popupSeconds) return;
            float alpha = 1f - Mathf.Clamp01((t - popupSeconds * 0.65f) / (popupSeconds * 0.35f));
            float pop = 1f + 0.25f * Mathf.Exp(-t * 10f);            // quick scale-in feel via size
            float y = 300f - 30f * t / popupSeconds;

            if (popup.Saved)
            {
                Ui.ShadowLabel(0, y - 150, Ui.Width, 120, "SAVE!", Mathf.RoundToInt(110 * pop), new Color(Ui.Good.r, Ui.Good.g, Ui.Good.b, alpha));
                string sub = $"+{popup.Points}" + (ScoreRules.IsFar(popup.Target, popup.Zones) ? "  FAR SAVE" : "") +
                             (popup.Multiplier > 1.0 ? $"  ×{popup.Multiplier:0.0} STREAK" : "");
                Ui.ShadowLabel(0, y - 30, Ui.Width, 60, sub, 44, new Color(1f, 1f, 1f, alpha));
            }
            else
            {
                Ui.ShadowLabel(0, y - 150, Ui.Width, 120, "GOAL", Mathf.RoundToInt(110 * pop), new Color(Ui.Bad.r, Ui.Bad.g, Ui.Bad.b, alpha));
                string why = popup.Undershoot ? "NOT FAR ENOUGH" : popup.Overshoot ? "TOO FAR" : popup.WrongDirection ? "WRONG WAY" : "";
                if (why.Length > 0) Ui.ShadowLabel(0, y - 30, Ui.Width, 60, why, 40, new Color(1f, 1f, 1f, alpha));
            }
        }

        // ---------------- bottom ----------------

        void DrawBars()
        {
            var c = game.Controls;
            if (c == null) return;
            var th = c.Thresholds;
            bool showFar = game.Zones == 5;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f);
            shownBiceps = Mathf.Lerp(shownBiceps, c.ActivationBiceps, k);
            shownTriceps = Mathf.Lerp(shownTriceps, c.ActivationTriceps, k);

            float w = 300, h = 380;
            float x = 24, y = Ui.RefHeight - h - 24;
            Ui.RoundBox(x, y, w, h, Ui.Panel);
            Ui.Label(x + 20, y + 14, w - 40, 30, "MUSCLE ACTIVATION", 24, Ui.Muted, display: true);

            // each bar sits on the side of the screen its muscle moves the keeper to
            if (Mirrored)
            {
                Bar(x + 50, y + 60, shownBiceps, th.OnBiceps, th.FarBiceps, showFar, "< BICEPS");
                Bar(x + 175, y + 60, shownTriceps, th.OnTriceps, th.FarTriceps, showFar, "TRICEPS >");
            }
            else
            {
                Bar(x + 50, y + 60, shownTriceps, th.OnTriceps, th.FarTriceps, showFar, "< TRICEPS");
                Bar(x + 175, y + 60, shownBiceps, th.OnBiceps, th.FarBiceps, showFar, "BICEPS >");
            }

            float ly = y + h - 36;
            Ui.Fill(x + 24, ly + 11, 24, 3, Ui.Text);
            Ui.Label(x + 54, ly, 80, 26, "ON", 18, Ui.Muted, display: true);
            if (showFar)
            {
                Ui.Fill(x + 120, ly + 11, 24, 3, Ui.Accent);
                Ui.Label(x + 150, ly, 120, 26, "FAR", 18, Ui.Muted, display: true);
            }
        }

        void Bar(float x, float y, float value, float on, float far, bool showFar, string label)
        {
            const float bw = 76f, bh = 220f;
            Ui.RoundBox(x, y, bw, bh, Ui.PanelLight);
            float f = Mathf.Clamp01(value / barFullScale);
            Color fill = showFar && value >= far ? Ui.Accent : value >= on ? Ui.Cue : new Color(0.55f, 0.62f, 0.7f);
            if (f > 0.02f) Ui.RoundBox(x, y + bh * (1f - f), bw, bh * f, fill);

            Line(x, y, bw, bh, on, Ui.Text);
            if (showFar) Line(x, y, bw, bh, far, Ui.Accent);

            Ui.Label(x - 25, y + bh + 8, bw + 50, 28, label, 22, Ui.Text, TextAnchor.UpperCenter, display: true);
            Ui.Label(x - 25, y + bh + 34, bw + 50, 26, value.ToString("0.00"), 20, Ui.Muted, TextAnchor.UpperCenter);
        }

        void Line(float x, float y, float bw, float bh, float level, Color c)
        {
            float f = Mathf.Clamp01(level / barFullScale);
            Ui.Fill(x - 8, y + bh * (1f - f) - 1.5f, bw + 16, 3f, c);
        }

        void DrawSlotStrip()
        {
            int zones = game.Zones;
            var m = game.Machine;
            string[] names = zones == 3 ? new[] { "LEFT", "CENTRE", "RIGHT" } : new[] { "L2", "L1", "C", "R1", "R2" };
            float cell = zones == 3 ? 180f : 116f;
            float h = 76f;
            float total = cell * zones;
            float x0 = (Ui.Width - total) / 2f;
            float y = Ui.RefHeight - h - 30;
            int half = (zones - 1) / 2;
            int keeper = game.Keeper.CommandedZoneSlot;
            bool mirrored = Mirrored;

            Ui.RoundBox(x0 - 12, y - 12, total + 24, h + 24, Ui.Panel);
            for (int i = 0; i < zones; i++)
            {
                int slot = mirrored ? half - i : i - half; // cells are in screen order, names too
                float cx = x0 + i * cell;
                bool isKeeper = slot == keeper;
                // with assist off the target is only revealed afterwards, as feedback
                bool isTarget = m != null && m.HasActiveShot && slot == m.CurrentTarget &&
                                (game.Assist || m.State == RoundState.Feedback);
                Color frame = Ui.Cue;
                if (isTarget && m.State == RoundState.Feedback && game.LastShot != null)
                    frame = game.LastShot.Saved ? Ui.Good : Ui.Bad;

                if (isTarget) Ui.RoundBox(cx + 2, y - 4, cell - 4, h + 8, frame);
                Ui.RoundBox(cx + 8, y + 3, cell - 16, h - 6, isKeeper ? Ui.Accent : new Color(0.16f, 0.2f, 0.26f, 1f));
                Ui.Label(cx, y + 12, cell, 56, names[i], 34, isKeeper ? Color.black : Ui.Text, TextAnchor.UpperCenter, display: true);
            }
        }

        /// <summary>Where the shot goes, as seen on screen.</summary>
        static string Direction(int target, int zones, bool mirrored)
        {
            if (target == 0) return "CENTRE · STAY RELAXED";
            int onScreen = mirrored ? -target : target;
            string side = onScreen > 0 ? "RIGHT >>" : "<< LEFT";
            if (zones == 5) return (System.Math.Abs(target) == 2 ? "FAR " : "NEAR ") + side;
            return side;
        }
    }
}
