using System.Text;
using Keeper244.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Keeper244.Game
{
    /// <summary>
    /// F1 debug panel (brief §3.7): frame rate, round state and timers, raw input values.
    /// Latency and packet rate come from the UDP receiver in P3.
    /// </summary>
    public sealed class DebugStatusOverlay : MonoBehaviour
    {
        [SerializeField] GameManager game;
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
            sb.AppendLine("latency        — (P3)");
            sb.Append("packet rate    — (P3)");

            float w = 420, h = 360;
            float x = Ui.Width - w - 20, y = 230;
            Ui.Fill(x, y, w, h, new Color(0f, 0f, 0f, 0.8f));
            var style = Ui.LabelStyle(17, Ui.Text);
            GUI.Label(Ui.R(x + 14, y + 10, w - 28, h - 20), sb.ToString(), style);
        }
    }
}
