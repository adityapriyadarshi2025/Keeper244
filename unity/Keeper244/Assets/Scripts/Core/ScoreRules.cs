using System;

namespace Keeper244.Core
{
    /// <summary>
    /// Brief §3.6 [PROPOSED]:
    ///   save in a near or centre slot  +100
    ///   save in a far slot (5-zone ±2) +150
    ///   × (1 + 0.1 · streak), capped at ×2, where streak = saves in a row BEFORE this shot.
    /// Worked: 4 saves in a row, then a far save → 150 × 1.4 = 210.
    /// </summary>
    public static class ScoreRules
    {
        public const int NearPoints = 100;
        public const int FarPoints = 150;
        public const double StreakStep = 0.1;
        public const double MaxMultiplier = 2.0;

        public static bool IsFar(int targetZoneSlot, int zones) => zones == 5 && Math.Abs(targetZoneSlot) == 2;

        public static int BasePoints(int targetZoneSlot, int zones) => IsFar(targetZoneSlot, zones) ? FarPoints : NearPoints;

        public static double Multiplier(int streakBefore) => Math.Min(MaxMultiplier, 1.0 + StreakStep * Math.Max(0, streakBefore));

        /// <summary>Points for one shot; 0 for a goal.</summary>
        public static int Points(bool saved, int targetZoneSlot, int zones, int streakBefore)
        {
            if (!saved) return 0;
            return (int)Math.Round(BasePoints(targetZoneSlot, zones) * Multiplier(streakBefore), MidpointRounding.AwayFromZero);
        }
    }
}
