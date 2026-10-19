namespace Keeper244.Core
{
    /// <summary>One frame of input as the round state machine sees it.</summary>
    public struct InputSample
    {
        /// <summary>Keeper slot at 5-zone resolution, {-2..+2}.</summary>
        public int Slot5;
        /// <summary>Player relaxed (EMG: both channels below θ_off).</summary>
        public bool AtRest;
        /// <summary>Normalised biceps activation (fraction of MVC).</summary>
        public float Biceps;
        /// <summary>Normalised triceps activation (fraction of MVC).</summary>
        public float Triceps;

        public InputSample(int slot5, bool atRest, float biceps = 0f, float triceps = 0f)
        {
            Slot5 = slot5;
            AtRest = atRest;
            Biceps = biceps;
            Triceps = triceps;
        }
    }

    /// <summary>
    /// Per-muscle thresholds from calibration (brief §4.4), in normalised units. The HUD draws
    /// θ_on and θ_far as lines on the activation bars. Keyboard mode uses the brief's worked-example
    /// values; EMG mode receives real ones in the "calib" UDP message (P3).
    /// </summary>
    public struct ChannelThresholds
    {
        public float OnBiceps, OffBiceps, FarBiceps;
        public float OnTriceps, OffTriceps, FarTriceps;

        /// <summary>Brief §4.4 worked example: θ_on 0.10, θ_off 0.07, θ_far 0.35.</summary>
        public static ChannelThresholds Placeholder => new ChannelThresholds
        {
            OnBiceps = 0.10f, OffBiceps = 0.07f, FarBiceps = 0.35f,
            OnTriceps = 0.10f, OffTriceps = 0.07f, FarTriceps = 0.35f,
        };
    }
}
