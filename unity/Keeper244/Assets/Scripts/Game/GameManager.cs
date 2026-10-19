using System;
using Keeper244.Controls;
using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Plays one level: reads the input every frame, moves the keeper, ticks the round state
    /// machine (Core) and turns its events into ball, striker and marker visuals. GameFlow decides
    /// which level runs and when; HUD, audio and the logger listen to the events below.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Scene")]
        [SerializeField] KeeperController keeper;
        [SerializeField] BallController ball;
        [SerializeField] StrikerController striker;
        [SerializeField] ZoneMarker marker;

        [Header("Shot visuals (do not affect the result)")]
        [Tooltip("Height range of the ball at the goal, metres. Crossbar is at 2.44 m.")]
        [SerializeField] Vector2 ballHeightRange = new Vector2(0.3f, 1.9f);
        [Tooltip("Random sideways offset inside the zone, as a fraction of zone width.")]
        [SerializeField, Range(0f, 0.45f)] float jitterFraction = 0.3f;

        RoundStateMachine machine;
        IInputProvider controls;
        LevelConfig level;
        readonly System.Random visualRng = new System.Random();
        Vector3 shotPoint;

        /// <summary>Null in free play and when no level is running.</summary>
        public RoundStateMachine Machine => machine;
        public IInputProvider Controls => controls;
        public LevelConfig Level => level;
        public KeeperController Keeper => keeper;
        public BallController Ball => ball;
        /// <summary>Where the current shot is going (valid from CUE).</summary>
        public Vector3 ShotPoint => shotPoint;
        public bool IsRunning => level != null;
        public bool IsFreePlay => level != null && level.settings.freePlay;
        public bool Paused { get; set; }
        public ShotResult LastShot { get; private set; }
        public LevelResult LastLevelResult { get; private set; }
        /// <summary>Zones of the running level (5 in free play).</summary>
        public int Zones => level != null ? level.settings.zones : 5;

        /// <summary>
        /// Assist on: the target is shown during CUE and FLIGHT (glowing ground marker; the HUD adds
        /// the call-out and strip outline). Off: the player only has the striker's run-up and the
        /// ball; the marker still lights up green/red after the shot as feedback. Set by GameFlow.
        /// </summary>
        public bool Assist
        {
            get => assist;
            set
            {
                if (assist == value) return;
                assist = value;
                // apply mid-shot too: show or hide the cue marker right away
                if (machine != null && (machine.State == RoundState.Cue || machine.State == RoundState.Flight))
                {
                    if (assist) marker.Show(machine.CurrentTarget, machine.Settings.zones);
                    else marker.Hide();
                }
            }
        }

        /// <summary>Assist state when the current/last shot's CUE started (what gets logged).</summary>
        public bool AssistAtCue { get; private set; } = true;

        bool assist = true;

        public event Action<LevelConfig> LevelStarted;
        /// <summary>(shotIndex, target zone slot)</summary>
        public event Action<int, int> CueStarted;
        /// <summary>(shotIndex, target zone slot)</summary>
        public event Action<int, int> Kicked;
        public event Action<ShotResult> ShotResolved;
        public event Action<LevelResult> LevelCompleted;

        public void SetInput(IInputProvider input) => controls = input;

        public void StartLevel(LevelConfig config, IInputProvider input)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            controls = input ?? throw new ArgumentNullException(nameof(input));

            StopLevel();
            level = config;
            var s = config.settings.Clone();
            keeper.Zones = s.zones;
            keeper.Command(0);
            keeper.SnapToCommand();
            Paused = false;

            if (!s.freePlay)
            {
                machine = RoundStateMachine.CreateBalanced(s);
                machine.WaitingForRest += OnWaitingForRest;
                machine.CueStarted += OnCueStarted;
                machine.Kicked += OnKicked;
                machine.ShotResolved += OnShotResolved;
                machine.LevelCompleted += OnLevelCompleted;
            }

            Debug.Log(s.freePlay
                ? $"[Keeper244] Start {s.levelName} (free play), input {controls.SourceName}"
                : $"[Keeper244] Start {s.levelName}: {machine.ShotCount} shots, {s.zones} zones, cue {s.cueSeconds:0.##} s, flight {s.flightSeconds:0.##} s, input {controls.SourceName}");

            LevelStarted?.Invoke(config);
            machine?.Start();
        }

        /// <summary>Abandon the running level (if any) and reset the pitch.</summary>
        public void StopLevel()
        {
            if (machine != null)
            {
                machine.WaitingForRest -= OnWaitingForRest;
                machine.CueStarted -= OnCueStarted;
                machine.Kicked -= OnKicked;
                machine.ShotResolved -= OnShotResolved;
                machine.LevelCompleted -= OnLevelCompleted;
                machine = null;
            }
            level = null;
            LastShot = null;
            LastLevelResult = null;
            Paused = false;
            ball.ResetToSpot();
            striker.ResetPose();
            marker.Hide();
        }

        void OnDestroy() => StopLevel();

        void Update()
        {
            if (controls == null) return;

            // No signal → keeper back to centre (brief §4.7). Pausing/voiding the shot comes in P3.
            bool live = level != null && controls.HasSignal;
            int slot = live ? controls.Slot : 0;
            keeper.Command(slot);

            if (machine != null && !Paused)
            {
                var sample = new InputSample(slot, !live || controls.IsAtRest, controls.ActivationBiceps, controls.ActivationTriceps);
                machine.Tick(Time.deltaTime, sample);
            }
        }

        void OnWaitingForRest(int shotIndex)
        {
            ball.ResetToSpot();
            if (shotIndex == 0) striker.ResetPose(); else striker.ReturnHome();
            marker.Hide();
        }

        void OnCueStarted(int shotIndex, int target)
        {
            int zones = machine.Settings.zones;
            float zoneWidth = FieldGeometry.ZoneWidth(zones);
            float jitter = ((float)visualRng.NextDouble() * 2f - 1f) * jitterFraction * zoneWidth;
            float height = Mathf.Lerp(ballHeightRange.x, ballHeightRange.y, (float)visualRng.NextDouble());
            shotPoint = new Vector3(FieldGeometry.ZoneCentreX(target, zones) + jitter, height, keeper.transform.position.z);

            AssistAtCue = assist;
            if (assist) marker.Show(target, zones);
            striker.BeginRunUp(shotPoint, machine.Settings.cueSeconds);
            CueStarted?.Invoke(shotIndex, target);
        }

        void OnKicked(int shotIndex, int target)
        {
            striker.Kick();
            ball.Launch(shotPoint, machine.Settings.flightSeconds);
            Kicked?.Invoke(shotIndex, target);
        }

        void OnShotResolved(ShotResult r)
        {
            LastShot = r;
            ball.PlayOutcome(r.Saved);
            if (!assist) marker.Show(r.Target, r.Zones); // feedback only: where it went, after the fact
            marker.ShowResult(r.Saved);
            if (r.Saved) keeper.Celebrate();
            Debug.Log($"[Keeper244] Shot {r.ShotIndex + 1}/{r.ShotCount}: target {r.Target:+0;-0;0}, keeper {r.KeeperZoneSlot:+0;-0;0} " +
                      $"→ {(r.Saved ? $"SAVE +{r.Points}" : "GOAL")} | RT {(double.IsNaN(r.ReactionTime) ? "—" : $"{r.ReactionTime * 1000:0} ms")}, " +
                      $"cue {r.CueTime:0.00} s, kick {r.KickTime:0.00} s, arrival {r.ArrivalTime:0.00} s");
            ShotResolved?.Invoke(r);
        }

        void OnLevelCompleted(LevelResult result)
        {
            LastLevelResult = result;
            marker.Hide();
            Debug.Log($"[Keeper244] {result.LevelName} complete: {result.Saves}/{result.Shots} saves ({result.SaveFraction:P0}), score {result.Score} " +
                      $"→ {(result.Passed ? "PASSED" : "not passed")} (need {result.UnlockSaveFraction:P0}).");
            LevelCompleted?.Invoke(result);
        }
    }
}
