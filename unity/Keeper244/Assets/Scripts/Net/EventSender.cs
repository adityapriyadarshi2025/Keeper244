using Keeper244.Core;
using Keeper244.Game;
using UnityEngine;

namespace Keeper244.Net
{
    /// <summary>
    /// Sends game events to the Python bridge on port 5006 (docs/PROTOCOL.md). Python timestamps
    /// each one on arrival, so the raw EMG and the game share one clock (brief §5.3).
    ///
    ///   level_start · shot_cue · kick · arrival · level_end   — the round
    ///   shot_void                                             — a shot dropped by signal loss (P3)
    ///   slot_applied                                          — the keeper moved because of packet seq
    ///   cmd mark                                              — operator label (M key in game)
    ///
    /// Sends in keyboard mode too (nobody listening is fine), so Python can log keyboard sessions.
    /// </summary>
    public sealed class EventSender : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField] string host = Protocol.Host;
        [SerializeField] int port = Protocol.UnityToPythonPort;

        UdpDatagramSender sender;

        public long Sent => sender?.Sent ?? 0;
        public long Failed => sender?.Failed ?? 0;
        public string LastError => sender?.LastError;
        public string Target => $"{host}:{port}";
        public int Marks { get; private set; }

        void OnEnable()
        {
            sender = new UdpDatagramSender(host, port);
            if (game == null) return;
            game.LevelStarted += OnLevelStarted;
            game.CueStarted += OnCue;
            game.Kicked += OnKick;
            game.ShotResolved += OnArrival;
            game.LevelCompleted += OnLevelEnd;
            game.ShotVoided += OnVoid;
            game.SlotApplied += OnSlotApplied;
        }

        void OnDisable()
        {
            if (game != null)
            {
                game.LevelStarted -= OnLevelStarted;
                game.CueStarted -= OnCue;
                game.Kicked -= OnKick;
                game.ShotResolved -= OnArrival;
                game.LevelCompleted -= OnLevelEnd;
                game.ShotVoided -= OnVoid;
                game.SlotApplied -= OnSlotApplied;
            }
            sender?.Dispose();
            sender = null;
        }

        void Send(object message)
        {
            sender?.Send(Protocol.ToJson(message));
        }

        string LevelId => game.Level != null ? game.Level.settings.levelId : "";

        void OnLevelStarted(LevelConfig level) => Send(new LevelStartEvent
        {
            level = level.settings.levelId,
            zones = level.settings.zones,
            shots = game.Machine != null ? game.Machine.ShotCount : 0,
        });

        void OnCue(int shot, int target) => Send(new ShotCueEvent { shot = shot + 1, level = LevelId, zones = game.Zones, target = target });

        void OnKick(int shot, int target) => Send(new KickEvent { shot = shot + 1 });

        void OnArrival(ShotResult r) => Send(new ArrivalEvent
        {
            shot = r.ShotIndex + 1,
            target = r.Target,
            keeper_slot = r.KeeperZoneSlot,
            saved = r.Saved,
        });

        void OnLevelEnd(LevelResult r) => Send(new LevelEndEvent { level = r.LevelId, shots = r.Shots, saves = r.Saves });

        void OnVoid(int shot, int target) => Send(new ShotVoidEvent { shot = shot + 1, reason = Protocol.VoidNoSignal });

        void OnSlotApplied(long seq) => Send(new SlotAppliedEvent { seq = seq });

        /// <summary>Operator label for the current trial (brief §6), e.g. an artifact trial.</summary>
        public void Mark(string label)
        {
            Marks++;
            Send(new MarkCommand { label = string.IsNullOrEmpty(label) ? $"mark_{Marks}" : label });
        }
    }
}
