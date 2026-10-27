using System;
using System.Net;
using Keeper244.Controls;
using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Net
{
    /// <summary>
    /// EMG input over UDP (brief §5.3, docs/PROTOCOL.md): listens on port 5005 for the bridge's
    /// <c>emg</c> packets (100 Hz) and <c>calib</c> messages, and serves them to the game as an
    /// <see cref="IInputProvider"/>, exactly like the keyboard.
    ///
    ///  - A background thread receives; this component parses on the main thread every frame
    ///    (it runs before GameManager, so the keeper uses this frame's packets).
    ///  - Packets whose seq is not newer than the last one are dropped (brief §4.7).
    ///  - HasSignal is false when no packet arrived for 250 ms: the game shows NO SIGNAL, the
    ///    keeper goes to centre, the round pauses and a shot in play is voided.
    ///  - It listens whenever it is enabled, so the menu can show "receiving" before EMG is chosen.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class EmgReceiver : MonoBehaviour, IInputProvider
    {
        [SerializeField] int port = Protocol.PythonToUnityPort;
        [Tooltip("Off: only this computer can send (no firewall prompt). On: accept a bridge on another PC.")]
        [SerializeField] bool acceptFromNetwork;

        readonly SignalLink link = new SignalLink(Protocol.SignalTimeoutSeconds);
        UdpDatagramListener listener;

        int slot;
        long slotSeq = -1;
        float biceps, triceps;
        bool rest = true;
        string status = Protocol.StatusOk;
        ChannelThresholds thresholds = ChannelThresholds.Placeholder;

        // ---------------- IInputProvider ----------------

        public int Slot => HasSignal ? slot : 0;
        public float ActivationBiceps => HasSignal ? biceps : 0f;
        public float ActivationTriceps => HasSignal ? triceps : 0f;
        public bool IsAtRest => !HasSignal || rest;
        public bool HasSignal => link.HasSignal(UdpDatagramListener.Now);
        public string SourceName => "EMG";
        public ChannelThresholds Thresholds => thresholds;
        public string Status => status;
        public long SlotSeq => slotSeq;

        // ---------------- diagnostics (menu, F1 panel) ----------------

        public int Port => port;
        public SignalLink Link => link;
        public bool IsListening => listener != null && listener.IsListening;
        public string ListenError => listener?.Error;
        /// <summary>True once a calib message arrived (thresholds are real, not placeholders).</summary>
        public bool Calibrated { get; private set; }
        /// <summary>Round trip the bridge last measured (emg out → slot_applied back), ms; NaN if never sent.</summary>
        public float BridgeRoundTripMs { get; private set; } = float.NaN;
        /// <summary>Datagrams that were not valid JSON messages.</summary>
        public long BadMessages { get; private set; }
        public double PacketRate => link.Rate(UdpDatagramListener.Now);
        public double PacketAgeSeconds => link.Age(UdpDatagramListener.Now);

        void OnEnable()
        {
            listener = new UdpDatagramListener(acceptFromNetwork ? IPAddress.Any : IPAddress.Loopback, port);
            if (listener.Start())
                Debug.Log($"[Keeper244] EMG receiver listening on {(acceptFromNetwork ? "all interfaces" : "127.0.0.1")}:{port}");
            else
                Debug.LogWarning($"[Keeper244] EMG receiver: {listener.Error}");
        }

        void OnDisable()
        {
            listener?.Dispose();
            listener = null;
        }

        void Update()
        {
            if (listener == null) return;
            while (listener.TryDequeue(out var d)) Handle(d.Text, d.Time);
        }

        /// <summary>Apply one datagram that arrived at time t (UdpDatagramListener.Now clock).</summary>
        public void Handle(string json, double t)
        {
            string type = Protocol.PeekType(json);
            switch (type)
            {
                case Protocol.TypeEmg: HandleEmg(json, t); break;
                case Protocol.TypeCalib: HandleCalib(json); break;
                case null: BadMessages++; break;
                default: break; // unknown types are ignored (forward compatible)
            }
        }

        void HandleEmg(string json, double t)
        {
            EmgPacket p;
            try { p = JsonUtility.FromJson<EmgPacket>(json); }
            catch (ArgumentException) { BadMessages++; return; }
            if (p == null || !link.Accept(p.seq, t)) return;

            int s = Mathf.Clamp(p.slot, -2, 2);
            if (s != slot || slotSeq < 0) slotSeq = p.seq; // the packet that set this slot
            slot = s;
            biceps = Mathf.Clamp(p.ab, 0f, 1.5f);
            triceps = Mathf.Clamp(p.at, 0f, 1.5f);
            rest = p.rest;
            status = string.IsNullOrEmpty(p.status) ? Protocol.StatusOk : p.status;

            if (json.Contains("\"rtt_ms\""))
            {
                try { BridgeRoundTripMs = JsonUtility.FromJson<EmgExtras>(json).rtt_ms; }
                catch (ArgumentException) { }
            }
        }

        void HandleCalib(string json)
        {
            CalibPacket c;
            try { c = JsonUtility.FromJson<CalibPacket>(json); }
            catch (ArgumentException) { BadMessages++; return; }
            if (c?.th_on == null || c.th_off == null || c.th_far == null ||
                c.th_on.Length < 2 || c.th_off.Length < 2 || c.th_far.Length < 2)
            {
                BadMessages++;
                return;
            }
            thresholds = new ChannelThresholds
            {
                OnBiceps = c.th_on[0], OffBiceps = c.th_off[0], FarBiceps = c.th_far[0],
                OnTriceps = c.th_on[1], OffTriceps = c.th_off[1], FarTriceps = c.th_far[1],
            };
            Calibrated = true;
        }
    }
}
