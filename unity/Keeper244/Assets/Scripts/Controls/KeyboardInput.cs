using Keeper244.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Keeper244.Controls
{
    /// <summary>
    /// Keyboard stand-in for the EMG bridge, so the game can be built and played with no Python
    /// running (brief §5.5).
    ///
    ///   D / →          biceps light  → slot +1 (right, near)
    ///   Shift + D / →  biceps strong → slot +2 (right, far)
    ///   A / ←          triceps light → slot −1 (left, near)
    ///   Shift + A / ←  triceps strong→ slot −2 (left, far)
    ///   nothing held   relaxed       → slot 0 (centre)
    ///   A and D held   co-contraction→ slot 0 (ambiguous, like the EMG rule)
    ///
    /// It also fakes smooth activation values (ramping like an EMG envelope) so the HUD bars have
    /// something realistic to draw before real EMG exists.
    /// Runs before other scripts each frame, so the game reads this frame's keys (no 1-frame lag).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class KeyboardInput : MonoBehaviour, IInputProvider
    {
        [Header("Fake activations (fraction of MVC)")]
        [Tooltip("Level shown when a muscle is relaxed (EMG noise floor).")]
        [SerializeField] float restLevel = 0.02f;
        [Tooltip("Level for a light flex (near slot).")]
        [SerializeField] float lightLevel = 0.22f;
        [Tooltip("Level for a strong flex (far slot).")]
        [SerializeField] float strongLevel = 0.55f;
        [Tooltip("Envelope time constant in seconds: how fast the fake activation follows the keys.")]
        [SerializeField] float envelopeTau = 0.06f;

        float biceps;
        float triceps;
        int slot;
        bool atRest = true;

        public int Slot => slot;
        public float ActivationBiceps => biceps;
        public float ActivationTriceps => triceps;
        public bool IsAtRest => atRest;
        public bool HasSignal => Keyboard.current != null;
        public string SourceName => "Keyboard";
        public ChannelThresholds Thresholds => ChannelThresholds.Placeholder;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                slot = 0;
                atRest = true;
                return;
            }

            bool right = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
            bool left = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
            bool strong = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

            float bicepsTarget = right ? (strong ? strongLevel : lightLevel) : restLevel;
            float tricepsTarget = left ? (strong ? strongLevel : lightLevel) : restLevel;

            if (right && !left) slot = strong ? 2 : 1;
            else if (left && !right) slot = strong ? -2 : -1;
            else slot = 0;

            atRest = !right && !left;

            // First-order low-pass towards the target, like an EMG envelope.
            float k = envelopeTau > 0f ? 1f - Mathf.Exp(-Time.deltaTime / envelopeTau) : 1f;
            biceps += (bicepsTarget - biceps) * k;
            triceps += (tricepsTarget - triceps) * k;
        }
    }
}
