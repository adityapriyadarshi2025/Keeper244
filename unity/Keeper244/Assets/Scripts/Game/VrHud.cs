using Keeper244.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Keeper244.Game
{
    /// <summary>
    /// What the participant reads in the headset (brief §3.8). The IMGUI HUD only draws on the
    /// desktop window, so in VR the same information sits in the stadium on three world-space
    /// boards that face the coach seat (world-locked, never head-locked):
    ///
    ///   centre, above the striker   call-out: RELAX / NEAR RIGHT &gt;&gt; / SAVE! / GOAL / NO SIGNAL,
    ///                               plus the level intro, pause and results
    ///   left (~26° off centre)      scoreboard: level, shot n/N, saves, score, streak
    ///   right (~26° off centre)     signal + muscle bars (triceps left, biceps right, ON/FAR lines)
    ///
    /// Built in code on first use (no prefab, no scene rebuild) and hidden on the desktop.
    /// </summary>
    public sealed class VrHud : MonoBehaviour
    {
        const float PixelsToMetres = 0.01f; // 1 canvas pixel = 1 cm in the stadium

        GameManager game;
        GameFlow flow;
        Vector3 eye = new Vector3(0f, 4f, -5.25f);

        GameObject root;
        Text centreBig, centreSmall, scoreTitle, scoreMain, scoreSub, signalText, barLabelL, barLabelR;
        RectTransform fillL, fillR, onL, onR, farL, farR;
        Image fillImgL, fillImgR, centreAccent;
        float shownB, shownT;

        ShotResult popup;
        float popupAt = -10f;
        float voidedAt = -100f;

        public bool Visible => root != null && root.activeSelf;

        public void Bind(GameManager gameManager, GameFlow gameFlow, Vector3 eyePosition)
        {
            game = gameManager;
            flow = gameFlow;
            eye = eyePosition;
        }

        void OnEnable()
        {
            if (game == null) game = GetComponent<GameManager>();
            if (game == null) return;
            game.ShotResolved += OnShot;
            game.ShotVoided += OnVoided;
        }

        void OnDisable()
        {
            if (game == null) return;
            game.ShotResolved -= OnShot;
            game.ShotVoided -= OnVoided;
        }

        void OnShot(ShotResult r)
        {
            popup = r;
            popupAt = Time.unscaledTime;
        }

        void OnVoided(int shot, int target) => voidedAt = Time.unscaledTime;

        public void SetVisible(bool on)
        {
            if (on && root == null) Build();
            if (root != null) root.SetActive(on);
        }

        // ---------------- building ----------------

        void Build()
        {
            root = new GameObject("VR HUD");
            var font = Ui.Display != null ? Ui.Display : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var body = Ui.Body != null ? Ui.Body : font;

            // centre: high above the striker, ~17 m away; big letters ≈ 4° tall
            var centre = Board("Callout", new Vector3(0f, 6.3f, 12f), new Vector2(1000, 300), new Color(0.03f, 0.05f, 0.08f, 0.55f));
            centreAccent = Fill(centre, "Accent", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -8), Vector2.zero, Ui.Accent);
            centreBig = Label(centre, "Big", font, 130, TextAnchor.MiddleCenter, new Vector2(0, 0.35f), new Vector2(1, 1));
            centreSmall = Label(centre, "Small", body, 54, TextAnchor.MiddleCenter, new Vector2(0, 0), new Vector2(1, 0.38f));

            // left: scoreboard
            var score = Board("Scoreboard", new Vector3(-6.5f, 4.2f, 8f), new Vector2(560, 320), Ui.Panel);
            Fill(score, "Accent", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -8), Vector2.zero, Ui.Accent);
            scoreTitle = Label(score, "Level", font, 54, TextAnchor.MiddleCenter, new Vector2(0, 0.72f), new Vector2(1, 0.97f));
            scoreMain = Label(score, "Shot", font, 110, TextAnchor.MiddleCenter, new Vector2(0, 0.30f), new Vector2(1, 0.74f));
            scoreSub = Label(score, "Score", body, 44, TextAnchor.MiddleCenter, new Vector2(0, 0.04f), new Vector2(1, 0.32f));

            // right: signal + muscle bars
            var bars = Board("MuscleBars", new Vector3(6.5f, 4.2f, 8f), new Vector2(560, 420), Ui.Panel);
            Fill(bars, "Accent", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -8), Vector2.zero, Ui.Info);
            signalText = Label(bars, "Signal", font, 50, TextAnchor.MiddleCenter, new Vector2(0, 0.84f), new Vector2(1, 0.98f));
            Bar(bars, 0.18f, out fillL, out fillImgL, out onL, out farL);
            Bar(bars, 0.62f, out fillR, out fillImgR, out onR, out farR);
            barLabelL = Label(bars, "LabelL", font, 40, TextAnchor.MiddleCenter, new Vector2(0.08f, 0.02f), new Vector2(0.48f, 0.16f));
            barLabelR = Label(bars, "LabelR", font, 40, TextAnchor.MiddleCenter, new Vector2(0.52f, 0.02f), new Vector2(0.92f, 0.16f));
            barLabelL.text = "< TRICEPS";
            barLabelR.text = "BICEPS >";

            root.SetActive(false);
        }

        RectTransform Board(string name, Vector3 position, Vector2 sizePx, Color background)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(root.transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.position = position;
            rt.rotation = Quaternion.LookRotation(position - eye, Vector3.up); // readable from the seat
            rt.localScale = Vector3.one * PixelsToMetres;
            Fill(rt, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, background);
            return rt;
        }

        static Image Fill(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = go.GetComponent<Image>();
            img.color = colour;
            img.raycastTarget = false;
            return img;
        }

        static Text Label(RectTransform parent, string name, Font font, int size, TextAnchor align, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(16, 0);
            rt.offsetMax = new Vector2(-16, 0);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Ui.Text;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>One activation bar: a track, a fill growing from the bottom, and the θ_on / θ_far lines.</summary>
        static void Bar(RectTransform parent, float x0, out RectTransform fill, out Image fillImg, out RectTransform on, out RectTransform far)
        {
            var track = Fill(parent, "Track", new Vector2(x0 + 0.04f, 0.18f), new Vector2(x0 + 0.16f, 0.80f), Vector2.zero, Vector2.zero, Ui.PanelLight);
            var trackRt = (RectTransform)track.transform;
            fillImg = Fill(trackRt, "Fill", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, Vector2.zero, Ui.Cue);
            fill = (RectTransform)fillImg.transform;
            on = (RectTransform)Fill(trackRt, "On", new Vector2(-0.3f, 0), new Vector2(1.3f, 0), new Vector2(0, -3), new Vector2(0, 3), Ui.Text).transform;
            far = (RectTransform)Fill(trackRt, "Far", new Vector2(-0.3f, 0), new Vector2(1.3f, 0), new Vector2(0, -3), new Vector2(0, 3), Ui.Accent).transform;
        }

        // ---------------- every frame ----------------

        void LateUpdate()
        {
            if (!Visible || game == null || flow == null) return;
            UpdateCentre();
            UpdateScoreboard();
            UpdateBars();
        }

        void UpdateCentre()
        {
            string big = null, small = null;
            Color colour = Ui.Text;
            var level = flow.CurrentLevel;
            switch (flow.CurrentScreen)
            {
                case GameFlow.ScreenId.Menu:
                    big = "KEEPER<color=#FF9420>244</color>";
                    small = "WAITING FOR THE OPERATOR";
                    break;
                case GameFlow.ScreenId.Intro:
                    if (level != null)
                    {
                        var s = level.settings;
                        string title = s.levelName.Length > 3 ? s.levelName.Substring(s.levelName.IndexOf(' ') + 1) : s.levelName;
                        big = title.ToUpperInvariant();
                        small = s.freePlay ? "PRACTICE · FLEX AND WATCH THE KEEPER" : $"LEVEL {s.levelId.TrimStart('L')} · {s.TotalShots} SHOTS · {s.zones} ZONES";
                        colour = Ui.Text;
                    }
                    break;
                case GameFlow.ScreenId.Paused:
                    big = "PAUSED";
                    small = "THE OPERATOR WILL RESUME";
                    break;
                case GameFlow.ScreenId.Results:
                    var r = flow.LastResult;
                    if (r != null)
                    {
                        big = $"{r.Saves} / {r.Shots} SAVES";
                        small = r.Passed ? $"PASSED · SCORE {r.Score}" : $"SCORE {r.Score} · NEED {r.UnlockSaveFraction:P0} TO PASS";
                        colour = r.Passed ? Ui.Good : Ui.Text;
                    }
                    break;
                default:
                    if (game.SignalLost)
                    {
                        big = "NO SIGNAL";
                        small = Time.unscaledTime - voidedAt < 6f ? "SHOT VOIDED · IT WILL BE REPLAYED" : "ROUND PAUSED · RELAX";
                        colour = Ui.Bad;
                    }
                    else if (popup != null && Time.unscaledTime - popupAt < 1.4f)
                    {
                        big = popup.Saved ? "SAVE!" : "GOAL";
                        small = popup.Saved ? $"+{popup.Points}" : (popup.Undershoot ? "NOT FAR ENOUGH" : popup.Overshoot ? "TOO FAR" : popup.WrongDirection ? "WRONG WAY" : "");
                        colour = popup.Saved ? Ui.Good : Ui.Bad;
                    }
                    else
                    {
                        HudOverlay.Callout(game, false, out big, out small, out colour); // never mirrored in VR
                    }
                    break;
            }
            centreBig.text = big ?? "";
            centreBig.color = colour;
            centreSmall.text = small ?? "";
            centreAccent.color = game.SignalLost && flow.CurrentScreen == GameFlow.ScreenId.Playing ? Ui.Bad : Ui.Accent;
        }

        void UpdateScoreboard()
        {
            var level = game.Level != null ? game.Level : flow.CurrentLevel;
            var m = game.Machine;
            if (level == null) { scoreTitle.text = "KEEPER244"; scoreMain.text = ""; scoreSub.text = ""; return; }
            var s = level.settings;
            scoreTitle.text = $"<color=#FF9420>{s.levelId}</color>  {s.levelName.ToUpperInvariant()}";
            if (m == null)
            {
                scoreMain.text = s.freePlay ? "FREE PLAY" : "";
                scoreSub.text = game.Assist ? "" : "ASSIST OFF";
                return;
            }
            scoreMain.text = $"SHOT {Mathf.Min(m.ShotIndex + 1, m.ShotCount)}/{m.ShotCount}";
            scoreSub.text = $"SAVES {m.Saves}   SCORE {m.Score}   ×{ScoreRules.Multiplier(m.Streak):0.0}" + (game.Assist ? "" : "   ASSIST OFF");
        }

        void UpdateBars()
        {
            var c = game.Controls;
            bool ok = c != null && c.HasSignal;
            signalText.text = !ok ? "NO SIGNAL" : (c.Status != "ok" ? $"{c.SourceName.ToUpperInvariant()} · {c.Status.ToUpperInvariant().Replace('_', ' ')}" : $"{c.SourceName.ToUpperInvariant()} · SIGNAL OK");
            signalText.color = !ok ? Ui.Bad : c.Status != "ok" ? Ui.Accent : Ui.Good;
            if (c == null) return;

            const float fullScale = 0.8f; // same scale as the desktop bars
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f);
            shownB = Mathf.Lerp(shownB, c.ActivationBiceps, k);
            shownT = Mathf.Lerp(shownT, c.ActivationTriceps, k);
            var th = c.Thresholds;
            bool showFar = game.Zones == 5;
            SetBar(fillL, fillImgL, onL, farL, shownT, th.OnTriceps, th.FarTriceps, showFar, fullScale);
            SetBar(fillR, fillImgR, onR, farR, shownB, th.OnBiceps, th.FarBiceps, showFar, fullScale);
        }

        static void SetBar(RectTransform fill, Image img, RectTransform on, RectTransform far, float value, float thOn, float thFar, bool showFar, float fullScale)
        {
            float f = Mathf.Clamp01(value / fullScale);
            fill.anchorMax = new Vector2(1, f);
            img.color = showFar && value >= thFar ? Ui.Accent : value >= thOn ? Ui.Cue : new Color(0.55f, 0.62f, 0.7f);
            Place(on, thOn / fullScale);
            far.gameObject.SetActive(showFar);
            if (showFar) Place(far, thFar / fullScale);
        }

        static void Place(RectTransform line, float f)
        {
            f = Mathf.Clamp01(f);
            line.anchorMin = new Vector2(line.anchorMin.x, f);
            line.anchorMax = new Vector2(line.anchorMax.x, f);
        }
    }
}
