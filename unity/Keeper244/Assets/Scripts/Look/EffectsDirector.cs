using Keeper244.Core;
using Keeper244.Game;
using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Game-feel effects: dust puff at the kick, ball trail in flight, confetti burst and camera
    /// punch on a save, net bulge and camera shake on a goal.
    /// </summary>
    public sealed class EffectsDirector : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField] CameraDirector cameraDirector;
        [SerializeField] TrailRenderer ballTrail;
        [SerializeField] ParticleSystem kickDust;
        [SerializeField] ParticleSystem saveBurst;
        [SerializeField] ParticleSystem saveSparks;
        [Tooltip("Parent of the home goal's net panels; scaled briefly when a goal goes in.")]
        [SerializeField] Transform homeNet;

        float bulgeT = -1f;
        Vector3 netScale = Vector3.one;

        void Awake()
        {
            if (homeNet != null) netScale = homeNet.localScale;
            if (ballTrail != null) ballTrail.emitting = false;
            // soft round particles for dust and sparks (confetti stays square, like paper)
            var dot = ProceduralTextures.SoftDot();
            foreach (var ps in new[] { kickDust, saveSparks })
                if (ps != null) ps.GetComponent<ParticleSystemRenderer>().material.mainTexture = dot;
        }

        void OnEnable()
        {
            if (game == null) return;
            game.CueStarted += OnCue;
            game.Kicked += OnKick;
            game.ShotResolved += OnShot;
        }

        void OnDisable()
        {
            if (game == null) return;
            game.CueStarted -= OnCue;
            game.Kicked -= OnKick;
            game.ShotResolved -= OnShot;
        }

        void OnCue(int shot, int target)
        {
            if (ballTrail != null) { ballTrail.emitting = false; ballTrail.Clear(); }
        }

        void OnKick(int shot, int target)
        {
            var ballPos = game.Ball != null ? game.Ball.transform.position : Vector3.zero;
            Emit(kickDust, ballPos, 18);
            if (ballTrail != null) { ballTrail.Clear(); ballTrail.emitting = true; }
        }

        void OnShot(ShotResult r)
        {
            var ballPos = game.Ball != null ? game.Ball.transform.position : Vector3.zero;
            if (r.Saved)
            {
                Emit(saveBurst, ballPos, 70);
                Emit(saveSparks, ballPos, 30);
                if (cameraDirector != null) cameraDirector.Punch(0.12f);
                if (ballTrail != null) ballTrail.emitting = false;
            }
            else
            {
                bulgeT = 0f;
                if (cameraDirector != null) cameraDirector.Shake(0.12f, 0.45f);
                if (ballTrail != null) ballTrail.emitting = false;
            }
        }

        static void Emit(ParticleSystem ps, Vector3 at, int count)
        {
            if (ps == null) return;
            ps.transform.position = at;
            ps.Emit(count);
        }

        void Update()
        {
            if (bulgeT < 0f || homeNet == null) return;
            bulgeT += Time.deltaTime;
            float u = Mathf.Clamp01(bulgeT / 0.7f);
            // quick push back, then a damped wobble
            float bulge = Mathf.Sin(u * Mathf.PI * 3f) * Mathf.Exp(-u * 4f) * 0.35f;
            homeNet.localScale = new Vector3(netScale.x, netScale.y, netScale.z * (1f + bulge));
            if (u >= 1f) { homeNet.localScale = netScale; bulgeT = -1f; }
        }
    }
}
