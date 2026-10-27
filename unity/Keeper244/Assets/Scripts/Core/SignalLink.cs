using System;
using System.Collections.Generic;

namespace Keeper244.Core
{
    /// <summary>
    /// Bookkeeping for one stream of numbered packets (the EMG bridge's <c>seq</c>), free of Unity so
    /// it is unit-tested directly. Times are seconds on any monotonic clock.
    ///
    ///  - <see cref="Accept"/>: a packet counts only if its seq is newer than the last one accepted.
    ///    Late, duplicated or reordered packets are dropped (brief §4.7). A sender that restarts
    ///    (seq jumps back after the link went quiet, or by a huge amount) starts a new stream.
    ///  - <see cref="HasSignal"/>: the last accepted packet is younger than the timeout (250 ms).
    ///  - Diagnostics for the F1 panel: rate over the last second, packets lost (gaps in seq),
    ///    packets dropped (out of order), mean interval and jitter.
    /// </summary>
    public sealed class SignalLink
    {
        readonly double timeout;
        readonly long restartJump;
        readonly Queue<double> recent = new Queue<double>();

        public SignalLink(double timeoutSeconds = 0.25, long restartJump = 1000)
        {
            if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            timeout = timeoutSeconds;
            this.restartJump = Math.Max(1, restartJump);
            Reset();
        }

        public double TimeoutSeconds => timeout;
        /// <summary>Seq of the last accepted packet (-1 before the first).</summary>
        public long LastSeq { get; private set; }
        /// <summary>Arrival time of the last accepted packet (NaN before the first).</summary>
        public double LastTime { get; private set; }
        /// <summary>Every packet offered to <see cref="Accept"/>.</summary>
        public long Received { get; private set; }
        public long Accepted { get; private set; }
        /// <summary>Packets refused because their seq was not newer (out of order or repeated).</summary>
        public long Dropped { get; private set; }
        /// <summary>Packets that never arrived: the sum of the gaps in accepted seq numbers.</summary>
        public long Lost { get; private set; }
        /// <summary>Times the sender restarted its seq numbering.</summary>
        public int Restarts { get; private set; }
        /// <summary>Running mean of the time between accepted packets, seconds (NaN until two arrive).</summary>
        public double MeanInterval { get; private set; }
        /// <summary>Running mean absolute deviation of that interval, seconds.</summary>
        public double Jitter { get; private set; }

        public void Reset()
        {
            LastSeq = -1;
            LastTime = double.NaN;
            Received = Accepted = Dropped = Lost = 0;
            Restarts = 0;
            MeanInterval = double.NaN;
            Jitter = 0;
            recent.Clear();
        }

        /// <summary>Offer a packet that arrived at time <paramref name="t"/>. True = use it.</summary>
        public bool Accept(long seq, double t)
        {
            Received++;
            bool restart = false;
            if (Accepted > 0 && seq <= LastSeq)
            {
                bool quietGap = t - LastTime > timeout;
                bool hugeJump = LastSeq - seq >= restartJump;
                if (!quietGap && !hugeJump)
                {
                    Dropped++;
                    return false;
                }
                restart = true;
                Restarts++;
            }

            if (Accepted > 0 && !restart)
            {
                if (seq > LastSeq + 1) Lost += seq - LastSeq - 1;
                double dt = t - LastTime;
                if (double.IsNaN(MeanInterval))
                {
                    MeanInterval = dt;
                }
                else
                {
                    const double k = 0.05; // ~20-packet memory
                    Jitter += k * (Math.Abs(dt - MeanInterval) - Jitter);
                    MeanInterval += k * (dt - MeanInterval);
                }
            }

            LastSeq = seq;
            LastTime = t;
            Accepted++;
            recent.Enqueue(t);
            Trim(t);
            return true;
        }

        public bool HasSignal(double now) => Accepted > 0 && now - LastTime < timeout;

        /// <summary>Seconds since the last accepted packet (infinity before the first).</summary>
        public double Age(double now) => Accepted > 0 ? now - LastTime : double.PositiveInfinity;

        /// <summary>Accepted packets per second over the last second.</summary>
        public double Rate(double now)
        {
            Trim(now);
            return recent.Count;
        }

        void Trim(double now)
        {
            while (recent.Count > 0 && recent.Peek() <= now - 1.0) recent.Dequeue();
        }
    }
}
