using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Glowing strip on the ground in front of the goal that telegraphs the target zone during CUE,
    /// then turns green (save) or red (goal) during FEEDBACK.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class ZoneMarker : MonoBehaviour
    {
        [SerializeField] Color cueColor = new Color(1f, 0.85f, 0.1f);
        [SerializeField] Color saveColor = new Color(0.2f, 0.9f, 0.3f);
        [SerializeField] Color goalColor = new Color(0.95f, 0.2f, 0.2f);
        [Tooltip("Gap left at each side of the strip, metres.")]
        [SerializeField] float sideGap = 0.08f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        Renderer rend;
        MaterialPropertyBlock block;

        void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
        }

        public void Show(int zoneSlot, int zones)
        {
            var p = transform.position;
            p.x = FieldGeometry.ZoneCentreX(zoneSlot, zones);
            transform.position = p;

            var s = transform.localScale;
            s.x = Mathf.Max(0.1f, FieldGeometry.ZoneWidth(zones) - 2f * sideGap);
            transform.localScale = s;

            SetColor(cueColor);
            rend.enabled = true;
        }

        public void ShowResult(bool saved) => SetColor(saved ? saveColor : goalColor);

        public void Hide()
        {
            if (rend != null) rend.enabled = false;
        }

        void SetColor(Color c)
        {
            rend.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c); // URP shaders
            block.SetColor(ColorId, c);     // built-in fallback
            rend.SetPropertyBlock(block);
        }
    }
}
