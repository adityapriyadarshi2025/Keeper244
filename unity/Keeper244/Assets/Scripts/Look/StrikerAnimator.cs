using Keeper244.Game;
using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Striker body: relaxed stance, running stride (legs and arms in opposition, stride rate
    /// from speed), back-swing of the right leg in the last strides, strike, follow-through.
    /// </summary>
    public sealed class StrikerAnimator : MonoBehaviour
    {
        [SerializeField] HumanoidRig rig;
        [SerializeField] StrikerController striker;
        [SerializeField] float sharpness = 16f;

        float phase;

        void LateUpdate()
        {
            if (rig == null || !rig.IsComplete || striker == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float speed = striker.Speed;
            float run = Mathf.Clamp01(speed / 4f);
            phase += dt * (6f + speed * 2.2f);
            float s = Mathf.Sin(phase);

            float thighL = -38f * s * run, thighR = 38f * s * run;
            float shinL = 15f + 55f * Mathf.Max(0f, -s) * run, shinR = 15f + 55f * Mathf.Max(0f, s) * run;
            float armL = 30f * s * run, armR = -30f * s * run;
            float spineX = 4f + 14f * run;
            float hipY = rig.standingHipHeight - 0.03f * run + 0.03f * Mathf.Abs(Mathf.Cos(phase)) * run;
            float armSpreadL = -12f, armSpreadR = 12f;

            switch (striker.Current)
            {
                case StrikerController.Phase.RunUp:
                {
                    // last 0.25 s: plant left leg, cock the right leg back, arms out for balance
                    float k = Mathf.Clamp01(1f - striker.TimeToKick / 0.25f);
                    thighR = Mathf.Lerp(thighR, 45f, k);
                    shinR = Mathf.Lerp(shinR, 85f, k);
                    thighL = Mathf.Lerp(thighL, -10f, k);
                    shinL = Mathf.Lerp(shinL, 10f, k);
                    armSpreadL = Mathf.Lerp(-12f, -70f, k);
                    armSpreadR = Mathf.Lerp(12f, 40f, k);
                    spineX = Mathf.Lerp(spineX, -6f, k);
                    break;
                }
                case StrikerController.Phase.Kick:
                {
                    float u = Mathf.Clamp01(striker.SinceKick / 0.35f);
                    float swing = Mathf.Sin(u * Mathf.PI * 0.5f);
                    thighR = Mathf.Lerp(45f, -85f, swing);
                    shinR = Mathf.Lerp(85f, 5f, Mathf.Clamp01(swing * 1.6f));
                    thighL = -10f; shinL = 10f;
                    armSpreadL = -75f; armSpreadR = 45f;
                    spineX = -12f;
                    break;
                }
                case StrikerController.Phase.FollowThrough:
                {
                    float u = Mathf.Clamp01((striker.SinceKick - 0.35f) / 0.5f);
                    thighR = Mathf.Lerp(-85f, thighR, u);
                    shinR = Mathf.Lerp(5f, shinR, u);
                    armSpreadL = Mathf.Lerp(-75f, -12f, u);
                    armSpreadR = Mathf.Lerp(45f, 12f, u);
                    break;
                }
            }

            rig.SetHipHeight(hipY, sharpness, dt);
            HumanoidRig.Pose(rig.hips, Vector3.zero, sharpness, dt);
            HumanoidRig.Pose(rig.spine, new Vector3(spineX, 0f, 0f), sharpness, dt);
            HumanoidRig.Pose(rig.head, new Vector3(-spineX * 0.5f + 6f, 0f, 0f), sharpness, dt);
            HumanoidRig.Pose(rig.thighL, new Vector3(thighL, 0f, -4f), sharpness * 1.5f, dt);
            HumanoidRig.Pose(rig.thighR, new Vector3(thighR, 0f, 4f), sharpness * 1.5f, dt);
            HumanoidRig.Pose(rig.shinL, new Vector3(shinL, 0f, 0f), sharpness * 1.5f, dt);
            HumanoidRig.Pose(rig.shinR, new Vector3(shinR, 0f, 0f), sharpness * 1.5f, dt);
            HumanoidRig.Pose(rig.upperArmL, new Vector3(armL, 0f, armSpreadL), sharpness, dt);
            HumanoidRig.Pose(rig.upperArmR, new Vector3(armR, 0f, armSpreadR), sharpness, dt);
            HumanoidRig.Pose(rig.foreArmL, new Vector3(-55f * run - 15f, 0f, 0f), sharpness, dt);
            HumanoidRig.Pose(rig.foreArmR, new Vector3(-55f * run - 15f, 0f, 0f), sharpness, dt);
        }
    }
}
