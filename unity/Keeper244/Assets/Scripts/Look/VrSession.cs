using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Keeper244.Look
{
    /// <summary>
    /// Starts and stops the headset. Implemented by Keeper244.XR (Assets/Scripts/XR), which Unity
    /// compiles only when XR Plug-in Management is installed, so the game builds and runs without
    /// any XR packages (desktop only).
    /// </summary>
    public interface IVrBackend
    {
        /// <summary>Start the OpenXR loader and its subsystems. False + a reason if no headset / runtime.</summary>
        bool TryStart(out string error);
        void Stop();
        /// <summary>Loader initialised and the headset display running.</summary>
        bool IsRunning { get; }
    }

    /// <summary>
    /// The VR headset (brief §3.8: the lab's Oculus Rift S, through OpenXR). Desktop is the default.
    /// On Windows the game tries the headset when it starts, and the operator can start or stop it
    /// from the menu. The Rift S cannot run on macOS, so on a Mac this stays off without trying.
    ///
    /// GameFlow listens to <see cref="Changed"/>: VR on → fixed coach seat (CameraDirector) and the
    /// world-space HUD (VrHud); the desktop window keeps the operator's menus and HUD.
    /// </summary>
    public sealed class VrSession : MonoBehaviour
    {
        public enum State { NotInstalled, NotSupported, Off, On, Failed }

        /// <summary>Set by Keeper244.XR when it loads; null when the XR packages are not installed.</summary>
        public static IVrBackend Backend;

        [Tooltip("Windows: try to start the headset when the game starts.")]
        [SerializeField] bool startOnLaunch = true;
        [Tooltip("Seconds the headset may report 'not running' right after starting before it counts as lost.")]
        [SerializeField] float startGraceSeconds = 3f;

        float startedAt;
        float notRunningFor;

        public State Current { get; private set; } = State.Off;
        /// <summary>One line for the operator's menu.</summary>
        public string Message { get; private set; } = "";
        public bool IsOn => Current == State.On;
        public bool CanStart => Backend != null && PlatformSupportsVr && !IsOn;
        public string DeviceName => string.IsNullOrEmpty(XRSettings.loadedDeviceName) ? "headset" : XRSettings.loadedDeviceName;

        /// <summary>true = the game is now shown in the headset, false = back to desktop.</summary>
        public event Action<bool> Changed;

        static readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

        /// <summary>Refresh rate of the running headset display (Rift S: 80 Hz), 0 if none.</summary>
        public static float RefreshRate
        {
            get
            {
                SubsystemManager.GetSubsystems(displays);
                foreach (var d in displays)
                    if (d.running && d.TryGetDisplayRefreshRate(out float hz)) return hz;
                return 0f;
            }
        }

        public static bool PlatformSupportsVr =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        void Start()
        {
            if (Backend == null)
                Set(State.NotInstalled, "VR packages not installed (docs/SETUP.md §I)");
            else if (!PlatformSupportsVr)
                Set(State.NotSupported, "VR needs the Windows PC (the Rift S can't run on macOS)");
            else if (startOnLaunch)
                TryStart();
            else
                Set(State.Off, "Headset off");
        }

        public void TryStart()
        {
            if (!CanStart) return;
            if (Backend.TryStart(out string error))
            {
                startedAt = Time.unscaledTime;
                notRunningFor = 0f;
                Set(State.On, $"In the headset ({DeviceName})");
                Debug.Log($"[Keeper244] VR on: {DeviceName}, {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} per eye");
                Changed?.Invoke(true);
            }
            else
            {
                Set(State.Failed, error);
                Debug.LogWarning($"[Keeper244] VR not started: {error}");
            }
        }

        public void Stop() => StopWith(State.Off, "Headset off");

        void StopWith(State state, string message)
        {
            if (!IsOn) return;
            Backend?.Stop();
            Set(state, message);
            Changed?.Invoke(false);
        }

        void Update()
        {
            if (!IsOn || Backend == null) return;
            // the runtime can go away (headset unplugged, Meta app closed): fall back to desktop
            if (Backend.IsRunning || Time.unscaledTime - startedAt < startGraceSeconds) { notRunningFor = 0f; return; }
            notRunningFor += Time.unscaledDeltaTime;
            if (notRunningFor > 1f)
            {
                Debug.LogWarning("[Keeper244] VR headset lost: back to desktop.");
                StopWith(State.Failed, "Headset lost: check the cable and the Meta app, then Start VR");
            }
        }

        void OnDestroy()
        {
            if (IsOn) Backend?.Stop();
        }

        void Set(State state, string message)
        {
            Current = state;
            Message = message;
        }
    }
}
