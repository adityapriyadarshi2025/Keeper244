using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Renders the keeper at the commanded slot. The slot comes from the input (Python decides it
    /// for EMG); this script only moves the body there at a capped speed, so there are no teleports
    /// (brief §4.7). Saves are decided logically from the commanded slot, not from this position.
    /// </summary>
    public sealed class KeeperController : MonoBehaviour
    {
        [Tooltip("Seconds to cross one 5-zone slot (1.464 m). 0.15 s → 9.8 m/s.")]
        [SerializeField] float adjacentSlotSeconds = 0.15f;

        [Tooltip("Body lean at the far slot, degrees. Purely visual.")]
        [SerializeField] float maxLeanDegrees = 12f;

        [Tooltip("Height of the little jump after a save, metres.")]
        [SerializeField] float celebrateHeight = 0.25f;
        [SerializeField] float celebrateSeconds = 0.35f;

        int zones = 3;
        float hopT = -1f;

        /// <summary>Current input slot5 in {-2..+2}.</summary>
        public int CommandedSlot5 { get; private set; }

        /// <summary>Commanded slot at the level's resolution.</summary>
        public int CommandedZoneSlot => FieldGeometry.ToZoneSlot(CommandedSlot5, zones);

        public int Zones
        {
            get => zones;
            set { FieldGeometry.CheckZones(value); zones = value; }
        }

        float Speed => FieldGeometry.ZoneWidth(5) / Mathf.Max(0.01f, adjacentSlotSeconds);

        public void Command(int slot5) => CommandedSlot5 = Mathf.Clamp(slot5, -2, 2);

        /// <summary>Small jump after a save. Visual only.</summary>
        public void Celebrate() => hopT = 0f;

        public void SnapToCommand()
        {
            var p = transform.position;
            p.x = FieldGeometry.KeeperX(CommandedSlot5, zones);
            transform.position = p;
        }

        void Update()
        {
            var p = transform.position;
            float targetX = FieldGeometry.KeeperX(CommandedSlot5, zones);
            p.x = Mathf.MoveTowards(p.x, targetX, Speed * Time.deltaTime);
            p.y = 0f;
            if (hopT >= 0f)
            {
                hopT += Time.deltaTime;
                float u = Mathf.Clamp01(hopT / Mathf.Max(0.01f, celebrateSeconds));
                p.y = Mathf.Sin(Mathf.PI * u) * celebrateHeight;
                if (u >= 1f) hopT = -1f;
            }
            transform.position = p;

            // Lean towards the side the keeper is on (negative z-rotation tips the top towards +x).
            float farX = FieldGeometry.ZoneWidth(5) * 2f;
            float lean = -maxLeanDegrees * Mathf.Clamp(p.x / farX, -1f, 1f);
            transform.rotation = Quaternion.Euler(0f, 0f, lean);
        }
    }
}
