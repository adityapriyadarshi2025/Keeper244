using System;
using UnityEngine;

namespace Keeper244.Net
{
    /// <summary>
    /// The frozen UDP contract (docs/PROTOCOL.md, examples in protocol/udp_examples.json).
    /// Field names are lower-case on purpose: JsonUtility maps them 1:1 to the JSON keys.
    /// The UDP receiver and sender that use these arrive in P3.
    /// </summary>
    public static class Protocol
    {
        public const string Host = "127.0.0.1";
        public const int PythonToUnityPort = 5005;
        public const int UnityToPythonPort = 5006;
        public const float SignalTimeoutSeconds = 0.25f;

        public const string TypeEmg = "emg";
        public const string TypeCalib = "calib";
        public const string TypeEvent = "event";
        public const string TypeCmd = "cmd";

        public const string StatusOk = "ok";
        public const string StatusNoData = "no_data";
        public const string StatusSaturated = "saturated";
        public const string StatusFlatline = "flatline";

        /// <summary>Read only the "type" key, to decide which class to parse the message into.</summary>
        public static string PeekType(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<MessageHeader>(json)?.type; }
            catch (ArgumentException) { return null; }
        }

        public static string ToJson(object message) => JsonUtility.ToJson(message);
    }

    [Serializable]
    public sealed class MessageHeader
    {
        public string type;
        public string name;
    }

    // ---------------- Python → Unity (port 5005) ----------------

    [Serializable]
    public sealed class EmgPacket
    {
        public string type = Protocol.TypeEmg;
        public long seq;
        public double t;
        public float ab;
        public float at;
        public int slot;
        public bool rest;
        public string status = Protocol.StatusOk;
    }

    [Serializable]
    public sealed class CalibPacket
    {
        public string type = Protocol.TypeCalib;
        /// <summary>[biceps, triceps]</summary>
        public float[] th_on = new float[2];
        public float[] th_off = new float[2];
        public float[] th_far = new float[2];
    }

    // ---------------- Unity → Python (port 5006) ----------------

    [Serializable]
    public sealed class LevelStartEvent
    {
        public string type = Protocol.TypeEvent;
        public string name = "level_start";
        public string level;
        public int zones;
        public int shots;
    }

    [Serializable]
    public sealed class ShotCueEvent
    {
        public string type = Protocol.TypeEvent;
        public string name = "shot_cue";
        /// <summary>1-based.</summary>
        public int shot;
        public string level;
        public int zones;
        public int target;
    }

    [Serializable]
    public sealed class KickEvent
    {
        public string type = Protocol.TypeEvent;
        public string name = "kick";
        public int shot;
    }

    [Serializable]
    public sealed class ArrivalEvent
    {
        public string type = Protocol.TypeEvent;
        public string name = "arrival";
        public int shot;
        public int target;
        public int keeper_slot;
        public bool saved;
    }

    [Serializable]
    public sealed class LevelEndEvent
    {
        public string type = Protocol.TypeEvent;
        public string name = "level_end";
        public string level;
        public int shots;
        public int saves;
    }

    [Serializable]
    public sealed class SlotAppliedEvent
    {
        public string type = Protocol.TypeEvent;
        public string name = "slot_applied";
        public long seq;
    }

    [Serializable]
    public sealed class MarkCommand
    {
        public string type = Protocol.TypeCmd;
        public string name = "mark";
        public string label;
    }
}
