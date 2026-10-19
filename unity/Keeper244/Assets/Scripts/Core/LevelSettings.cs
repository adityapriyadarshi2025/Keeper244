using System;

namespace Keeper244.Core
{
    /// <summary>
    /// Every tunable of one level (brief §3.5). Lives inside a LevelConfig asset, so timings are
    /// data, never hard-coded. Plain [Serializable] class: Unity shows it in the Inspector, and
    /// tests can build one without Unity.
    /// </summary>
    [Serializable]
    public sealed class LevelSettings
    {
        /// <summary>Short id used in logs and UDP events, e.g. "L1".</summary>
        public string levelId = "L1";

        public string levelName = "L1 Three Zones";

        /// <summary>3 or 5.</summary>
        public int zones = 3;

        /// <summary>L0 Free Play: no shots, the keeper just follows the muscles (practice, demos, calibration checks).</summary>
        public bool freePlay = false;

        /// <summary>CUE: target telegraphed, player starts flexing (T_cue).</summary>
        public float cueSeconds = 1.5f;

        /// <summary>FLIGHT: kick to arrival (T_flight).</summary>
        public float flightSeconds = 1.2f;

        /// <summary>FEEDBACK: save/goal shown before the next WAIT_REST.</summary>
        public float feedbackSeconds = 1.5f;

        /// <summary>WAIT_REST: player must be at rest this long, continuously, before the next cue.</summary>
        public float restGateSeconds = 0.5f;

        /// <summary>Shots per target zone. Total shots = zones * shotsPerTarget (balanced).</summary>
        public int shotsPerTarget = 3;

        /// <summary>Save fraction needed to unlock the next level.</summary>
        public float unlockSaveFraction = 0.7f;

        /// <summary>Seed for the shot order. 0 = different order every run.</summary>
        public int randomSeed = 0;

        public int TotalShots => zones * shotsPerTarget;

        public void Validate()
        {
            FieldGeometry.CheckZones(zones);
            if (cueSeconds <= 0f) throw new ArgumentException("cueSeconds must be > 0");
            if (flightSeconds <= 0f) throw new ArgumentException("flightSeconds must be > 0");
            if (feedbackSeconds < 0f) throw new ArgumentException("feedbackSeconds must be >= 0");
            if (restGateSeconds < 0f) throw new ArgumentException("restGateSeconds must be >= 0");
            if (shotsPerTarget < 1) throw new ArgumentException("shotsPerTarget must be >= 1");
            if (unlockSaveFraction < 0f || unlockSaveFraction > 1f) throw new ArgumentException("unlockSaveFraction must be in [0, 1]");
        }

        public LevelSettings Clone() => (LevelSettings)MemberwiseClone();
    }
}
