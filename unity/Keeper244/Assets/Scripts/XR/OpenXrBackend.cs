using Keeper244.Look;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace Keeper244.XR
{
    /// <summary>
    /// Starts the headset through XR Plug-in Management (OpenXR loader → Meta's OpenXR runtime →
    /// Rift S). This assembly only compiles when XR Plug-in Management is installed (asmdef define
    /// constraint KEEPER_XR), and registers itself with <see cref="VrSession"/> at load.
    ///
    /// Keep "Initialize XR on Startup" OFF in Project Settings → XR Plug-in Management: the game
    /// starts XR itself, and only on Windows, so a Mac never tries to load OpenXR.
    /// </summary>
    sealed class OpenXrBackend : IVrBackend
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => VrSession.Backend = new OpenXrBackend();

        static XRManagerSettings Manager => XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;

        public bool IsRunning
        {
            get
            {
                var m = Manager;
                return m != null && m.isInitializationComplete && XRSettings.isDeviceActive;
            }
        }

        public bool TryStart(out string error)
        {
            var m = Manager;
            if (m == null)
            {
                error = "OpenXR is not enabled: Project Settings → XR Plug-in Management → tick OpenXR (docs/SETUP.md §I)";
                return false;
            }
            if (!m.isInitializationComplete) m.InitializeLoaderSync();
            if (m.activeLoader == null)
            {
                error = "No headset: plug in the Rift S, open Meta Horizon Link and set it as the OpenXR runtime";
                return false;
            }
            m.StartSubsystems();
            error = null;
            return true;
        }

        public void Stop()
        {
            var m = Manager;
            if (m == null || !m.isInitializationComplete) return;
            m.StopSubsystems();
            m.DeinitializeLoader();
        }
    }
}
