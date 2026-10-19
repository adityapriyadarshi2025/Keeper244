using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Main camera: a slow orbit around the stadium behind the menu, and the fixed behind-the-goal
    /// play view during levels (brief §3.8: the play camera never follows the keeper). Adds a short
    /// shake on a goal and a zoom punch on a save.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraDirector : MonoBehaviour
    {
        [Header("Menu orbit")]
        [SerializeField] Vector3 orbitCentre = new Vector3(0f, 2f, 14f);
        [SerializeField] float orbitRadius = 30f;
        [SerializeField] float orbitHeight = 11f;
        [SerializeField] float orbitDegreesPerSecond = 4f;

        [Header("Transitions")]
        [SerializeField] float blendSeconds = 1.4f;

        Camera cam;
        Vector3 playPos;
        Quaternion playRot;
        float baseFov;
        bool menu = true;
        float orbitAngle = 200f;
        float blend = 1f;               // 0 = menu pose, 1 = play pose
        float shakeAmp, shakeTime, shakeDur;
        float punch, punchT = -1f;

        void Awake()
        {
            cam = GetComponent<Camera>();
            playPos = transform.position;
            playRot = transform.rotation;
            baseFov = cam.fieldOfView;
            blend = 0f;
        }

        /// <summary>true = menu orbit, false = play view.</summary>
        public void SetMenu(bool showMenu) => menu = showMenu;

        public void Shake(float amplitude, float seconds)
        {
            shakeAmp = amplitude;
            shakeDur = seconds;
            shakeTime = 0f;
        }

        /// <summary>Brief zoom-in (fraction of the field of view).</summary>
        public void Punch(float amount)
        {
            punch = amount;
            punchT = 0f;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float target = menu ? 0f : 1f;
            blend = Mathf.MoveTowards(blend, target, dt / blendSeconds);
            float e = blend * blend * (3f - 2f * blend);

            orbitAngle += orbitDegreesPerSecond * dt;
            float a = orbitAngle * Mathf.Deg2Rad;
            Vector3 orbitPos = orbitCentre + new Vector3(Mathf.Cos(a) * orbitRadius, orbitHeight, Mathf.Sin(a) * orbitRadius);
            Quaternion orbitRot = Quaternion.LookRotation(orbitCentre - orbitPos, Vector3.up);

            Vector3 pos = Vector3.Lerp(orbitPos, playPos, e);
            Quaternion rot = Quaternion.Slerp(orbitRot, playRot, e);

            if (shakeTime < shakeDur)
            {
                shakeTime += dt;
                float k = shakeAmp * (1f - shakeTime / shakeDur);
                float t = Time.unscaledTime * 35f;
                pos += new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * 2f * k;
            }

            float fov = baseFov;
            if (punchT >= 0f)
            {
                punchT += dt;
                float u = Mathf.Clamp01(punchT / 0.5f);
                fov = baseFov * (1f - punch * Mathf.Sin(u * Mathf.PI));
                if (u >= 1f) punchT = -1f;
            }

            transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
        }
    }
}
