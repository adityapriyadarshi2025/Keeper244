using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Keeper244.Core;
using NUnit.Framework;

namespace Keeper244.Tests
{
    /// <summary>
    /// Scoring (brief §3.6), per-shot metrics (§3.9), level summary and the CSV format.
    /// dt = 0.125 s: rest gate = 4 ticks, cue 1.5 s = 12 ticks, flight 1.25 s = 10 ticks.
    /// </summary>
    public class ScoringAndMetricsTests
    {
        const float Dt = 0.125f;
        const int CueTicks = 12;
        const int FlightTicks = 10;

        static LevelSettings Settings(int zones) => new LevelSettings
        {
            levelId = "T", levelName = "Test", zones = zones,
            cueSeconds = 1.5f, flightSeconds = 1.25f, feedbackSeconds = 1.5f, restGateSeconds = 0.5f,
            shotsPerTarget = 1, unlockSaveFraction = 0.7f,
        };

        /// <summary>Plays one shot: rest gate, then one input per tick for CUE + FLIGHT (22 ticks), then feedback.</summary>
        static ShotResult PlayShot(RoundStateMachine m, Func<int, InputSample> inputAtTick)
        {
            ShotResult result = null;
            void Grab(ShotResult r) => result = r;
            m.ShotResolved += Grab;
            for (int i = 0; i < 4; i++) m.Tick(Dt, new InputSample(0, true));
            for (int i = 0; i < CueTicks + FlightTicks; i++) m.Tick(Dt, inputAtTick(i));
            for (int i = 0; i < 12; i++) m.Tick(Dt, new InputSample(0, true));
            m.ShotResolved -= Grab;
            Assert.IsNotNull(result, "shot did not resolve");
            return result;
        }

        static InputSample Hold(int slot5, float biceps = 0f, float triceps = 0f) => new InputSample(slot5, slot5 == 0, biceps, triceps);

        // ---------------- scoring ----------------

        [Test]
        public void Score_WorkedExampleFromTheBrief()
        {
            Assert.AreEqual(210, ScoreRules.Points(true, 2, 5, 4), "4 saves in a row, then a far save = 150 × 1.4");
        }

        [TestCase(true, 1, 5, 0, 100)]
        [TestCase(true, 0, 5, 0, 100)]
        [TestCase(true, -2, 5, 0, 150)]
        [TestCase(true, 1, 3, 0, 100, TestName = "3-zone sides are not 'far'")]
        [TestCase(true, 1, 5, 3, 130)]
        [TestCase(true, 1, 5, 10, 200, TestName = "multiplier reaches ×2")]
        [TestCase(true, -2, 5, 25, 300, TestName = "multiplier capped at ×2")]
        [TestCase(false, 2, 5, 9, 0, TestName = "goal scores 0")]
        public void Score_Points(bool saved, int target, int zones, int streak, int expected)
        {
            Assert.AreEqual(expected, ScoreRules.Points(saved, target, zones, streak));
        }

        [Test]
        public void Machine_TracksStreakAndScore()
        {
            var m = new RoundStateMachine(Settings(5), new[] { 1, 2, -1, 0 });
            m.Start();
            var r1 = PlayShot(m, i => Hold(1));   // save, streak 0 → 100
            var r2 = PlayShot(m, i => Hold(2));   // far save, streak 1 → 150 × 1.1 = 165
            var r3 = PlayShot(m, i => Hold(1));   // goal (target -1) → 0, streak resets
            var r4 = PlayShot(m, i => Hold(0));   // centre save, streak 0 → 100
            Assert.AreEqual(new[] { 100, 165, 0, 100 }, new[] { r1.Points, r2.Points, r3.Points, r4.Points });
            Assert.AreEqual(new[] { 0, 1, 2, 0 }, new[] { r1.StreakBefore, r2.StreakBefore, r3.StreakBefore, r4.StreakBefore });
            Assert.AreEqual(365, m.Score);
            Assert.AreEqual(2, m.BestStreak);
        }

        // ---------------- §3.9 metrics ----------------

        [Test]
        public void ReactionTime_FromCueToFirstCorrectSideFrame()
        {
            var m = new RoundStateMachine(Settings(3), new[] { 1 });
            m.Start();
            var r = PlayShot(m, i => Hold(i < 4 ? 0 : 1));   // moves on the 5th cue tick
            Assert.AreEqual(0.625, r.ReactionTime, 1e-9);
            Assert.IsFalse(r.WrongDirection);
            Assert.IsTrue(r.Saved);
        }

        [Test]
        public void WrongDirection_ThenCorrected()
        {
            var m = new RoundStateMachine(Settings(3), new[] { 1 });
            m.Start();
            var r = PlayShot(m, i => Hold(i < 2 ? 0 : i < 6 ? -1 : 1));
            Assert.IsTrue(r.WrongDirection);
            Assert.AreEqual(0.875, r.ReactionTime, 1e-9, "reaction time counts the first correct-side frame");
            Assert.IsTrue(r.Saved);
        }

        [Test]
        public void CentreShot_AnyMoveIsWrongDirection_NoReactionTime()
        {
            var m = new RoundStateMachine(Settings(3), new[] { 0 });
            m.Start();
            var r = PlayShot(m, i => Hold(i == 3 ? 1 : 0));
            Assert.IsTrue(r.WrongDirection);
            Assert.IsTrue(double.IsNaN(r.ReactionTime));
            Assert.IsTrue(r.Saved, "back at centre by arrival");
        }

        [Test]
        public void Overshoot_FarThenBackToNear()
        {
            var m = new RoundStateMachine(Settings(5), new[] { 1 });
            m.Start();
            var r = PlayShot(m, i => Hold(i < 3 ? 0 : i < 8 ? 2 : 1));
            Assert.IsTrue(r.Overshoot);
            Assert.IsFalse(r.Undershoot);
            Assert.IsTrue(r.Saved);
        }

        [Test]
        public void Undershoot_NearWhenFarWasNeeded()
        {
            var m = new RoundStateMachine(Settings(5), new[] { -2 });
            m.Start();
            var r = PlayShot(m, i => Hold(-1));
            Assert.IsTrue(r.Undershoot);
            Assert.IsFalse(r.Saved);
            Assert.AreEqual(0.0, r.TimeInTargetBeforeArrival);
            Assert.IsTrue(double.IsNaN(r.MeanTriceps));
        }

        [Test]
        public void TimeInTarget_AndMeanActivationsOverTheFinalHold()
        {
            var m = new RoundStateMachine(Settings(3), new[] { 1 });
            m.Start();
            // target side early, drop back to centre, then hold the target for the last 6 ticks
            var r = PlayShot(m, i =>
                i < 5 ? Hold(1, biceps: 0.9f) :
                i < 16 ? Hold(0) :
                Hold(1, biceps: 0.3f, triceps: 0.05f));
            Assert.AreEqual(0.75, r.TimeInTargetBeforeArrival, 1e-9);
            Assert.AreEqual(0.3, r.MeanBiceps, 1e-6);
            Assert.AreEqual(0.05, r.MeanTriceps, 1e-6);
        }

        [Test]
        public void LevelResult_PerZoneAndMeanReactionTime()
        {
            var m = new RoundStateMachine(Settings(3), new[] { 1, -1, 0 });
            LevelResult level = null;
            m.LevelCompleted += l => level = l;
            m.Start();
            PlayShot(m, i => Hold(i < 4 ? 0 : 1));    // save, RT 0.625
            PlayShot(m, i => Hold(i < 2 ? 0 : -1));   // save, RT 0.375
            PlayShot(m, i => Hold(1));                // goal on the centre shot
            Assert.IsNotNull(level);
            CollectionAssert.AreEqual(new[] { 1, 1, 1 }, level.ZoneShots);
            CollectionAssert.AreEqual(new[] { 1, 0, 1 }, level.ZoneSaves, "left, centre, right");
            Assert.AreEqual(0.5, level.MeanReactionTime, 1e-9);
            Assert.AreEqual(210, level.Score, "100 + 110 (streak 1) + 0");
            Assert.AreEqual("T", level.LevelId);
        }

        [Test]
        public void FreePlayLevels_HaveNoStateMachine()
        {
            var s = Settings(5);
            s.freePlay = true;
            Assert.Throws<ArgumentException>(() => new RoundStateMachine(s, new[] { 0 }));
        }

        // ---------------- CSV ----------------

        static ShotResult SampleResult() => new ShotResult
        {
            ShotIndex = 2, ShotCount = 9, Zones = 5, Target = -2, KeeperSlot5 = -2, KeeperZoneSlot = -2,
            Saved = true, Points = 165, StreakBefore = 1, Multiplier = 1.1,
            CueTime = 4.5, KickTime = 6.0, ArrivalTime = 7.25, ReactionTime = 0.625,
            WrongDirection = false, Overshoot = false, Undershoot = false,
            TimeInTargetBeforeArrival = 1.5, FalseActivations = 1, MeanBiceps = 0.02, MeanTriceps = 0.51,
        };

        [Test]
        public void Csv_RowMatchesHeader()
        {
            string row = ShotCsv.Row(SampleResult(), "P01", "Keyboard", "L2", "front", assist: false);
            Assert.AreEqual(ShotCsv.Columns.Length, row.Split(',').Length);
            Assert.AreEqual("P01,Keyboard,L2,5,3,-2,-2,-2,1,165,1,1.1,4.500,6.000,7.250,0.625,0,0,0,1.500,1,0.0200,0.5100,front,0", row);
        }

        [Test]
        public void Csv_CameraAndAssistAreTheLastColumns_EmptyWhenUnknown()
        {
            int n = ShotCsv.Columns.Length;
            Assert.AreEqual("camera", ShotCsv.Columns[n - 2]);
            Assert.AreEqual("assist", ShotCsv.Columns[n - 1]);
            string row = ShotCsv.Row(SampleResult(), "P01", "Keyboard", "L2");
            Assert.AreEqual(n, row.Split(',').Length);
            StringAssert.EndsWith(",0.5100,,", row);
            StringAssert.EndsWith(",behind,1", ShotCsv.Row(SampleResult(), "P01", "Keyboard", "L2", "behind", assist: true));
        }

        [Test]
        public void Csv_NaNIsEmpty_AndQuotesAreEscaped()
        {
            var r = SampleResult();
            r.ReactionTime = double.NaN;
            r.MeanBiceps = double.NaN;
            string row = ShotCsv.Row(r, "Pat, \"A\"", "Keyboard", "L2");
            StringAssert.StartsWith("\"Pat, \"\"A\"\"\",", row);
            StringAssert.Contains(",7.250,,0,", row);
        }

        [Test]
        public void Csv_UsesDecimalPointInEveryLocale()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string row = ShotCsv.Row(SampleResult(), "P01", "Keyboard", "L2");
                StringAssert.Contains("0.625", row);
                Assert.IsFalse(row.Contains("0,625"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = old;
            }
        }
    }
}
