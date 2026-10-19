using System;
using System.Collections.Generic;

namespace Keeper244.Core
{
    /// <summary>
    /// Balanced shuffle (brief §3.5): every zone appears equally often, in random order,
    /// so the target-vs-achieved confusion matrix is balanced.
    /// </summary>
    public static class ShotSequence
    {
        public static List<int> Balanced(int zones, int shotsPerTarget, Random rng)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (shotsPerTarget < 1) throw new ArgumentOutOfRangeException(nameof(shotsPerTarget));

            var targets = new List<int>(zones * shotsPerTarget);
            foreach (int zone in FieldGeometry.ZoneSlots(zones))
                for (int i = 0; i < shotsPerTarget; i++)
                    targets.Add(zone);

            // Fisher–Yates
            for (int i = targets.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (targets[i], targets[j]) = (targets[j], targets[i]);
            }
            return targets;
        }

        /// <summary>Seed 0 means "random each run".</summary>
        public static Random MakeRng(int seed) => seed == 0 ? new Random() : new Random(seed);
    }
}
