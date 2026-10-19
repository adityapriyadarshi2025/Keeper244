using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// The striker telegraphs the shot: during CUE he lines up for the target and makes a curved
    /// run-up that arrives at the ball exactly at the kick, then follows through. Visual only;
    /// all timing comes from the round state machine.
    /// </summary>
    public sealed class StrikerController : MonoBehaviour
    {
        public enum Phase { Idle, Setup, RunUp, Kick, FollowThrough, Return }

        [Tooltip("The ball on the penalty spot (its start position is the spot).")]
        [SerializeField] Transform ball;
        [SerializeField] float runUpDistance = 3.4f;
        [SerializeField] float runUpSideOffset = 1.5f;
        [Tooltip("Longest run-up in seconds (shorter cues get a quicker run).")]
        [SerializeField] float maxRunSeconds = 1.0f;
        [SerializeField] float kickSeconds = 0.35f;
        [SerializeField] float followThroughDistance = 0.9f;
        [SerializeField] float followThroughSeconds = 0.6f;
        [SerializeField] float walkSpeed = 2.6f;

        Vector3 spot;
        Vector3 aimDir = Vector3.back;
        Vector3 homePos;
        Vector3 startPos;
        Vector3 plantPos;
        Vector3 setupFrom;
        float t;
        float setupSeconds;
        float runSeconds;
        Vector3 lastPos;

        public Phase Current { get; private set; } = Phase.Idle;
        /// <summary>Horizontal speed, m/s (drives the leg cycle).</summary>
        public float Speed { get; private set; }
        /// <summary>Seconds until the planned kick (RunUp only).</summary>
        public float TimeToKick => Current == Phase.RunUp ? Mathf.Max(0f, runSeconds - t) : (Current == Phase.Setup ? setupSeconds - t + runSeconds : 0f);
        /// <summary>Seconds since the kick (Kick / FollowThrough).</summary>
        public float SinceKick { get; private set; }

        void Awake()
        {
            spot = ball != null ? ball.position : transform.position + Vector3.back * 1.4f;
            spot.y = 0f;
            homePos = StartFor(Vector3.back);
            ResetPose();
        }

        Vector3 Right(Vector3 dir) => Vector3.Cross(Vector3.up, dir).normalized;

        /// <summary>Right-footed run-up: from behind and to the left of the ball.</summary>
        Vector3 StartFor(Vector3 dir) => spot - dir * runUpDistance - Right(dir) * runUpSideOffset;

        public void ResetPose()
        {
            Current = Phase.Idle;
            transform.position = homePos;
            transform.rotation = Quaternion.LookRotation((spot - homePos).normalized, Vector3.up);
            lastPos = homePos;
            Speed = 0f;
        }

        /// <summary>Jog back to the run-up mark after a shot (no teleport while the camera watches).</summary>
        public void ReturnHome()
        {
            if (Current == Phase.Idle && (transform.position - homePos).sqrMagnitude < 0.01f) return;
            Current = Phase.Return;
        }

        /// <summary>CUE starts: line up for `target` and run so the right foot meets the ball after `cueSeconds`.</summary>
        public void BeginRunUp(Vector3 target, float cueSeconds)
        {
            Vector3 d = target - spot;
            d.y = 0f;
            aimDir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.back;
            startPos = StartFor(aimDir);
            plantPos = spot - aimDir * 0.45f - Right(aimDir) * 0.22f; // left foot planted beside the ball
            runSeconds = Mathf.Min(maxRunSeconds, cueSeconds * 0.72f);
            setupSeconds = Mathf.Max(0f, cueSeconds - runSeconds);
            setupFrom = transform.position;
            t = 0f;
            Current = setupSeconds > 0f ? Phase.Setup : Phase.RunUp;
        }

        public void Kick()
        {
            Current = Phase.Kick;
            SinceKick = 0f;
            transform.position = plantPos;
            transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;

            switch (Current)
            {
                case Phase.Setup:
                {
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, setupSeconds)));
                    transform.position = Vector3.Lerp(setupFrom, startPos, u);
                    Face(plantPos - startPos, 8f, dt);
                    if (t >= setupSeconds) { t -= setupSeconds; Current = Phase.RunUp; }
                    break;
                }
                case Phase.RunUp:
                {
                    float u = Mathf.Clamp01(t / Mathf.Max(0.01f, runSeconds));
                    // ease in, then a slight curve so the last strides come in square to the ball
                    float e = u * u * (3f - 2f * u) * 0.35f + u * 0.65f;
                    Vector3 straight = Vector3.Lerp(startPos, plantPos, e);
                    Vector3 bend = -Right(aimDir) * 0.4f * Mathf.Sin(e * Mathf.PI);
                    transform.position = straight + bend;
                    Face(transform.position - lastPos, 14f, dt);
                    break; // Kick() ends this phase, exactly on the state machine's kick
                }
                case Phase.Kick:
                {
                    SinceKick += dt;
                    Face(aimDir, 20f, dt);
                    if (SinceKick >= kickSeconds) { Current = Phase.FollowThrough; t = 0f; setupFrom = transform.position; }
                    break;
                }
                case Phase.FollowThrough:
                {
                    SinceKick += dt;
                    float u = Mathf.Clamp01(t / followThroughSeconds);
                    float e = 1f - (1f - u) * (1f - u);
                    transform.position = setupFrom + aimDir * followThroughDistance * e;
                    if (u >= 1f) Current = Phase.Return;
                    break;
                }
                case Phase.Return:
                {
                    Vector3 to = homePos - transform.position;
                    to.y = 0f;
                    float step = walkSpeed * dt;
                    if (to.magnitude <= step)
                    {
                        transform.position = homePos;
                        Face(spot - homePos, 8f, dt);
                        Current = Phase.Idle;
                    }
                    else
                    {
                        transform.position += to.normalized * step;
                        Face(to, 8f, dt);
                    }
                    break;
                }
                case Phase.Idle:
                    Face(spot - transform.position, 4f, dt);
                    break;
            }

            Vector3 delta = transform.position - lastPos;
            delta.y = 0f;
            Speed = Mathf.Lerp(Speed, delta.magnitude / dt, 1f - Mathf.Exp(-12f * dt));
            lastPos = transform.position;
        }

        void Face(Vector3 dir, float sharpness, float dt)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-sharpness * dt));
        }
    }
}
