using Keeper244.Core;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// One level's tunables as an asset (Assets/Config/Levels/*.asset). Edit timings here, in the
    /// Inspector, never in code. Create more via Assets → Create → Keeper244 → Level Config.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelConfig", menuName = "Keeper244/Level Config", order = 0)]
    public sealed class LevelConfig : ScriptableObject
    {
        public LevelSettings settings = new LevelSettings();

        void OnValidate()
        {
            try { settings.Validate(); }
            catch (System.ArgumentException e) { Debug.LogWarning($"[Keeper244] {name}: {e.Message}", this); }
        }
    }
}
