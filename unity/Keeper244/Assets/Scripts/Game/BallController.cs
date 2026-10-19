using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Scripted ball flight from the penalty spot to the target point, then a short save/goal
    /// follow-through. No physics: the result is decided logically by the round state machine.
    /// </summary>
    public sealed class BallController : MonoBehaviour
    {
        [Tooltip("Extra height at mid-flight, metres.")]
        [SerializeField] float arcHeight = 0.5f;
        [Tooltip("Follow-through after arrival (rebound or into the net), seconds.")]
        [SerializeField] float outcomeSeconds = 0.6f;
        [Tooltip("Spin while flying, degrees per second.")]
        [SerializeField] float spinDegreesPerSecond = 720f;

        enum Phase { Idle, Flight, Outcome }

        Phase phase = Phase.Idle;
        Vector3 spot;
        Vector3 from;
        Vector3 to;
        float duration;
        float t;
        float radius;

        void Awake()
        {
            spot = transform.position;
            radius = transform.lossyScale.y * 0.5f;
        }

        public void ResetToSpot()
        {
            phase = Phase.Idle;
            transform.position = spot;
            transform.rotation = Quaternion.identity;
        }

        public void Launch(Vector3 target, float seconds)
        {
            from = transform.position;
            to = target;
            duration = Mathf.Max(0.01f, seconds);
            t = 0f;
            phase = Phase.Flight;
        }

        public void PlayOutcome(bool saved)
        {
            from = transform.position;
            to = saved
                ? new Vector3(from.x * 0.6f, radius, from.z + 4f)                       // parried back into play
                : new Vector3(from.x, radius, from.z - 1.5f);                           // into the net, drops to the grass
            duration = outcomeSeconds;
            t = 0f;
            phase = Phase.Outcome;
        }

        void Update()
        {
            if (phase == Phase.Idle) return;

            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float arc = phase == Phase.Flight ? arcHeight : 0.3f;
            Vector3 pos = Vector3.Lerp(from, to, u);
            pos.y += arc * 4f * u * (1f - u);
            transform.position = pos;
            transform.Rotate(Vector3.right, -spinDegreesPerSecond * Time.deltaTime, Space.World);

            if (u >= 1f && phase == Phase.Outcome) phase = Phase.Idle;
        }
    }
}
