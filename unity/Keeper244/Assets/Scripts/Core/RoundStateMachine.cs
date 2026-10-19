using System;
using System.Collections.Generic;

namespace Keeper244.Core
{
    /// <summary>Brief §3.4. KICK and RESOLVE are instants, reported as events.</summary>
    public enum RoundState
    {
        Idle,
        WaitRest,
        Cue,
        Flight,
        Feedback,
        Complete,
    }

    public sealed class ShotResult
    {
        /// <summary>0-based.</summary>
        public int ShotIndex;
        public int ShotCount;
        /// <summary>Target zone slot.</summary>
        public int Target;
        /// <summary>Keeper's input slot5 at arrival.</summary>
        public int KeeperSlot5;
        /// <summary>Keeper's zone slot at arrival (sign(slot5) in 3-zone levels).</summary>
        public int KeeperZoneSlot;
        public bool Saved;
        /// <summary>Times on the state machine's own clock (seconds since Start).</summary>
        public double CueTime;
        public double KickTime;
        public double ArrivalTime;
        /// <summary>Times the player left rest during this shot's WAIT_REST (brief §3.9).</summary>
        public int FalseActivations;
    }

    public sealed class LevelResult
    {
        public string LevelName;
        public int Shots;
        public int Saves;
        public float UnlockSaveFraction;
        public double SaveFraction => Shots == 0 ? 0.0 : (double)Saves / Shots;
        public bool Passed => SaveFraction >= UnlockSaveFraction;
    }

    /// <summary>
    /// The penalty round loop, free of Unity so it is unit-tested directly:
    ///
    ///   WAIT_REST ─► CUE ─► (KICK) ─► FLIGHT ─► (RESOLVE) ─► FEEDBACK ─► next shot | COMPLETE
    ///
    /// Call Tick every frame with the frame time, the keeper's current input slot5 and whether
    /// the player is at rest. Timer overshoot carries into the next state, so CUE + FLIGHT last
    /// exactly T_cue + T_flight on this clock regardless of frame rate.
    /// </summary>
    public sealed class RoundStateMachine
    {
        readonly LevelSettings settings;
        readonly List<int> targets;
        readonly List<ShotResult> results = new List<ShotResult>();

        float stateTime;
        float restTime;
        bool prevAtRest = true;
        int falseActivations;
        double cueTime;
        double kickTime;

        public RoundState State { get; private set; } = RoundState.Idle;
        /// <summary>0-based index of the current shot.</summary>
        public int ShotIndex { get; private set; }
        public int ShotCount => targets.Count;
        public int Saves { get; private set; }
        /// <summary>Seconds since Start on this machine's clock.</summary>
        public double Clock { get; private set; }
        public LevelSettings Settings => settings;
        public IReadOnlyList<ShotResult> Results => results;

        /// <summary>Target zone slot of the current shot (valid in Cue, Flight, Feedback).</summary>
        public int CurrentTarget => ShotIndex < targets.Count ? targets[ShotIndex] : 0;
        public bool HasActiveShot => State == RoundState.Cue || State == RoundState.Flight || State == RoundState.Feedback;
        /// <summary>Seconds spent continuously at rest in WAIT_REST.</summary>
        public float RestTime => restTime;
        /// <summary>Seconds into the current timed state.</summary>
        public float StateTime => stateTime;

        /// <summary>(shotIndex, targetZoneSlot)</summary>
        public event Action<int, int> CueStarted;
        /// <summary>(shotIndex, targetZoneSlot)</summary>
        public event Action<int, int> Kicked;
        public event Action<ShotResult> ShotResolved;
        /// <summary>(shotIndex of the shot about to be cued)</summary>
        public event Action<int> WaitingForRest;
        public event Action<LevelResult> LevelCompleted;

        public RoundStateMachine(LevelSettings settings, IList<int> targetSequence)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            if (targetSequence == null || targetSequence.Count == 0)
                throw new ArgumentException("need at least one target", nameof(targetSequence));
            int half = (settings.zones - 1) / 2;
            foreach (int t in targetSequence)
                if (t < -half || t > half)
                    throw new ArgumentOutOfRangeException(nameof(targetSequence), t, $"target outside {settings.zones}-zone range");
            targets = new List<int>(targetSequence);
        }

        /// <summary>Convenience: balanced random order from the settings' seed.</summary>
        public static RoundStateMachine CreateBalanced(LevelSettings settings)
        {
            settings.Validate();
            var order = ShotSequence.Balanced(settings.zones, settings.shotsPerTarget, ShotSequence.MakeRng(settings.randomSeed));
            return new RoundStateMachine(settings, order);
        }

        public void Start()
        {
            results.Clear();
            Saves = 0;
            ShotIndex = 0;
            Clock = 0;
            EnterWaitRest();
        }

        public void Tick(float dt, int keeperSlot5, bool atRest)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            if (State == RoundState.Idle || State == RoundState.Complete) return;

            Clock += dt;

            switch (State)
            {
                case RoundState.WaitRest:
                    if (atRest)
                    {
                        restTime += dt;
                    }
                    else
                    {
                        if (prevAtRest) falseActivations++;
                        restTime = 0f;
                    }
                    prevAtRest = atRest;
                    if (atRest && restTime >= settings.restGateSeconds)
                        EnterCue();
                    break;

                case RoundState.Cue:
                    stateTime += dt;
                    if (stateTime >= settings.cueSeconds)
                    {
                        stateTime -= settings.cueSeconds;
                        kickTime = Clock - stateTime;
                        State = RoundState.Flight;
                        Kicked?.Invoke(ShotIndex, CurrentTarget);
                    }
                    break;

                case RoundState.Flight:
                    stateTime += dt;
                    if (stateTime >= settings.flightSeconds)
                    {
                        stateTime -= settings.flightSeconds;
                        Resolve(keeperSlot5);
                    }
                    break;

                case RoundState.Feedback:
                    stateTime += dt;
                    if (stateTime >= settings.feedbackSeconds)
                    {
                        stateTime = 0f;
                        ShotIndex++;
                        if (ShotIndex >= targets.Count)
                            Finish();
                        else
                            EnterWaitRest();
                    }
                    break;
            }
        }

        void EnterWaitRest()
        {
            State = RoundState.WaitRest;
            stateTime = 0f;
            restTime = 0f;
            prevAtRest = true;
            falseActivations = 0;
            WaitingForRest?.Invoke(ShotIndex);
        }

        void EnterCue()
        {
            State = RoundState.Cue;
            stateTime = 0f;
            cueTime = Clock;
            CueStarted?.Invoke(ShotIndex, CurrentTarget);
        }

        void Resolve(int keeperSlot5)
        {
            int zoneSlot = FieldGeometry.ToZoneSlot(keeperSlot5, settings.zones);
            bool saved = zoneSlot == CurrentTarget;
            if (saved) Saves++;

            var result = new ShotResult
            {
                ShotIndex = ShotIndex,
                ShotCount = targets.Count,
                Target = CurrentTarget,
                KeeperSlot5 = keeperSlot5,
                KeeperZoneSlot = zoneSlot,
                Saved = saved,
                CueTime = cueTime,
                KickTime = kickTime,
                ArrivalTime = kickTime + settings.flightSeconds,
                FalseActivations = falseActivations,
            };
            results.Add(result);
            State = RoundState.Feedback;
            ShotResolved?.Invoke(result);
        }

        void Finish()
        {
            State = RoundState.Complete;
            LevelCompleted?.Invoke(new LevelResult
            {
                LevelName = settings.levelName,
                Shots = results.Count,
                Saves = Saves,
                UnlockSaveFraction = settings.unlockSaveFraction,
            });
        }
    }
}
