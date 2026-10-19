using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Sound effects synthesised in code (no audio files): cue blip, kick thud, save chime,
    /// goal buzz, level-complete arpeggio. Listens to GameManager events.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioFeedback : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField, Range(0f, 1f)] float volume = 0.6f;

        const int Rate = 44100;

        AudioSource source;
        AudioClip cue, kick, save, goal, complete;

        public float Volume { get => volume; set => volume = Mathf.Clamp01(value); }

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            cue = Tone("cue", 0.06f, f0: 880f, f1: 880f, gain: 0.25f);
            kick = Kick();
            save = Concat("save", Tone(null, 0.09f, 660f, 660f, 0.35f), Tone(null, 0.14f, 990f, 990f, 0.35f));
            goal = Tone("goal", 0.38f, f0: 220f, f1: 110f, gain: 0.35f, square: true);
            complete = Concat("complete", Tone(null, 0.1f, 523f, 523f, 0.3f), Tone(null, 0.1f, 659f, 659f, 0.3f), Tone(null, 0.22f, 784f, 784f, 0.3f));
        }

        void OnEnable()
        {
            if (game == null) return;
            game.CueStarted += OnCue;
            game.Kicked += OnKick;
            game.ShotResolved += OnResolved;
            game.LevelCompleted += OnComplete;
        }

        void OnDisable()
        {
            if (game == null) return;
            game.CueStarted -= OnCue;
            game.Kicked -= OnKick;
            game.ShotResolved -= OnResolved;
            game.LevelCompleted -= OnComplete;
        }

        void OnCue(int shot, int target) => Play(cue);
        void OnKick(int shot, int target) => Play(kick);
        void OnResolved(ShotResult r) => Play(r.Saved ? save : goal);
        void OnComplete(LevelResult r) => Play(complete);

        void Play(AudioClip clip)
        {
            if (clip != null && volume > 0f) source.PlayOneShot(clip, volume);
        }

        // ---------- synthesis ----------

        static AudioClip Tone(string name, float seconds, float f0, float f1, float gain, bool square = false)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                double f = Mathf.Lerp(f0, f1, u);
                phase += 2.0 * Mathf.PI * f / Rate;
                float s = (float)System.Math.Sin(phase);
                if (square) s = s >= 0f ? 0.6f : -0.6f;
                float env = Mathf.Min(1f, i / (0.005f * Rate)) * (1f - u); // 5 ms attack, linear decay
                data[i] = s * env * gain;
            }
            return Make(name ?? "tone", data);
        }

        static AudioClip Kick()
        {
            int n = (int)(0.12f * Rate);
            var data = new float[n];
            var rng = new System.Random(244);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                phase += 2.0 * Mathf.PI * Mathf.Lerp(120f, 50f, t / 0.12f) / Rate;
                float thump = (float)System.Math.Sin(phase) * Mathf.Exp(-t * 30f);
                float noise = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f);
                data[i] = 0.5f * thump + 0.25f * noise;
            }
            return Make("kick", data);
        }

        static AudioClip Concat(string name, params AudioClip[] parts)
        {
            int total = 0;
            foreach (var p in parts) total += p.samples;
            var data = new float[total];
            int offset = 0;
            foreach (var p in parts)
            {
                var buf = new float[p.samples];
                p.GetData(buf, 0);
                buf.CopyTo(data, offset);
                offset += p.samples;
            }
            return Make(name, data);
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
