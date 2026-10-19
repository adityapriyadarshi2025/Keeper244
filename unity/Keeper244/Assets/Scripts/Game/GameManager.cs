using Keeper244.Controls;
using Keeper244.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Keeper244.Game
{
    /// <summary>
    /// Wires the round state machine (Core) to the scene: reads the input every frame, moves the
    /// keeper, and turns state-machine events into ball, striker and marker visuals.
    ///
    /// Keys: R = restart the level.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [Header("Level")]
        [SerializeField] LevelConfig level;

        [Header("Input (any component implementing IInputProvider)")]
        [SerializeField] MonoBehaviour inputSource;

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
        readonly System.Random visualRng = new System.Random();
        Vector3 shotPoint;

        public RoundStateMachine Machine => machine;
        public IInputProvider Controls => controls;
        public LevelConfig Level => level;
        public KeeperController Keeper => keeper;
        public ShotResult LastShot { get; private set; }
        public LevelResult LastLevelResult { get; private set; }

        void Awake()
        {
            controls = inputSource as IInputProvider;
            if (controls == null)
                Debug.LogError("[Keeper244] GameManager: 'Input Source' must be a component that implements IInputProvider (e.g. KeyboardInput).", this);
            if (level == null)
                Debug.LogError("[Keeper244] GameManager: no LevelConfig assigned.", this);
        }

        void Start() => StartLevel();

        public void StartLevel()
        {
            if (level == null || controls == null) return;

            Unsubscribe();
            machine = RoundStateMachine.CreateBalanced(level.settings.Clone());
            machine.WaitingForRest += OnWaitingForRest;
            machine.CueStarted += OnCueStarted;
            machine.Kicked += OnKicked;
            machine.ShotResolved += OnShotResolved;
            machine.LevelCompleted += OnLevelCompleted;

            keeper.Zones = machine.Settings.zones;
            LastShot = null;
            LastLevelResult = null;

            Debug.Log($"[Keeper244] Start {machine.Settings.levelName}: {machine.ShotCount} shots, " +
                      $"{machine.Settings.zones} zones, cue {machine.Settings.cueSeconds:0.##} s, flight {machine.Settings.flightSeconds:0.##} s, input {controls.SourceName}");
            machine.Start();
        }

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (machine == null) return;
            machine.WaitingForRest -= OnWaitingForRest;
            machine.CueStarted -= OnCueStarted;
            machine.Kicked -= OnKicked;
            machine.ShotResolved -= OnShotResolved;
            machine.LevelCompleted -= OnLevelCompleted;
        }

        void Update()
        {
            if (machine == null || controls == null) return;

            // No signal → keeper back to centre (brief §4.7). Pausing/voiding the shot comes in P3.
            int slot = controls.HasSignal ? controls.Slot : 0;
            keeper.Command(slot);
            machine.Tick(Time.deltaTime, slot, controls.IsAtRest);

            var kb = Keyboard.current;
            if (kb != null && kb.rKey.wasPressedThisFrame) StartLevel();
        }

        void OnWaitingForRest(int shotIndex)
        {
            ball.ResetToSpot();
            striker.ResetPose();
            marker.Hide();
        }

        void OnCueStarted(int shotIndex, int target)
        {
            int zones = machine.Settings.zones;
            float zoneWidth = FieldGeometry.ZoneWidth(zones);
            float jitter = ((float)visualRng.NextDouble() * 2f - 1f) * jitterFraction * zoneWidth;
            float height = Mathf.Lerp(ballHeightRange.x, ballHeightRange.y, (float)visualRng.NextDouble());
            shotPoint = new Vector3(FieldGeometry.ZoneCentreX(target, zones) + jitter, height, keeper.transform.position.z);

            marker.Show(target, zones);
            striker.AimAt(shotPoint);
        }

        void OnKicked(int shotIndex, int target)
        {
            striker.Kick();
            ball.Launch(shotPoint, machine.Settings.flightSeconds);
        }

        void OnShotResolved(ShotResult r)
        {
            LastShot = r;
            ball.PlayOutcome(r.Saved);
            marker.ShowResult(r.Saved);
            Debug.Log($"[Keeper244] Shot {r.ShotIndex + 1}/{r.ShotCount}: target {r.Target:+0;-0;0}, keeper {r.KeeperZoneSlot:+0;-0;0} " +
                      $"→ {(r.Saved ? "SAVE" : "GOAL")} | cue {r.CueTime:0.00} s, kick {r.KickTime:0.00} s, arrival {r.ArrivalTime:0.00} s, " +
                      $"false activations {r.FalseActivations}");
        }

        void OnLevelCompleted(LevelResult result)
        {
            LastLevelResult = result;
            marker.Hide();
            Debug.Log($"[Keeper244] {result.LevelName} complete: {result.Saves}/{result.Shots} saves ({result.SaveFraction:P0}) " +
                      $"→ {(result.Passed ? "PASSED" : "not passed")} (need {result.UnlockSaveFraction:P0}). Press R to replay.");
        }
    }
}
