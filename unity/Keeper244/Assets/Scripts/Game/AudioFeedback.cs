using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// All sound is synthesised in code (no audio files): crowd ambience loop, referee whistle at
    /// the start of a level, cue blip, kick thud, crowd cheer + chime on a save, crowd groan + buzz
    /// on a goal, and a fanfare when a level ends. Listens to GameManager events.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class AudioFeedback : MonoBehaviour
    {
        [SerializeField] GameManager game;
        [SerializeField, Range(0f, 1f)] float volume = 0.7f;
        [SerializeField, Range(0f, 1f)] float crowdVolume = 0.35f;

        const int Rate = 44100;

        AudioSource source;
        AudioSource ambience;
        AudioClip cue, kick, save, goal, complete, whistle, cheer, groan, crowdLoop;
        readonly System.Random rng = new System.Random(244);

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            cue = Tone("cue", 0.06f, 880f, 880f, 0.2f);
            kick = Kick();
            save = Concat("save", Tone(null, 0.09f, 660f, 660f, 0.3f), Tone(null, 0.16f, 990f, 990f, 0.3f));
            goal = Tone("goal", 0.38f, 220f, 110f, 0.25f, square: true);
            complete = Concat("complete", Tone(null, 0.1f, 523f, 523f, 0.3f), Tone(null, 0.1f, 659f, 659f, 0.3f), Tone(null, 0.25f, 784f, 784f, 0.3f));
            whistle = Concat("whistle", Whistle(0.18f), Silence(0.08f), Whistle(0.55f));
            cheer = CrowdBurst("cheer", 2.2f, 900f, 0.06f, 0.6f, rising: true);
            groan = CrowdBurst("groan", 1.6f, 380f, 0.15f, 0.5f, rising: false);
            crowdLoop = CrowdLoop(6f);

            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = crowdLoop;
            ambience.loop = true;
            ambience.playOnAwake = false;
            ambience.spatialBlend = 0f;
            ambience.volume = crowdVolume;
        }

        void Start()
        {
            if (crowdVolume > 0f) ambience.Play();
        }

        void OnEnable()
        {
            if (game == null) return;
            game.LevelStarted += OnLevelStarted;
            game.CueStarted += OnCue;
            game.Kicked += OnKick;
            game.ShotResolved += OnResolved;
            game.LevelCompleted += OnComplete;
            game.ShotVoided += OnVoided;
        }

        void OnDisable()
        {
            if (game == null) return;
            game.LevelStarted -= OnLevelStarted;
            game.CueStarted -= OnCue;
            game.Kicked -= OnKick;
            game.ShotResolved -= OnResolved;
            game.LevelCompleted -= OnComplete;
            game.ShotVoided -= OnVoided;
        }

        void OnLevelStarted(LevelConfig level) => Play(whistle, 0.8f);
        void OnVoided(int shot, int target) => Play(whistle, 0.6f); // referee stops play
        void OnCue(int shot, int target) => Play(cue);
        void OnKick(int shot, int target) => Play(kick);

        void OnResolved(ShotResult r)
        {
            if (r.Saved) { Play(save); Play(cheer, crowdVolume * 2.2f); }
            else { Play(goal, 0.8f); Play(groan, crowdVolume * 2f); }
        }

        void OnComplete(LevelResult r)
        {
            Play(complete);
            if (r.Passed) Play(cheer, crowdVolume * 2.5f);
        }

        void Play(AudioClip clip, float gain = 1f)
        {
            if (clip != null && volume > 0f) source.PlayOneShot(clip, volume * gain);
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
                phase += 2.0 * System.Math.PI * Mathf.Lerp(f0, f1, u) / Rate;
                float s = (float)System.Math.Sin(phase);
                if (square) s = s >= 0f ? 0.6f : -0.6f;
                float env = Mathf.Min(1f, i / (0.005f * Rate)) * (1f - u);
                data[i] = s * env * gain;
            }
            return Make(name ?? "tone", data);
        }

        static AudioClip Silence(float seconds) => Make("silence", new float[(int)(seconds * Rate)]);

        /// <summary>Pea whistle: ~2.9 kHz with a fast warble from the pea.</summary>
        static AudioClip Whistle(float seconds)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float warble = Mathf.Sin(2f * Mathf.PI * 32f * t);
                phase += 2.0 * System.Math.PI * (2900f + 120f * warble) / Rate;
                float env = Mathf.Min(1f, t / 0.015f) * Mathf.Min(1f, (seconds - t) / 0.03f);
                float am = 0.75f + 0.25f * warble;
                data[i] = (float)System.Math.Sin(phase) * env * am * 0.35f;
            }
            return Make("whistle", data);
        }

        static AudioClip Kick()
        {
            int n = (int)(0.12f * Rate);
            var data = new float[n];
            var r = new System.Random(244);
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                phase += 2.0 * System.Math.PI * Mathf.Lerp(120f, 50f, t / 0.12f) / Rate;
                float thump = (float)System.Math.Sin(phase) * Mathf.Exp(-t * 30f);
                float noise = ((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 90f);
                data[i] = 0.55f * thump + 0.25f * noise;
            }
            return Make("kick", data);
        }

        /// <summary>Band-passed noise with a swell: thousands of voices from far away.</summary>
        AudioClip CrowdBurst(string name, float seconds, float centreHz, float attack, float gain, bool rising)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            var bp = new BandPass(centreHz, 0.8f);
            var bp2 = new BandPass(centreHz * 2.1f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                float env = Mathf.Min(1f, t / attack) * Mathf.Exp(-Mathf.Max(0f, t - attack) * (rising ? 1.4f : 2.2f));
                float wobble = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 3.3f * t) + 0.1f * Mathf.Sin(2f * Mathf.PI * 5.1f * t);
                data[i] = (bp.Process(noise) * 0.8f + bp2.Process(noise) * 0.35f) * env * wobble * gain * 3f;
            }
            return Make(name, data);
        }

        /// <summary>Seamless murmur loop: low band-passed noise with slow random swells, cross-faded at the ends.</summary>
        AudioClip CrowdLoop(float seconds)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            var bp = new BandPass(500f, 0.6f);
            var bp2 = new BandPass(1200f, 1.0f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                float swell = 0.75f + 0.15f * Mathf.Sin(2f * Mathf.PI * t / seconds * 2f) + 0.1f * Mathf.Sin(2f * Mathf.PI * t / seconds * 5f);
                data[i] = (bp.Process(noise) + 0.4f * bp2.Process(noise)) * swell * 1.2f;
            }
            int fade = Rate / 2;
            for (int i = 0; i < fade; i++)
            {
                float w = (float)i / fade;
                data[i] = data[i] * w + data[n - fade + i] * (1f - w);
            }
            var looped = new float[n - fade];
            System.Array.Copy(data, looped, looped.Length);
            return Make("crowd", looped);
        }

        /// <summary>RBJ biquad band-pass (constant 0 dB peak gain).</summary>
        sealed class BandPass
        {
            readonly float b0, b2, a1, a2;
            float x1, x2, y1, y2;

            public BandPass(float f0, float q)
            {
                float w0 = 2f * Mathf.PI * f0 / Rate;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float a0 = 1f + alpha;
                b0 = alpha / a0;
                b2 = -alpha / a0;
                a1 = -2f * Mathf.Cos(w0) / a0;
                a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return y;
            }
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
