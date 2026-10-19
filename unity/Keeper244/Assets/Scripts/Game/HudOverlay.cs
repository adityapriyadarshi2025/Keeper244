using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// In-game HUD (brief §3.7):
    ///  - top-left: level, input + signal (OK / NO SIGNAL), what to do now
    ///  - top-right: score, shot n/N, saves, streak and multiplier
    ///  - bottom-left: activation bars, triceps left and biceps right, with θ_on and θ_far lines
    ///  - bottom-centre: slot strip (keeper slot filled, target outlined during CUE/FLIGHT)
    ///  - centre: points popup after each shot
    /// </summary>
    public sealed class HudOverlay : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField] GameFlow flow;
        [Tooltip("Activation shown at the top of the bars (fraction of MVC).")]
        [SerializeField] float barFullScale = 0.8f;
        [SerializeField] float popupSeconds = 1.3f;

        ShotResult popup;
        float popupStart = -10f;

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
            if (flow != null && flow.CurrentScreen == GameFlow.ScreenId.Menu) return;
            GUI.depth = 10;

            DrawStatus();
            if (!game.IsFreePlay) DrawScore();
            DrawBars();
            DrawSlotStrip();
            DrawPopup();
        }

        void DrawStatus()
        {
            var c = game.Controls;
            var m = game.Machine;
            float x = 20, y = 20, w = 520, h = 150;
            Ui.PanelBox(x, y, w, h);
            Ui.Label(x + 18, y + 14, w - 36, 36, game.Level.settings.levelName, 28, Ui.Text, bold: true);

            bool ok = c != null && c.HasSignal;
            Ui.Fill(x + 20, y + 62, 16, 16, ok ? Ui.Good : Ui.Bad);
            Ui.Label(x + 46, y + 56, w - 60, 30, ok ? $"{c.SourceName}  ·  signal OK" : "NO SIGNAL", 20, ok ? Ui.Muted : Ui.Bad, bold: !ok);

            string prompt;
            Color col = Ui.Info;
            if (game.IsFreePlay)
            {
                prompt = "Free play: flex to move the keeper.  Esc = menu";
            }
            else
            {
                switch (m.State)
                {
                    case RoundState.WaitRest:
                        prompt = $"Relax…  ({m.RestTime:0.0} / {m.Settings.restGateSeconds:0.0} s)";
                        break;
                    case RoundState.Cue:
                        prompt = $"Shot coming {Direction(m.CurrentTarget, m.Settings.zones)}";
                        col = Ui.Cue;
                        break;
                    case RoundState.Flight:
                        prompt = "Ball in flight!";
                        col = Ui.Cue;
                        break;
                    case RoundState.Feedback:
                        bool saved = game.LastShot != null && game.LastShot.Saved;
                        prompt = saved ? "SAVE!" : "GOAL";
                        col = saved ? Ui.Good : Ui.Bad;
                        break;
                    case RoundState.Complete:
                        prompt = "Level complete";
                        col = Ui.Good;
                        break;
                    default:
                        prompt = "";
                        break;
                }
            }
            Ui.Label(x + 18, y + 96, w - 36, 40, prompt, 26, col, bold: true);
        }

        void DrawScore()
        {
            var m = game.Machine;
            float w = 340, h = 190;
            float x = Ui.Width - w - 20, y = 20;
            Ui.PanelBox(x, y, w, h);
            Ui.Label(x + 20, y + 14, w - 40, 24, "SCORE", 18, Ui.Muted);
            Ui.Label(x + 20, y + 36, w - 40, 64, m.Score.ToString(), 54, Ui.Text, bold: true);
            int shotNo = Mathf.Min(m.ShotIndex + 1, m.ShotCount);
            Ui.Label(x + 20, y + 106, w - 40, 30, $"Shot {shotNo} / {m.ShotCount}     Saves {m.Saves}", 22, Ui.Text);
            double mult = ScoreRules.Multiplier(m.Streak);
            Ui.Label(x + 20, y + 140, w - 40, 30, $"Streak {m.Streak}   ×{mult:0.0}", 22, m.Streak > 0 ? Ui.Accent : Ui.Muted);
        }

        void DrawBars()
        {
            var c = game.Controls;
            if (c == null) return;
            var th = c.Thresholds;
            bool showFar = game.Zones == 5;

            float x = 20, w = 300, h = 360;
            float y = Ui.RefHeight - h - 20;
            Ui.PanelBox(x, y, w, h);
            Ui.Label(x + 18, y + 12, w - 36, 26, "MUSCLE ACTIVATION", 18, Ui.Muted);

            // Triceps on the left (moves the keeper left), biceps on the right — like the movement.
            Bar(x + 50, y + 54, c.ActivationTriceps, th.OnTriceps, th.FarTriceps, showFar, "← TRICEPS");
            Bar(x + 175, y + 54, c.ActivationBiceps, th.OnBiceps, th.FarBiceps, showFar, "BICEPS →");

            float ly = y + h - 34;
            Ui.Fill(x + 20, ly + 10, 22, 3, Ui.Text);
            Ui.Label(x + 48, ly, 90, 24, "on", 16, Ui.Muted);
            if (showFar)
            {
                Ui.Fill(x + 110, ly + 10, 22, 3, Ui.Accent);
                Ui.Label(x + 138, ly, 120, 24, "far", 16, Ui.Muted);
            }
        }

        void Bar(float x, float y, float value, float on, float far, bool showFar, string label)
        {
            const float bw = 76f, bh = 220f;
            Ui.Fill(x, y, bw, bh, Ui.PanelLight);
            float f = Mathf.Clamp01(value / barFullScale);
            Color fill = value >= far && showFar ? Ui.Accent : value >= on ? Ui.Cue : new Color(0.55f, 0.6f, 0.65f);
            Ui.Fill(x, y + bh * (1f - f), bw, bh * f, fill);

            Line(x, y, bw, bh, on, Ui.Text);
            if (showFar) Line(x, y, bw, bh, far, Ui.Accent);

            Ui.Label(x - 20, y + bh + 6, bw + 40, 26, label, 17, Ui.Text, TextAnchor.UpperCenter, bold: true);
            Ui.Label(x - 20, y + bh + 30, bw + 40, 24, value.ToString("0.00"), 17, Ui.Muted, TextAnchor.UpperCenter);
        }

        void Line(float x, float y, float bw, float bh, float level, Color c)
        {
            float f = Mathf.Clamp01(level / barFullScale);
            Ui.Fill(x - 6, y + bh * (1f - f) - 1.5f, bw + 12, 3f, c);
        }

        void DrawSlotStrip()
        {
            int zones = game.Zones;
            var m = game.Machine;
            string[] names = zones == 3 ? new[] { "LEFT", "CENTRE", "RIGHT" } : new[] { "L2", "L1", "C", "R1", "R2" };
            float cell = zones == 3 ? 170f : 110f;
            float h = 70f;
            float total = cell * zones;
            float x0 = (Ui.Width - total) / 2f;
            float y = Ui.RefHeight - h - 28;
            int half = (zones - 1) / 2;
            int keeper = game.Keeper.CommandedZoneSlot;

            Ui.Fill(x0 - 8, y - 8, total + 16, h + 16, Ui.Panel);
            for (int i = 0; i < zones; i++)
            {
                int slot = i - half;
                float cx = x0 + i * cell;
                bool isKeeper = slot == keeper;
                Ui.Fill(cx + 4, y, cell - 8, h, isKeeper ? Ui.Accent : Ui.PanelLight);
                Ui.Label(cx, y + 18, cell, 34, names[i], 22, isKeeper ? Color.black : Ui.Text, TextAnchor.UpperCenter, bold: isKeeper);

                if (m != null && m.HasActiveShot && slot == m.CurrentTarget)
                {
                    Color frame = Ui.Cue;
                    if (m.State == RoundState.Feedback && game.LastShot != null)
                        frame = game.LastShot.Saved ? Ui.Good : Ui.Bad;
                    Ui.Frame(cx + 4, y, cell - 8, h, frame, 5f);
                }
            }
        }

        void DrawPopup()
        {
            if (popup == null) return;
            float t = Time.unscaledTime - popupStart;
            if (t > popupSeconds) return;
            float alpha = 1f - Mathf.Clamp01((t - popupSeconds * 0.6f) / (popupSeconds * 0.4f));
            float rise = 30f * t / popupSeconds;

            float w = 420f;
            float x = (Ui.Width - w) / 2f, y = 190f - rise;
            if (popup.Saved)
            {
                var c = new Color(Ui.Good.r, Ui.Good.g, Ui.Good.b, alpha);
                Ui.Label(x, y, w, 80, $"+{popup.Points}", 64, c, TextAnchor.UpperCenter, bold: true);
                string sub = (ScoreRules.IsFar(popup.Target, popup.Zones) ? "far save" : "save") +
                             (popup.Multiplier > 1.0 ? $"   ×{popup.Multiplier:0.0} streak" : "");
                Ui.Label(x, y + 78, w, 34, sub, 24, new Color(1f, 1f, 1f, alpha), TextAnchor.UpperCenter);
            }
            else
            {
                Ui.Label(x, y, w, 80, "GOAL", 64, new Color(Ui.Bad.r, Ui.Bad.g, Ui.Bad.b, alpha), TextAnchor.UpperCenter, bold: true);
            }
        }

        static string Direction(int target, int zones)
        {
            if (target == 0) return "to the CENTRE: stay relaxed";
            string side = target > 0 ? "RIGHT →" : "← LEFT";
            if (zones == 5) return (System.Math.Abs(target) == 2 ? "FAR " : "NEAR ") + side;
            return side;
        }
    }
}
