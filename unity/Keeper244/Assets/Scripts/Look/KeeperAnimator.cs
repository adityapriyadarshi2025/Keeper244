using Keeper244.Core;
using Keeper244.Game;
using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Procedural keeper body language driven by where the keeper is:
    ///  centre  → crouched ready stance, gloves out, light bounce
    ///  near    → side-step, leaning, arms reaching to that side
    ///  far     → full dive: body rolls towards the post, both arms stretched over the head
    /// plus a fist-pump after a save and a slump after a goal. Purely visual: the save/goal
    /// decision never looks at these poses.
    /// </summary>
    public sealed class KeeperAnimator : MonoBehaviour
    {
        [SerializeField] HumanoidRig rig;
        [SerializeField] KeeperController keeper;
        [SerializeField] GameManager game;
        [Tooltip("Body roll at a full dive, degrees.")]
        [SerializeField] float diveRollDegrees = 62f;
        [SerializeField] float sharpness = 14f;

        float celebrateT = -1f;
        float slumpT = -1f;
        float lastX;
        float stepPhase;

        void OnEnable()
        {
            if (game != null) game.ShotResolved += OnShot;
        }

        void OnDisable()
        {
            if (game != null) game.ShotResolved -= OnShot;
        }

        void OnShot(ShotResult r)
        {
            if (r.Saved) { celebrateT = 0f; slumpT = -1f; }
            else { slumpT = 0f; celebrateT = -1f; }
        }

        void LateUpdate()
        {
            if (rig == null || !rig.IsComplete || keeper == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float x = keeper.transform.position.x;
            float vx = (x - lastX) / dt;
            lastX = x;

            float far = FieldGeometry.ZoneWidth(5) * 2f;              // 2.928 m
            float side = Mathf.Abs(x) < 0.05f ? 0f : Mathf.Sign(x);
            float reach = Mathf.Clamp01(Mathf.Abs(x) / far);          // 0 centre … 1 at the far post
            float dive = Mathf.Clamp01((reach - 0.35f) / 0.65f);      // only past the near slot
            dive = dive * dive * (3f - 2f * dive);
            bool moving = Mathf.Abs(vx) > 0.6f;
            stepPhase += dt * (moving ? 18f : 0f);

            float t = Time.time;
            float bounce = 0.015f * Mathf.Sin(t * 7f) * (1f - dive);

            // ---- hips: crouch, then drop and roll into the dive ----
            float hipY = Mathf.Lerp(0.88f, 0.70f, dive) + bounce;
            rig.SetHipHeight(hipY, sharpness, dt);
            HumanoidRig.Pose(rig.hips, new Vector3(0f, 0f, -side * diveRollDegrees * dive), sharpness * 0.8f, dt);

            // ---- spine and head ----
            float spineLean = Mathf.Lerp(18f, 5f, dive);
            HumanoidRig.Pose(rig.spine, new Vector3(spineLean, 0f, -side * 10f * reach), sharpness, dt);
            HumanoidRig.Pose(rig.head, new Vector3(-8f, 0f, side * 12f * dive), sharpness, dt);

            // ---- arms ----
            ArmPose(rig.upperArmL, rig.foreArmL, -1f, side, reach, dive, dt);
            ArmPose(rig.upperArmR, rig.foreArmR, +1f, side, reach, dive, dt);

            // ---- legs: bent ready stance, shuffle while moving, straight in the dive ----
            float step = moving ? Mathf.Sin(stepPhase) * 18f : 0f;
            float thighBend = Mathf.Lerp(-32f, -5f, dive);
            float shinBend = Mathf.Lerp(52f, 8f, dive);
            HumanoidRig.Pose(rig.thighL, new Vector3(thighBend + step, 0f, -8f - 12f * dive), sharpness, dt);
            HumanoidRig.Pose(rig.thighR, new Vector3(thighBend - step, 0f, 8f + 12f * dive), sharpness, dt);
            HumanoidRig.Pose(rig.shinL, new Vector3(shinBend, 0f, 0f), sharpness, dt);
            HumanoidRig.Pose(rig.shinR, new Vector3(shinBend, 0f, 0f), sharpness, dt);

            // ---- reactions override the arms for a moment ----
            if (celebrateT >= 0f)
            {
                celebrateT += dt;
                float u = Mathf.Clamp01(celebrateT / 0.9f);
                float pump = Mathf.Sin(u * Mathf.PI);
                HumanoidRig.Pose(rig.upperArmL, new Vector3(0f, 0f, -150f * pump), 20f, dt);
                HumanoidRig.Pose(rig.upperArmR, new Vector3(0f, 0f, 150f * pump), 20f, dt);
                if (u >= 1f) celebrateT = -1f;
            }
            if (slumpT >= 0f)
            {
                slumpT += dt;
                float u = Mathf.Clamp01(slumpT / 1.2f);
                float s = Mathf.Sin(u * Mathf.PI);
                HumanoidRig.Pose(rig.head, new Vector3(30f * s, 0f, 0f), 10f, dt);
                HumanoidRig.Pose(rig.spine, new Vector3(25f * s + 5f, 0f, 0f), 10f, dt);
                if (u >= 1f) slumpT = -1f;
            }
        }

        /// <summary>
        /// One arm. `outward` is −1 for the left arm, +1 for the right. The arm on the dive side
        /// reaches out and up; the other arm sweeps over the head towards the same side.
        /// </summary>
        void ArmPose(Transform upper, Transform fore, float outward, float side, float reach, float dive, float dt)
        {
            float readyZ = outward * 38f;
            float z;
            if (side == 0f)
                z = readyZ;
            else if (Mathf.Approximately(outward, side))
                z = outward * Mathf.Lerp(38f + 40f * reach, 165f, dive);   // lead arm: out, then overhead
            else
                z = outward * Mathf.Lerp(38f, 210f, Mathf.Max(dive, 0.25f * reach)); // trail arm sweeps up over the head
            float forward = Mathf.Lerp(-25f, -5f, dive);
            HumanoidRig.Pose(upper, new Vector3(forward, 0f, z), sharpness, dt);
            HumanoidRig.Pose(fore, new Vector3(Mathf.Lerp(-45f, -5f, dive), 0f, 0f), sharpness, dt);
        }
    }
}
