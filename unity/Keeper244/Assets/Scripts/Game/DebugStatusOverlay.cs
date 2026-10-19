using System.Text;
using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Temporary text overlay for P1 play-testing: state, shot count, saves, slots and prompts.
    /// The real HUD (activation bars, slot strip, score) replaces it in P2.
    /// </summary>
    public sealed class DebugStatusOverlay : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField] int fontSize = 20;

        GUIStyle style;
        readonly StringBuilder sb = new StringBuilder();

        void OnGUI()
        {
            if (game == null || game.Machine == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = fontSize,
                    alignment = TextAnchor.UpperLeft,
                    richText = true,
                    wordWrap = false,
                    padding = new RectOffset(12, 12, 10, 10),
                };
            }

            var m = game.Machine;
            var c = game.Controls;
            sb.Clear();
            sb.AppendLine($"<b>{m.Settings.levelName}</b>   input: {c?.SourceName ?? "none"}");
            sb.AppendLine($"Shot {Mathf.Min(m.ShotIndex + 1, m.ShotCount)}/{m.ShotCount}   Saves {m.Saves}   State {m.State}");
            sb.AppendLine($"Keeper slot {game.Keeper.CommandedZoneSlot:+0;-0;0}" +
                          (m.HasActiveShot ? $"   Target {m.CurrentTarget:+0;-0;0}" : ""));

            switch (m.State)
            {
                case RoundState.WaitRest:
                    sb.AppendLine($"<color=#9cf>Relax: release all keys ({m.RestTime:0.0}/{m.Settings.restGateSeconds:0.0} s)</color>");
                    break;
                case RoundState.Cue:
                    sb.AppendLine($"<color=#fd4>Shot coming {Arrow(m.CurrentTarget)}</color>");
                    break;
                case RoundState.Flight:
                    sb.AppendLine("<color=#fd4>Ball in flight!</color>");
                    break;
                case RoundState.Feedback:
                    if (game.LastShot != null)
                        sb.AppendLine(game.LastShot.Saved ? "<color=#5e5>SAVE!</color>" : "<color=#f55>GOAL</color>");
                    break;
                case RoundState.Complete:
                    var r = game.LastLevelResult;
                    if (r != null)
                        sb.AppendLine($"<b>Done: {r.Saves}/{r.Shots} ({r.SaveFraction:P0}) " +
                                      $"{(r.Passed ? "<color=#5e5>PASSED</color>" : "<color=#f55>not passed</color>")}</b>   R = replay");
                    break;
            }
            sb.Append("<size=14>A/D or ←/→ = near   Shift = far   R = restart</size>");

            var content = new GUIContent(sb.ToString());
            Vector2 size = style.CalcSize(content);
            GUI.Box(new Rect(12, 12, size.x, size.y), content, style);
        }

        static string Arrow(int target)
        {
            if (target == 0) return "to the CENTRE (stay relaxed)";
            string side = target > 0 ? "RIGHT →" : "← LEFT";
            return System.Math.Abs(target) == 2 ? $"FAR {side}" : side;
        }
    }
}
