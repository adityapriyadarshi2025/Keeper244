using System;
using System.Linq;
using Keeper244.Core;
using NUnit.Framework;

namespace Keeper244.Tests
{
    public class ShotSequenceTests
    {
        [TestCase(3, 3)]
        [TestCase(5, 3)]
        [TestCase(5, 8)]
        public void EveryZoneAppearsEquallyOften(int zones, int perTarget)
        {
            var seq = ShotSequence.Balanced(zones, perTarget, new Random(42));
            Assert.AreEqual(zones * perTarget, seq.Count);
            foreach (int zone in FieldGeometry.ZoneSlots(zones))
                Assert.AreEqual(perTarget, seq.Count(t => t == zone), $"zone {zone}");
        }

        [Test]
        public void SameSeedSameOrder_DifferentSeedsDiffer()
        {
            var a = ShotSequence.Balanced(5, 3, new Random(7));
            var b = ShotSequence.Balanced(5, 3, new Random(7));
            CollectionAssert.AreEqual(a, b);

            bool anyDifferent = Enumerable.Range(1, 20)
                .Any(s => !ShotSequence.Balanced(5, 3, new Random(s)).SequenceEqual(a));
            Assert.IsTrue(anyDifferent, "shuffle never changes the order");
        }
    }
}
