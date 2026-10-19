using System;
using System.Collections.Generic;
using Keeper244.Core;
using NUnit.Framework;

namespace Keeper244.Tests
{
    /// <summary>
    /// The round loop timing and the save rule. dt = 0.125 s is exact in binary floating point,
    /// so tick counts below are exact: rest gate 0.5 s = 4 ticks, cue 1.5 s = 12, flight 1.25 s = 10.
    /// </summary>
    public class RoundStateMachineTests
    {
        const float Dt = 0.125f;

        static LevelSettings Settings(int zones = 3) => new LevelSettings
        {
            levelId = "T",
            levelName = "Test",
            zones = zones,
            cueSeconds = 1.5f,
            flightSeconds = 1.25f,
            feedbackSeconds = 1.5f,
            restGateSeconds = 0.5f,
            shotsPerTarget = 1,
            unlockSaveFraction = 0.7f,
        };

        static void Ticks(RoundStateMachine m, int n, int slot = 0, bool rest = true)
        {
            for (int i = 0; i < n; i++) m.Tick(Dt, slot, rest);
        }

        [Test]
        public void IdleUntilStarted_ThenWaitsForRest()
        {
            var m = new RoundStateMachine(Settings(), new[] { 1 });
            Assert.AreEqual(RoundState.Idle, m.State);
            m.Tick(1f, 0, true);
            Assert.AreEqual(RoundState.Idle, m.State);
            m.Start();
            Assert.AreEqual(RoundState.WaitRest, m.State);
        }

        [Test]
        public void RestGate_NeedsHalfASecondContinuouslyAtRest()
        {
            var m = new RoundStateMachine(Settings(), new[] { 1 });
            m.Start();
            Ticks(m, 3);                       // 0.375 s at rest
            Assert.AreEqual(RoundState.WaitRest, m.State);
            Ticks(m, 1, slot: 1, rest: false); // player flexes → timer resets
            Ticks(m, 3);
            Assert.AreEqual(RoundState.WaitRest, m.State);
            Ticks(m, 1);                       // 4th tick at rest = 0.5 s
            Assert.AreEqual(RoundState.Cue, m.State);
        }

        [Test]
        public void CueThenKickThenArrival_OnTheExactTicks()
        {
            var m = new RoundStateMachine(Settings(), new[] { 1 });
            int cues = 0, kicks = 0, resolved = 0;
            m.CueStarted += (i, t) => cues++;
            m.Kicked += (i, t) => kicks++;
            m.ShotResolved += r => resolved++;
            m.Start();

            Ticks(m, 4);
            Assert.AreEqual((RoundState.Cue, 1, 0), (m.State, cues, kicks));
            Ticks(m, 11);
            Assert.AreEqual(RoundState.Cue, m.State, "kicked too early");
            Ticks(m, 1);
            Assert.AreEqual((RoundState.Flight, 1), (m.State, kicks));
            Ticks(m, 9, slot: 1, rest: false);
            Assert.AreEqual((RoundState.Flight, 0), (m.State, resolved), "arrived too early");
            Ticks(m, 1, slot: 1, rest: false);
            Assert.AreEqual((RoundState.Feedback, 1), (m.State, resolved));
        }

        [Test]
        public void Result_UsesTheKeeperSlotAtArrival()
        {
            var m = new RoundStateMachine(Settings(), new[] { 1 });
            ShotResult result = null;
            m.ShotResolved += r => result = r;
            m.Start();
            Ticks(m, 4 + 12);                         // to the kick
            Ticks(m, 9, slot: -1, rest: false);       // wrong side during most of the flight
            Ticks(m, 1, slot: 2, rest: false);        // right side at arrival (strong flex, 3-zone → +1)
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Saved);
            Assert.AreEqual(2, result.KeeperSlot5);
            Assert.AreEqual(1, result.KeeperZoneSlot);
        }

        [TestCase(0, true, TestName = "Centre shot saved by staying relaxed")]
        [TestCase(1, false, TestName = "Centre shot conceded by flexing")]
        public void CentreShot(int keeperSlot, bool saved)
        {
            var m = new RoundStateMachine(Settings(), new[] { 0 });
            ShotResult result = null;
            m.ShotResolved += r => result = r;
            m.Start();
            Ticks(m, 4 + 12);
            Ticks(m, 10, slot: keeperSlot, rest: keeperSlot == 0);
            Assert.AreEqual(saved, result.Saved);
        }

        [Test]
        public void TimesOnTheMachineClock()
        {
            var m = new RoundStateMachine(Settings(), new[] { 1 });
            ShotResult result = null;
            m.ShotResolved += r => result = r;
            m.Start();
            Ticks(m, 4 + 12 + 10);
            Assert.AreEqual(0.5, result.CueTime, 1e-9);
            Assert.AreEqual(2.0, result.KickTime, 1e-9);
            Assert.AreEqual(3.25, result.ArrivalTime, 1e-9);
        }

        [Test]
        public void LongFrame_CarriesOvershootIntoTheNextState()
        {
            var m = new RoundStateMachine(Settings(), new[] { 1 });
            ShotResult result = null;
            m.ShotResolved += r => result = r;
            m.Start();
            Ticks(m, 4);                 // cue starts at 0.5 s
            m.Tick(2.0f, 0, true);       // one 2 s frame: kick happened at 2.0 s, 0.5 s into flight
            Assert.AreEqual(RoundState.Flight, m.State);
            Assert.AreEqual(0.5f, m.StateTime, 1e-6);
            m.Tick(0.75f, 1, false);     // arrival exactly at 3.25 s
            Assert.AreEqual(2.0, result.KickTime, 1e-9);
            Assert.AreEqual(3.25, result.ArrivalTime, 1e-9);
        }

        [Test]
        public void FalseActivations_CountEachTimeThePlayerLeavesRest()
        {
            var m = new RoundStateMachine(Settings(), new[] { 1 });
            ShotResult result = null;
            m.ShotResolved += r => result = r;
            m.Start();
            Ticks(m, 1);
            Ticks(m, 2, slot: 1, rest: false);  // one activation lasting two ticks = 1
            Ticks(m, 1);
            Ticks(m, 1, slot: -1, rest: false); // second activation = 2
            Ticks(m, 4);                        // rest gate passes
            Ticks(m, 12 + 10, slot: 1, rest: false);
            Assert.AreEqual(2, result.FalseActivations);
        }

        [Test]
        public void LevelCompletes_AfterTheLastShot_WithPassFail()
        {
            var targets = new[] { 1, -1, 0 };
            var m = new RoundStateMachine(Settings(), targets);
            LevelResult level = null;
            m.LevelCompleted += r => level = r;
            m.Start();

            // Save the first two shots, concede the third.
            var keeperFor = new Dictionary<int, int> { { 0, 1 }, { 1, -1 }, { 2, 1 } };
            for (int shot = 0; shot < 3; shot++)
            {
                Ticks(m, 4);
                Ticks(m, 12 + 10, slot: keeperFor[shot], rest: false);
                Ticks(m, 12);       // feedback 1.5 s
            }

            Assert.AreEqual(RoundState.Complete, m.State);
            Assert.IsNotNull(level);
            Assert.AreEqual((3, 2), (level.Shots, level.Saves));
            Assert.IsFalse(level.Passed, "2/3 = 67 % is below the 70 % unlock");

            m.Tick(5f, 0, true);
            Assert.AreEqual(RoundState.Complete, m.State, "ticks after completion do nothing");
        }

        [Test]
        public void RejectsTargetsOutsideTheLevel()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RoundStateMachine(Settings(3), new[] { 2 }));
            Assert.DoesNotThrow(() => new RoundStateMachine(Settings(5), new[] { 2, -2 }));
        }

        [Test]
        public void CreateBalanced_MakesZonesTimesShotsPerTarget()
        {
            var s = Settings(5);
            s.shotsPerTarget = 3;
            s.randomSeed = 123;
            var m = RoundStateMachine.CreateBalanced(s);
            Assert.AreEqual(15, m.ShotCount);
        }
    }
}
