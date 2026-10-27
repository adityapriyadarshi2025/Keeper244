using Keeper244.Core;
using NUnit.Framework;

namespace Keeper244.Tests
{
    /// <summary>Brief §4.7: seq ordering, the 250 ms timeout, sender restarts and the F1 statistics.</summary>
    public class SignalLinkTests
    {
        [Test]
        public void NoSignal_BeforeTheFirstPacket()
        {
            var link = new SignalLink(0.25);
            Assert.IsFalse(link.HasSignal(0.0));
            Assert.AreEqual(-1, link.LastSeq);
            Assert.IsTrue(double.IsPositiveInfinity(link.Age(1.0)));
        }

        [Test]
        public void Signal_LastsLessThanTheTimeout()
        {
            var link = new SignalLink(0.25);
            Assert.IsTrue(link.Accept(1, 10.0));
            Assert.IsTrue(link.HasSignal(10.0));
            Assert.IsTrue(link.HasSignal(10.249));
            Assert.IsFalse(link.HasSignal(10.25));
            Assert.AreEqual(0.1, link.Age(10.1), 1e-12);
        }

        [Test]
        public void OutOfOrderAndRepeatedPackets_AreDropped()
        {
            var link = new SignalLink(0.25);
            Assert.IsTrue(link.Accept(10, 1.00));
            Assert.IsTrue(link.Accept(12, 1.01));
            Assert.IsFalse(link.Accept(11, 1.02), "older than the last seen");
            Assert.IsFalse(link.Accept(12, 1.03), "repeat");
            Assert.IsTrue(link.Accept(13, 1.04));
            Assert.AreEqual(13, link.LastSeq);
            Assert.AreEqual(2, link.Dropped);
            Assert.AreEqual(3, link.Accepted);
            Assert.AreEqual(5, link.Received);
        }

        [Test]
        public void GapsInSeq_CountAsLost()
        {
            var link = new SignalLink(0.25);
            link.Accept(1, 0.00);
            link.Accept(2, 0.01);
            link.Accept(5, 0.02); // 3 and 4 never came
            link.Accept(6, 0.03);
            Assert.AreEqual(2, link.Lost);
        }

        [Test]
        public void SenderRestart_AfterTheLinkWentQuiet_IsAccepted()
        {
            var link = new SignalLink(0.25);
            link.Accept(500, 1.0);
            Assert.IsFalse(link.Accept(3, 1.1), "within the timeout a lower seq is just late");
            Assert.IsTrue(link.Accept(0, 1.5), "after 250 ms of silence a lower seq is a new stream");
            Assert.IsTrue(link.Accept(1, 1.51));
            Assert.AreEqual(1, link.Restarts);
            Assert.AreEqual(0, link.Lost, "a restart is not a gap");
        }

        [Test]
        public void SenderRestart_WithAHugeJumpBack_IsAcceptedImmediately()
        {
            var link = new SignalLink(0.25, restartJump: 1000);
            link.Accept(50_000, 1.0);
            Assert.IsTrue(link.Accept(0, 1.01));
            Assert.AreEqual(1, link.Restarts);
        }

        [Test]
        public void Rate_CountsPacketsInTheLastSecond()
        {
            var link = new SignalLink(0.25);
            for (int i = 0; i < 300; i++) link.Accept(i, i * 0.01); // 100 Hz for 3 s
            Assert.AreEqual(100, link.Rate(2.99), 1);
            Assert.AreEqual(0, link.Rate(10.0));
        }

        [Test]
        public void Interval_AndJitter_TrackASteadyStream()
        {
            var link = new SignalLink(0.25);
            for (int i = 0; i < 200; i++) link.Accept(i, i * 0.01 + (i % 2 == 0 ? 0.0 : 0.002));
            Assert.AreEqual(0.010, link.MeanInterval, 0.001);
            Assert.That(link.Jitter, Is.GreaterThan(0.001).And.LessThan(0.003));
        }

        [Test]
        public void Reset_ForgetsEverything()
        {
            var link = new SignalLink(0.25);
            link.Accept(7, 1.0);
            link.Reset();
            Assert.IsFalse(link.HasSignal(1.0));
            Assert.IsTrue(link.Accept(1, 1.0));
            Assert.AreEqual(0, link.Restarts);
        }
    }
}
