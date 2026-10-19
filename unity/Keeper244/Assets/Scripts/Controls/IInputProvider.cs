namespace Keeper244.Controls
{
    /// <summary>
    /// What the game needs from any control source. Keyboard (P1) and EMG over UDP (P3) both
    /// implement it, so the rest of the game never knows which one is plugged in.
    /// </summary>
    public interface IInputProvider
    {
        /// <summary>Keeper slot at 5-zone resolution, in {-2,-1,0,+1,+2}. +1/+2 = right (biceps), -1/-2 = left (triceps).
        /// 3-zone levels use sign(Slot).</summary>
        int Slot { get; }

        /// <summary>Normalised biceps activation (fraction of MVC, 0..1.5). For the HUD.</summary>
        float ActivationBiceps { get; }

        /// <summary>Normalised triceps activation (fraction of MVC, 0..1.5). For the HUD.</summary>
        float ActivationTriceps { get; }

        /// <summary>True when the player is relaxed (EMG: both channels below θ_off). Drives the rest gate.</summary>
        bool IsAtRest { get; }

        /// <summary>False when the source has gone quiet (EMG: no packet for 250 ms). Keyboard: always true.</summary>
        bool HasSignal { get; }

        /// <summary>Short name for the HUD and logs, e.g. "Keyboard" or "EMG".</summary>
        string SourceName { get; }
    }
}
