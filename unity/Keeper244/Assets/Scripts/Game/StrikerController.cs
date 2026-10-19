using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// The striker telegraphs the shot during CUE by turning towards the target, then lunges on
    /// the kick. Visual only; timing comes from the round state machine.
    /// </summary>
    public sealed class StrikerController : MonoBehaviour
    {
        [SerializeField] float turnSeconds = 0.6f;
        [SerializeField] float lungeDistance = 0.8f;
        [SerializeField] float lungeSeconds = 0.3f;

        Vector3 homePosition;
        Quaternion homeRotation;
        Quaternion fromRotation;
        Quaternion toRotation;
        float turnT;
        float lungeT = -1f;

        void Awake()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            fromRotation = toRotation = homeRotation;
            turnT = turnSeconds;
        }

        public void ResetPose()
        {
            transform.SetPositionAndRotation(homePosition, homeRotation);
            fromRotation = toRotation = homeRotation;
            turnT = turnSeconds;
            lungeT = -1f;
        }

        public void AimAt(Vector3 target)
        {
            Vector3 d = target - homePosition;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            fromRotation = transform.rotation;
            toRotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            turnT = 0f;
        }

        public void Kick() => lungeT = 0f;

        void Update()
        {
            if (turnT < turnSeconds)
            {
                turnT += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(turnT / turnSeconds));
                transform.rotation = Quaternion.Slerp(fromRotation, toRotation, u);
            }

            if (lungeT >= 0f)
            {
                lungeT += Time.deltaTime;
                float u = Mathf.Clamp01(lungeT / lungeSeconds);
                float offset = Mathf.Sin(Mathf.PI * u) * lungeDistance;
                transform.position = homePosition + transform.forward * offset;
                if (u >= 1f) lungeT = -1f;
            }
        }
    }
}
