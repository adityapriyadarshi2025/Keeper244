using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Joint references of a simple primitive-built footballer (built by the scene builder).
    /// Animators write local rotations every frame; nothing is baked.
    ///
    /// Conventions (joint-local, character faces +z of its root):
    ///  limbs hang along −y; rotating a limb by −x swings it FORWARD, +x backward;
    ///  rotating by +z swings it towards +x (the character's right).
    /// </summary>
    public sealed class HumanoidRig : MonoBehaviour
    {
        public Transform hips;
        public Transform spine;
        public Transform head;
        public Transform upperArmL, foreArmL, upperArmR, foreArmR;
        public Transform thighL, shinL, thighR, shinR;

        [Tooltip("Hips height when standing straight, metres.")]
        public float standingHipHeight = 0.95f;

        public bool IsComplete =>
            hips && spine && head && upperArmL && foreArmL && upperArmR && foreArmR && thighL && shinL && thighR && shinR;

        /// <summary>Smoothly rotate a joint towards a target local Euler pose.</summary>
        public static void Pose(Transform joint, Vector3 euler, float sharpness, float dt)
        {
            if (joint == null) return;
            var target = Quaternion.Euler(euler);
            float k = 1f - Mathf.Exp(-sharpness * dt);
            joint.localRotation = Quaternion.Slerp(joint.localRotation, target, k);
        }

        public void SetHipHeight(float y, float sharpness, float dt)
        {
            if (hips == null) return;
            var p = hips.localPosition;
            float k = 1f - Mathf.Exp(-sharpness * dt);
            p.y = Mathf.Lerp(p.y, y, k);
            hips.localPosition = p;
        }
    }
}
