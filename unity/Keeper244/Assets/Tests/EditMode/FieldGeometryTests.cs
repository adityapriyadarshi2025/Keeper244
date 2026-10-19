using System;
using Keeper244.Core;
using NUnit.Framework;

namespace Keeper244.Tests
{
    public class FieldGeometryTests
    {
        const float Eps = 1e-4f;

        [Test]
        public void ZoneWidths_MatchTheBrief()
        {
            Assert.AreEqual(2.44f, FieldGeometry.ZoneWidth(3), Eps);
            Assert.AreEqual(1.464f, FieldGeometry.ZoneWidth(5), Eps);
        }

        [TestCase(-2, 5, -2.928f)]
        [TestCase(-1, 5, -1.464f)]
        [TestCase(0, 5, 0f)]
        [TestCase(1, 5, 1.464f)]
        [TestCase(2, 5, 2.928f)]
        [TestCase(-2, 3, -2.44f)]
        [TestCase(-1, 3, -2.44f)]
        [TestCase(0, 3, 0f)]
        [TestCase(1, 3, 2.44f)]
        [TestCase(2, 3, 2.44f)]
        public void KeeperX_ForEverySlot(int slot5, int zones, float expectedX)
        {
            Assert.AreEqual(expectedX, FieldGeometry.KeeperX(slot5, zones), Eps);
        }

        [Test]
        public void ToZoneSlot_ClampsAndTakesSignInThreeZoneLevels()
        {
            Assert.AreEqual(2, FieldGeometry.ToZoneSlot(7, 5));
            Assert.AreEqual(-2, FieldGeometry.ToZoneSlot(-9, 5));
            Assert.AreEqual(1, FieldGeometry.ToZoneSlot(2, 3));
            Assert.AreEqual(-1, FieldGeometry.ToZoneSlot(-1, 3));
            Assert.AreEqual(0, FieldGeometry.ToZoneSlot(0, 3));
        }

        [Test]
        public void OnlyThreeOrFiveZones()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FieldGeometry.ZoneWidth(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => FieldGeometry.ZoneCentreX(2, 3));
        }

        [TestCase(2, 1, 3, true, TestName = "3-zone: strong right still saves a right shot")]
        [TestCase(1, 1, 3, true)]
        [TestCase(0, 0, 3, true, TestName = "3-zone: relaxed saves a centre shot")]
        [TestCase(1, 0, 3, false, TestName = "3-zone: flexing loses a centre shot")]
        [TestCase(-1, 1, 3, false)]
        [TestCase(1, 2, 5, false, TestName = "5-zone: near does not save a far shot")]
        [TestCase(-2, -2, 5, true)]
        [TestCase(0, 0, 5, true)]
        public void SaveRule_ComparesZoneSlots(int keeperSlot5, int target, int zones, bool expected)
        {
            Assert.AreEqual(expected, SaveRule.IsSave(keeperSlot5, target, zones));
        }
    }
}
