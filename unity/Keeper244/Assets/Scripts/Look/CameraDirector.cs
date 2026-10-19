using System;
using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Main camera.
    ///  - Menu: a slow orbit around the stadium.
    ///  - Play: one of two fixed views (brief §3.8: the play camera never follows the keeper).
    ///      BehindGoal  low behind the keeper, looking out at the striker (the keeper's own view)
    ///      Front       TV view from behind the striker, looking at the keeper and the goal
    ///    Switching swings the camera round the side of the box instead of cutting.
    ///  - While the camera is low behind the home goal, the home net and its back bars are hidden:
    ///    they would sit between the player and the shot. Every other view shows them.
    ///  - A short shake on a goal and a zoom punch on a save.
    ///
    /// In the Front view the keeper faces the camera, so his right is on the LEFT of the screen.
    /// <see cref="Mirrored"/> tells the HUD and the keyboard to flip what they show/accept; the
    /// game logic and the EMG mapping stay keeper-relative (biceps = keeper's right).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraDirector : MonoBehaviour
    {
        public enum View { BehindGoal, Front }

        [Serializable]
        public struct Pose
        {
            public Vector3 position;
            public Vector3 lookAt;
            [Range(10f, 90f)] public float fieldOfView;

            public Pose(Vector3 position, Vector3 lookAt, float fieldOfView)
            {
                this.position = position;
                this.lookAt = lookAt;
                this.fieldOfView = fieldOfView;
            }
        }

        [Header("Play views")]
        [Tooltip("High enough that the crossbar sits below the ball and the striker on screen, not across them.")]
        [SerializeField] Pose behindGoal = new Pose(new Vector3(0f, 4f, -5.25f), new Vector3(0f, 0.6f, 6f), 50f);
        [SerializeField] Pose front = new Pose(new Vector3(0f, 5f, 27.5f), new Vector3(0f, 0.75f, 4f), 24f);
        [Tooltip("Seconds for the swing between the two play views.")]
        [SerializeField] float swingSeconds = 1.6f;
        [Tooltip("The swing circles this point (ground level, inside the box).")]
        [SerializeField] Vector3 swingPivot = new Vector3(0f, 0f, 9f);
        [Tooltip("Extra height at the middle of the swing, metres.")]
        [SerializeField] float swingLift = 3f;

        [Header("Home net")]
        [Tooltip("Hidden while the camera is low behind the home goal (net panels and back bars).")]
        [SerializeField] Renderer[] hideWhenBehindGoal = Array.Empty<Renderer>();
        [Tooltip("The camera counts as 'behind the goal' when z < this, |x| < halfWidth and y < maxHeight.")]
        [SerializeField] float behindGoalZ = -0.3f;
        [SerializeField] float behindGoalHalfWidth = 12f;
        [SerializeField] float behindGoalMaxHeight = 8f;

        [Header("Menu orbit")]
        [SerializeField] Vector3 orbitCentre = new Vector3(0f, 2f, 14f);
        [SerializeField] float orbitRadius = 30f;
        [SerializeField] float orbitHeight = 11f;
        [SerializeField] float orbitDegreesPerSecond = 4f;
        [SerializeField] float orbitFieldOfView = 55f;

        [Header("Transitions")]
        [Tooltip("Seconds to blend between the menu orbit and the play view.")]
        [SerializeField] float blendSeconds = 1.4f;

        Camera cam;
        bool menu = true;
        float orbitAngle = 200f;
        float blend;                       // 0 = menu orbit, 1 = play view
        View from = View.BehindGoal;
        View to = View.BehindGoal;
        float swing = 1f;                  // 0 = at `from`, 1 = at `to`
        float shakeAmp, shakeTime, shakeDur;
        float punch, punchT = -1f;
        bool netHidden;

        /// <summary>The play view selected (the camera may still be swinging towards it).</summary>
        public View Current => to;

        /// <summary>true when the keeper faces the camera, so his right is on the left of the screen.</summary>
        public bool Mirrored => to == View.Front;

        /// <summary>Raised when the selected play view changes.</summary>
        public event Action<View> ViewChanged;

        /// <summary>Short name used in logs and on screen.</summary>
        public static string Name(View v) => v == View.Front ? "front" : "behind";

        void Awake()
        {
            cam = GetComponent<Camera>();
            if (hideWhenBehindGoal == null || hideWhenBehindGoal.Length == 0)
                hideWhenBehindGoal = FindHomeGoalBackParts();
        }

        /// <summary>
        /// Fallback for a scene built before the list was wired: find the home goal's net panels and
        /// back bars by their SceneBuilder names (Net_*, Back*).
        /// </summary>
        static Renderer[] FindHomeGoalBackParts()
        {
            var goal = GameObject.Find("HomeGoal");
            if (goal == null) return Array.Empty<Renderer>();
            var parts = new System.Collections.Generic.List<Renderer>();
            foreach (var r in goal.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Net_", StringComparison.Ordinal) || r.name.StartsWith("Back", StringComparison.Ordinal))
                    parts.Add(r);
            return parts.ToArray();
        }

        /// <summary>true = menu orbit, false = play view.</summary>
        public void SetMenu(bool showMenu) => menu = showMenu;

        /// <summary>Select a play view. Swings there unless `instant` (or the menu orbit is showing).</summary>
        public void SetView(View view, bool instant = false)
        {
            bool changed = view != to;
            if (instant || blend <= 0f)
            {
                from = to = view;
                swing = 1f;
            }
            else if (changed)
            {
                // the path is symmetric, so reversing mid-swing continues from where the camera is
                from = to;
                to = view;
                swing = 1f - swing;
            }
            if (changed) ViewChanged?.Invoke(view);
        }

        public void ToggleView() => SetView(to == View.Front ? View.BehindGoal : View.Front);

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
            float dt = Time.unscaledDeltaTime;  // keeps moving while paused
            blend = Mathf.MoveTowards(blend, menu ? 0f : 1f, dt / Mathf.Max(0.01f, blendSeconds));
            float eb = blend * blend * (3f - 2f * blend);
            if (swing < 1f) swing = Mathf.Min(1f, swing + dt / Mathf.Max(0.01f, swingSeconds));

            PlayPose(out var playPos, out var playLook, out float playFov);
            var playRot = Quaternion.LookRotation(playLook - playPos, Vector3.up);

            orbitAngle += orbitDegreesPerSecond * dt;
            float a = orbitAngle * Mathf.Deg2Rad;
            Vector3 orbitPos = orbitCentre + new Vector3(Mathf.Cos(a) * orbitRadius, orbitHeight, Mathf.Sin(a) * orbitRadius);
            Quaternion orbitRot = Quaternion.LookRotation(orbitCentre - orbitPos, Vector3.up);

            Vector3 pos = Vector3.Lerp(orbitPos, playPos, eb);
            Quaternion rot = Quaternion.Slerp(orbitRot, playRot, eb);
            float fov = Mathf.Lerp(orbitFieldOfView, playFov, eb);

            UpdateNet(pos);

            if (shakeTime < shakeDur)
            {
                shakeTime += dt;
                float k = shakeAmp * (1f - shakeTime / shakeDur);
                float t = Time.unscaledTime * 35f;
                pos += rot * new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * 2f * k;
            }

            if (punchT >= 0f)
            {
                punchT += dt;
                float u = Mathf.Clamp01(punchT / 0.5f);
                fov *= 1f - punch * Mathf.Sin(u * Mathf.PI);
                if (u >= 1f) punchT = -1f;
            }

            transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
        }

        Pose PoseOf(View v) => v == View.Front ? front : behindGoal;

        /// <summary>
        /// The play camera: the selected view, or a point on the swing between the two. The swing
        /// circles the pivot (so the camera never passes over its own look-at point), rises a little
        /// in the middle, and always goes round the +x side so reversing it is continuous.
        /// </summary>
        void PlayPose(out Vector3 pos, out Vector3 look, out float fov)
        {
            var b = PoseOf(to);
            if (swing >= 1f || from == to)
            {
                pos = b.position;
                look = b.lookAt;
                fov = b.fieldOfView;
                return;
            }

            var a = PoseOf(from);
            float e = swing * swing * (3f - 2f * swing);
            Vector3 ra = a.position - swingPivot, rb = b.position - swingPivot;
            float ang = Mathf.Lerp(Heading(ra), Heading(rb), e);
            float radius = Mathf.Lerp(new Vector2(ra.x, ra.z).magnitude, new Vector2(rb.x, rb.z).magnitude, e);
            float y = Mathf.Lerp(a.position.y, b.position.y, e) + swingLift * Mathf.Sin(e * Mathf.PI);
            pos = new Vector3(swingPivot.x + Mathf.Sin(ang) * radius, y, swingPivot.z + Mathf.Cos(ang) * radius);
            look = Vector3.Lerp(a.lookAt, b.lookAt, e);
            fov = Mathf.Lerp(a.fieldOfView, b.fieldOfView, e);
        }

        /// <summary>Heading around +y in [0, 2π), 0 = +z. Behind the goal is π, the front view is 0.</summary>
        static float Heading(Vector3 r)
        {
            float h = Mathf.Atan2(r.x, r.z);
            return h < 0f ? h + 2f * Mathf.PI : h;
        }

        void UpdateNet(Vector3 camPos)
        {
            bool hide = camPos.z < behindGoalZ && Mathf.Abs(camPos.x) < behindGoalHalfWidth && camPos.y < behindGoalMaxHeight;
            if (hide == netHidden) return;
            netHidden = hide;
            foreach (var r in hideWhenBehindGoal)
                if (r != null) r.enabled = !hide;
        }
    }
}
