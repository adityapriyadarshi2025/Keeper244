using System;

namespace Keeper244.Core
{
    /// <summary>
    /// Real football dimensions (metres) and the slot/zone geometry from brief §3.2.
    /// Plain C# (no UnityEngine) so it can be unit-tested anywhere.
    ///
    /// Coordinates: goal line on z = 0, pitch towards +z, penalty spot at z = +11.
    /// The keeper faces +z (towards the striker), so the keeper's RIGHT is +x.
    ///
    /// Two kinds of slot:
    ///  - "slot5": what the input sends, always in {-2,-1,0,+1,+2} (5-zone resolution).
    ///  - "zone slot": the level's own resolution. 5-zone levels use slot5 as is;
    ///    3-zone levels use sign(slot5) in {-1,0,+1}.
    /// </summary>
    public static class FieldGeometry
    {
        public const float GoalWidth = 7.32f;
        public const float GoalHeight = 2.44f;
        public const float PenaltyDistance = 11f;
        public const float BallDiameter = 0.22f;
        public const float KeeperHeight = 1.85f;

        /// <summary>Width of one zone: 7.32/3 = 2.44 m or 7.32/5 = 1.464 m.</summary>
        public static float ZoneWidth(int zones)
        {
            CheckZones(zones);
            return GoalWidth / zones;
        }

        /// <summary>Convert an input slot5 to the level's zone slot.</summary>
        public static int ToZoneSlot(int slot5, int zones)
        {
            CheckZones(zones);
            int clamped = Math.Max(-2, Math.Min(2, slot5));
            return zones == 3 ? Math.Sign(clamped) : clamped;
        }

        /// <summary>x of a zone's centre. 3-zone: {-2.44, 0, +2.44}; 5-zone: multiples of 1.464.</summary>
        public static float ZoneCentreX(int zoneSlot, int zones)
        {
            int half = (zones - 1) / 2;
            if (zoneSlot < -half || zoneSlot > half)
                throw new ArgumentOutOfRangeException(nameof(zoneSlot), zoneSlot, $"must be in [-{half}, {half}] for {zones} zones");
            return zoneSlot * ZoneWidth(zones);
        }

        /// <summary>Where the keeper should stand for an input slot5 in a level with this many zones.</summary>
        public static float KeeperX(int slot5, int zones) => ZoneCentreX(ToZoneSlot(slot5, zones), zones);

        /// <summary>All zone slots of a level, left to right: {-1,0,1} or {-2,-1,0,1,2}.</summary>
        public static int[] ZoneSlots(int zones)
        {
            CheckZones(zones);
            return zones == 3 ? new[] { -1, 0, 1 } : new[] { -2, -1, 0, 1, 2 };
        }

        public static void CheckZones(int zones)
        {
            if (zones != 3 && zones != 5)
                throw new ArgumentOutOfRangeException(nameof(zones), zones, "zones must be 3 or 5");
        }
    }

    /// <summary>Brief §3.4: save if the keeper's zone slot equals the target zone slot at ball arrival.
    /// A logical comparison, not a physics collision.</summary>
    public static class SaveRule
    {
        public static bool IsSave(int keeperSlot5, int targetZoneSlot, int zones)
            => FieldGeometry.ToZoneSlot(keeperSlot5, zones) == targetZoneSlot;
    }
}
