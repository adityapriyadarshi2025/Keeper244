using System;
using System.Globalization;
using System.Text;

namespace Keeper244.Core
{
    /// <summary>
    /// shots.csv format: one row per shot with every §3.9 field. Numbers use the invariant culture
    /// (decimal point, never comma), times in seconds with 3 decimals, booleans as 0/1, and
    /// "not applicable" (NaN) as an empty cell, so pandas reads it as NaN.
    /// </summary>
    public static class ShotCsv
    {
        public static readonly string[] Columns =
        {
            "participant", "input", "level", "zones", "shot_id", "target_slot", "keeper_slot_at_arrival",
            "keeper_slot5_at_arrival", "saved", "points", "streak_before", "multiplier",
            "cue_time", "kick_time", "arrival_time", "reaction_time",
            "wrong_direction", "overshoot", "undershoot", "time_in_target_before_arrival",
            "false_activations_in_wait_rest", "mean_a_b", "mean_a_t",
        };

        public static string Header => string.Join(",", Columns);

        public static string Row(ShotResult r, string participant, string input, string levelId)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(256);
            void Add(string v) { if (sb.Length > 0) sb.Append(','); sb.Append(v); }

            Add(Escape(participant));
            Add(Escape(input));
            Add(Escape(levelId));
            Add(r.Zones.ToString(c));
            Add((r.ShotIndex + 1).ToString(c));
            Add(r.Target.ToString(c));
            Add(r.KeeperZoneSlot.ToString(c));
            Add(r.KeeperSlot5.ToString(c));
            Add(Bool(r.Saved));
            Add(r.Points.ToString(c));
            Add(r.StreakBefore.ToString(c));
            Add(r.Multiplier.ToString("0.0#", c));
            Add(Num(r.CueTime));
            Add(Num(r.KickTime));
            Add(Num(r.ArrivalTime));
            Add(Num(r.ReactionTime));
            Add(Bool(r.WrongDirection));
            Add(Bool(r.Overshoot));
            Add(Bool(r.Undershoot));
            Add(Num(r.TimeInTargetBeforeArrival));
            Add(r.FalseActivations.ToString(c));
            Add(Num(r.MeanBiceps, "0.0000"));
            Add(Num(r.MeanTriceps, "0.0000"));
            return sb.ToString();
        }

        static string Bool(bool b) => b ? "1" : "0";

        static string Num(double v, string format = "0.000") =>
            double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString(format, CultureInfo.InvariantCulture);

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
