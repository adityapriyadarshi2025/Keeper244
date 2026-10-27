using System.Text;
using Keeper244.Core;
using Keeper244.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Keeper244.Game
{
    /// <summary>
    /// F1 debug panel (brief §3.7): frame rate, round state and timers, raw input values, and the
    /// UDP link: packet rate, age, lost / out-of-order packets, jitter, the bridge's round trip
    /// (emg sent → slot_applied back, measured in Python) and events sent.
    /// </summary>
    public sealed class DebugStatusOverlay : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField] EmgReceiver emg;
        [SerializeField] EventSender events;
        [SerializeField] bool visible;

        readonly StringBuilder sb = new StringBuilder();
        float fps;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) visible = !visible;
            if (Time.unscaledDeltaTime > 0f) fps = Mathf.Lerp(fps, 1f / Time.unscaledDeltaTime, 0.05f);
        }

        void OnGUI()
        {
            if (!visible || game == null) return;
            GUI.depth = 5;

            var m = game.Machine;
            var c = game.Controls;
            sb.Clear();
            sb.AppendLine("<b>DEBUG (F1)</b>");
            sb.AppendLine($"fps            {fps:0}");
            sb.AppendLine($"level          {(game.Level != null ? game.Level.settings.levelId : "—")}  zones {game.Zones}");
            if (m != null)
            {
                sb.AppendLine($"state          {m.State}  t={m.StateTime:0.00} s");
                sb.AppendLine($"clock          {m.Clock:0.00} s");
                sb.AppendLine($"rest timer     {m.RestTime:0.00} s");
                sb.AppendLine($"target         {(m.HasActiveShot ? Ui.Signed(m.CurrentTarget) : "—")}");
            }
            if (c != null)
            {
                sb.AppendLine($"input          {c.SourceName}  signal {(c.HasSignal ? "OK" : "LOST")}");
                sb.AppendLine($"slot5          {Ui.Signed(c.Slot)}   at rest {c.IsAtRest}");
                sb.AppendLine($"a_b / a_t      {c.ActivationBiceps:0.000} / {c.ActivationTriceps:0.000}");
            }
            sb.AppendLine($"keeper zone    {Ui.Signed(game.Keeper.CommandedZoneSlot)}");
            if (c != null && c.SourceName != "Keyboard") sb.AppendLine($"status         {c.Status}");

            if (emg != null)
            {
                var link = emg.Link;
                sb.AppendLine("<b>UDP IN :" + emg.Port + "</b>" + (emg.IsListening ? "" : "  " + (emg.ListenError ?? "not listening")));
                double age = emg.PacketAgeSeconds;
                sb.AppendLine($"packet rate    {emg.PacketRate:0} Hz   age {(double.IsInfinity(age) ? "—" : $"{age * 1000:0} ms")}");
                sb.AppendLine($"interval       {(double.IsNaN(link.MeanInterval) ? "—" : $"{link.MeanInterval * 1000:0.0} ms ± {link.Jitter * 1000:0.0}")}");
                sb.AppendLine($"seq            {link.LastSeq}  lost {link.Lost}  out-of-order {link.Dropped}");
                sb.AppendLine($"round trip     {(float.IsNaN(emg.BridgeRoundTripMs) ? "— (bridge sends rtt_ms)" : $"{emg.BridgeRoundTripMs:0.0} ms (bridge)")}");
                sb.AppendLine($"calibrated     {(emg.Calibrated ? "yes" : "no (placeholder lines)")}   bad msgs {emg.BadMessages}");
            }
            if (events != null)
                sb.Append($"events out     {events.Target}  sent {events.Sent}  failed {events.Failed}  marks {events.Marks}");

            float w = 520, h = 520;
            float x = Ui.Width - w - 20, y = 230;
            Ui.Fill(x, y, w, h, new Color(0f, 0f, 0f, 0.8f));
            var style = Ui.LabelStyle(17, Ui.Text);
            GUI.Label(Ui.R(x + 14, y + 10, w - 28, h - 20), sb.ToString(), style);
        }
    }
}
