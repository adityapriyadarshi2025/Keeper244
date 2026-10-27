using System;

namespace Keeper244.Core
{
    /// <summary>
    /// One shot, with every per-shot metric of brief §3.9. Times are seconds on the round state
    /// machine's own clock (since the level started). NaN = not applicable / never happened.
    /// </summary>
    public sealed class ShotResult
    {
        /// <summary>0-based.</summary>
        public int ShotIndex;
        public int ShotCount;
        public int Zones;
        /// <summary>Target zone slot.</summary>
        public int Target;
        /// <summary>Keeper's input slot5 at arrival.</summary>
        public int KeeperSlot5;
        /// <summary>Keeper's zone slot at arrival (sign(slot5) in 3-zone levels).</summary>
        public int KeeperZoneSlot;
        public bool Saved;

        // ---- scoring (brief §3.6) ----
        public int Points;
        /// <summary>Saves in a row before this shot.</summary>
        public int StreakBefore;
        public double Multiplier;

        // ---- timing ----
        public double CueTime;
        public double KickTime;
        public double ArrivalTime;

        // ---- behaviour (brief §3.9) ----
        /// <summary>Cue onset → first frame in a non-centre slot on the correct side. NaN for centre shots or if never.</summary>
        public double ReactionTime = double.NaN;
        /// <summary>First move off centre went the wrong way (centre shots: any move off centre).</summary>
        public bool WrongDirection;
        /// <summary>Reached a slot further out than the target on the correct side (e.g. +2 for a +1 shot).</summary>
        public bool Overshoot;
        /// <summary>At arrival: correct side but not far enough (e.g. +1 for a +2 shot).</summary>
        public bool Undershoot;
        /// <summary>Continuous time in the target slot that ends at arrival (0 if not in it at arrival).</summary>
        public double TimeInTargetBeforeArrival;
        /// <summary>Times the player left rest during this shot's WAIT_REST.</summary>
        public int FalseActivations;
        /// <summary>Mean activations while holding the target slot (the final dwell). NaN if never held.</summary>
        public double MeanBiceps = double.NaN;
        public double MeanTriceps = double.NaN;
    }

    public sealed class LevelResult
    {
        public string LevelId;
        public string LevelName;
        public int Zones;
        public int Shots;
        public int Saves;
        public int Score;
        public int BestStreak;
        /// <summary>Shots voided by signal loss and replayed (not in Shots).</summary>
        public int Voided;
        public float UnlockSaveFraction;
        /// <summary>Mean over shots that have a reaction time. NaN if none.</summary>
        public double MeanReactionTime = double.NaN;
        /// <summary>Per target zone, index = zoneSlot + (zones-1)/2 (left to right).</summary>
        public int[] ZoneShots = Array.Empty<int>();
        public int[] ZoneSaves = Array.Empty<int>();

        public double SaveFraction => Shots == 0 ? 0.0 : (double)Saves / Shots;
        public bool Passed => SaveFraction >= UnlockSaveFraction;
    }
}
