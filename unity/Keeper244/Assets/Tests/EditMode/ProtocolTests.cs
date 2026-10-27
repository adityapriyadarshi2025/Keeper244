using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Keeper244.Net;
using NUnit.Framework;
using UnityEngine;

namespace Keeper244.Tests
{
    /// <summary>
    /// Unity side of the frozen UDP contract: every example in protocol/udp_examples.json must
    /// round-trip through our [Serializable] classes with exactly the same keys and values.
    /// python/tests/test_protocol.py checks the same file from the Python side.
    /// </summary>
    public class ProtocolTests
    {
        static readonly Regex KeyPattern = new Regex("\"([A-Za-z_]+)\"\\s*:");

        static string ExamplesPath()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "protocol", "udp_examples.json");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>Each example sits on its own line in the file, starting with {"type".</summary>
        static List<string> ExampleMessages()
        {
            string path = ExamplesPath();
            if (path == null) Assert.Inconclusive("protocol/udp_examples.json not found above " + Directory.GetCurrentDirectory());
            return File.ReadAllLines(path)
                .Select(l => l.Trim().TrimEnd(','))
                .Where(l => l.StartsWith("{\"type\""))
                .ToList();
        }

        static object Parse(string json)
        {
            var header = JsonUtility.FromJson<MessageHeader>(json);
            switch (header.type)
            {
                case Protocol.TypeEmg: return JsonUtility.FromJson<EmgPacket>(json);
                case Protocol.TypeCalib: return JsonUtility.FromJson<CalibPacket>(json);
                case Protocol.TypeCmd: return JsonUtility.FromJson<MarkCommand>(json);
            }
            switch (header.name)
            {
                case "level_start": return JsonUtility.FromJson<LevelStartEvent>(json);
                case "shot_cue": return JsonUtility.FromJson<ShotCueEvent>(json);
                case "kick": return JsonUtility.FromJson<KickEvent>(json);
                case "arrival": return JsonUtility.FromJson<ArrivalEvent>(json);
                case "level_end": return JsonUtility.FromJson<LevelEndEvent>(json);
                case "slot_applied": return JsonUtility.FromJson<SlotAppliedEvent>(json);
                case "shot_void": return JsonUtility.FromJson<ShotVoidEvent>(json);
            }
            throw new ArgumentException("unknown message: " + json);
        }

        static HashSet<string> Keys(string json) =>
            new HashSet<string>(KeyPattern.Matches(json).Cast<Match>().Select(m => m.Groups[1].Value));

        [Test]
        public void ExamplesFileHasEveryMessageKind()
        {
            var msgs = ExampleMessages();
            Assert.AreEqual(12, msgs.Count, "update this test when the contract gains a message");
        }

        [Test]
        public void EveryExample_RoundTripsWithTheSameKeys()
        {
            foreach (string json in ExampleMessages())
            {
                object msg = Parse(json);
                string back = JsonUtility.ToJson(msg);
                CollectionAssert.AreEquivalent(Keys(json), Keys(back), $"keys differ for {json}\nC# wrote {back}");
            }
        }

        [Test]
        public void EmgExample_Values()
        {
            string json = ExampleMessages().First(l => l.Contains("\"seq\": 10234") && l.Contains("\"emg\""));
            var p = JsonUtility.FromJson<EmgPacket>(json);
            Assert.AreEqual(10234L, p.seq);
            Assert.AreEqual(512.34, p.t, 1e-9);
            Assert.AreEqual(0.286f, p.ab, 1e-6f);
            Assert.AreEqual(0.041f, p.at, 1e-6f);
            Assert.AreEqual(1, p.slot);
            Assert.IsFalse(p.rest);
            Assert.AreEqual(Protocol.StatusOk, p.status);
            Assert.AreEqual(Protocol.TypeEmg, Protocol.PeekType(json));
        }

        [Test]
        public void CalibExample_Values()
        {
            string json = ExampleMessages().First(l => l.Contains("\"calib\""));
            var c = JsonUtility.FromJson<CalibPacket>(json);
            CollectionAssert.AreEqual(new[] { 0.10f, 0.12f }, c.th_on);
            CollectionAssert.AreEqual(new[] { 0.07f, 0.084f }, c.th_off);
            CollectionAssert.AreEqual(new[] { 0.35f, 0.33f }, c.th_far);
        }

        [Test]
        public void PeekType_ToleratesGarbage()
        {
            Assert.IsNull(Protocol.PeekType(null));
            Assert.IsNull(Protocol.PeekType(""));
            Assert.IsNull(Protocol.PeekType("not json"));
        }

        [Test]
        public void ShotVoidExample_Values()
        {
            string json = ExampleMessages().First(l => l.Contains("\"shot_void\""));
            var e = JsonUtility.FromJson<ShotVoidEvent>(json);
            Assert.AreEqual(3, e.shot);
            Assert.AreEqual(Protocol.VoidNoSignal, e.reason);
        }

        [Test]
        public void OptionalRoundTrip_IsReadFromAnEmgPacket()
        {
            const string json = "{\"type\": \"emg\", \"seq\": 5, \"t\": 1.0, \"ab\": 0.1, \"at\": 0.0, \"slot\": 1, \"rest\": false, \"status\": \"ok\", \"rtt_ms\": 23.5}";
            Assert.AreEqual(23.5f, JsonUtility.FromJson<EmgExtras>(json).rtt_ms, 1e-4f);
            Assert.AreEqual(5L, JsonUtility.FromJson<EmgPacket>(json).seq, "extra fields don't disturb the packet");
        }

        [Test]
        public void PortsMatchTheSpec()
        {
            Assert.AreEqual(5005, Protocol.PythonToUnityPort);
            Assert.AreEqual(5006, Protocol.UnityToPythonPort);
            Assert.AreEqual(0.25f, Protocol.SignalTimeoutSeconds);
        }
    }
}
