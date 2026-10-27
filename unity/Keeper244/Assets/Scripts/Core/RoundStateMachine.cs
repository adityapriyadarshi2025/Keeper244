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

    /// <summary>
    /// The penalty round loop, free of Unity so it is unit-tested directly:
    ///
    ///   WAIT_REST ─► CUE ─► (KICK) ─► FLIGHT ─► (RESOLVE) ─► FEEDBACK ─► next shot | COMPLETE
    ///
    /// Call Tick every frame with the frame time and the current input. Timer overshoot carries
    /// into the next state, so CUE + FLIGHT last exactly T_cue + T_flight on this clock regardless
    /// of frame rate. It also scores each shot (brief §3.6) and measures the §3.9 metrics.
    ///
    /// Signal loss (brief §4.7): the caller stops ticking (the round pauses) and calls
    /// <see cref="VoidCurrentShot"/>. A shot in CUE or FLIGHT is then dropped without a result and
    /// the same shot is played again after the rest gate, so the level keeps its balanced targets.
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

        // per-shot behaviour, measured during CUE + FLIGHT
        double reactionTime;
        int firstMoveSign;
        bool wrongDirection;
        bool overshoot;
        double dwell;
        double dwellBiceps;
        double dwellTriceps;

        public RoundState State { get; private set; } = RoundState.Idle;
        /// <summary>0-based index of the current shot.</summary>
        public int ShotIndex { get; private set; }
        public int ShotCount => targets.Count;
        public int Saves { get; private set; }
        public int Score { get; private set; }
        /// <summary>Saves in a row so far (resets on a goal).</summary>
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        /// <summary>Shots abandoned because the signal dropped (each was replayed).</summary>
        public int Voided { get; private set; }
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
        /// <summary>(shotIndex, targetZoneSlot) of a shot dropped by <see cref="VoidCurrentShot"/>.</summary>
        public event Action<int, int> ShotVoided;

        public RoundStateMachine(LevelSettings settings, IList<int> targetSequence)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            if (settings.freePlay) throw new ArgumentException("free-play levels have no shots");
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
            Score = 0;
            Streak = 0;
            BestStreak = 0;
            Voided = 0;
            ShotIndex = 0;
            Clock = 0;
            EnterWaitRest();
        }

        /// <summary>
        /// Abandon the shot in CUE or FLIGHT: no result, no score, streak untouched. The machine goes
        /// back to WAIT_REST for the same shot index (same target). False if there was no shot in play.
        /// </summary>
        public bool VoidCurrentShot()
        {
            if (State != RoundState.Cue && State != RoundState.Flight) return false;
            Voided++;
            ShotVoided?.Invoke(ShotIndex, CurrentTarget);
            EnterWaitRest();
            return true;
        }

        /// <summary>Shorthand without activations (keeps older call sites and tests simple).</summary>
        public void Tick(float dt, int keeperSlot5, bool atRest) => Tick(dt, new InputSample(keeperSlot5, atRest));

        public void Tick(float dt, InputSample input)
        {
            if (dt < 0f) throw new ArgumentOutOfRangeException(nameof(dt));
            if (State == RoundState.Idle || State == RoundState.Complete) return;

            Clock += dt;

            switch (State)
            {
                case RoundState.WaitRest:
                    if (input.AtRest)
                    {
                        restTime += dt;
                    }
                    else
                    {
                        if (prevAtRest) falseActivations++;
                        restTime = 0f;
                    }
                    prevAtRest = input.AtRest;
                    if (input.AtRest && restTime >= settings.restGateSeconds)
                        EnterCue();
                    break;

                case RoundState.Cue:
                    Observe(dt, input);
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
                    Observe(dt, input);
                    stateTime += dt;
                    if (stateTime >= settings.flightSeconds)
                    {
                        stateTime -= settings.flightSeconds;
                        Resolve(input);
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

        /// <summary>Behaviour metrics for the current frame (brief §3.9).</summary>
        void Observe(float dt, InputSample input)
        {
            int target = CurrentTarget;
            int z = FieldGeometry.ToZoneSlot(input.Slot5, settings.zones);
            int side = Math.Sign(z);

            if (z != 0 && firstMoveSign == 0)
            {
                firstMoveSign = side;
                wrongDirection = target == 0 || side != Math.Sign(target);
            }
            if (target != 0 && side == Math.Sign(target))
            {
                if (double.IsNaN(reactionTime)) reactionTime = Clock - cueTime;
                if (Math.Abs(z) > Math.Abs(target)) overshoot = true;
            }

            if (z == target)
            {
                dwell += dt;
                dwellBiceps += input.Biceps * dt;
                dwellTriceps += input.Triceps * dt;
            }
            else
            {
                dwell = dwellBiceps = dwellTriceps = 0.0;
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
            reactionTime = double.NaN;
            firstMoveSign = 0;
            wrongDirection = false;
            overshoot = false;
            dwell = dwellBiceps = dwellTriceps = 0.0;
            CueStarted?.Invoke(ShotIndex, CurrentTarget);
        }

        void Resolve(InputSample input)
        {
            int target = CurrentTarget;
            int zoneSlot = FieldGeometry.ToZoneSlot(input.Slot5, settings.zones);
            bool saved = zoneSlot == target;

            int streakBefore = Streak;
            int points = ScoreRules.Points(saved, target, settings.zones, streakBefore);
            if (saved)
            {
                Saves++;
                Streak++;
                BestStreak = Math.Max(BestStreak, Streak);
            }
            else
            {
                Streak = 0;
            }
            Score += points;

            bool held = saved && dwell > 0.0;
            var result = new ShotResult
            {
                ShotIndex = ShotIndex,
                ShotCount = targets.Count,
                Zones = settings.zones,
                Target = target,
                KeeperSlot5 = input.Slot5,
                KeeperZoneSlot = zoneSlot,
                Saved = saved,
                Points = points,
                StreakBefore = streakBefore,
                Multiplier = ScoreRules.Multiplier(streakBefore),
                CueTime = cueTime,
                KickTime = kickTime,
                ArrivalTime = kickTime + settings.flightSeconds,
                ReactionTime = reactionTime,
                WrongDirection = wrongDirection,
                Overshoot = overshoot,
                Undershoot = target != 0 && Math.Sign(zoneSlot) == Math.Sign(target) && Math.Abs(zoneSlot) < Math.Abs(target),
                TimeInTargetBeforeArrival = saved ? dwell : 0.0,
                FalseActivations = falseActivations,
                MeanBiceps = held ? dwellBiceps / dwell : double.NaN,
                MeanTriceps = held ? dwellTriceps / dwell : double.NaN,
            };
            results.Add(result);
            State = RoundState.Feedback;
            ShotResolved?.Invoke(result);
        }

        void Finish()
        {
            State = RoundState.Complete;
            int zones = settings.zones;
            int half = (zones - 1) / 2;
            var zoneShots = new int[zones];
            var zoneSaves = new int[zones];
            double rtSum = 0;
            int rtCount = 0;
            foreach (var r in results)
            {
                zoneShots[r.Target + half]++;
                if (r.Saved) zoneSaves[r.Target + half]++;
                if (!double.IsNaN(r.ReactionTime)) { rtSum += r.ReactionTime; rtCount++; }
            }

            LevelCompleted?.Invoke(new LevelResult
            {
                LevelId = settings.levelId,
                LevelName = settings.levelName,
                Zones = zones,
                Shots = results.Count,
                Saves = Saves,
                Score = Score,
                BestStreak = BestStreak,
                Voided = Voided,
                UnlockSaveFraction = settings.unlockSaveFraction,
                MeanReactionTime = rtCount > 0 ? rtSum / rtCount : double.NaN,
                ZoneShots = zoneShots,
                ZoneSaves = zoneSaves,
            });
        }
    }
}
