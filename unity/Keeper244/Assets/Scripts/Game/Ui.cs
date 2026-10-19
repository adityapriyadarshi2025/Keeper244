using System.Collections.Generic;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Tiny IMGUI toolkit shared by the HUD and the menus. Layout is in a virtual 1080-pixel-high
    /// space; S() converts to real pixels, and fonts are sized in real pixels so text stays sharp
    /// at any resolution. Must only be used inside OnGUI.
    /// </summary>
    public static class Ui
    {
        public const float RefHeight = 1080f;

        public static readonly Color Panel = new Color(0.05f, 0.07f, 0.09f, 0.78f);
        public static readonly Color PanelLight = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Text = new Color(0.96f, 0.96f, 0.96f);
        public static readonly Color Muted = new Color(0.68f, 0.72f, 0.76f);
        public static readonly Color Accent = new Color(1f, 0.6f, 0.15f);    // keeper orange
        public static readonly Color Cue = new Color(1f, 0.85f, 0.1f);       // target yellow
        public static readonly Color Good = new Color(0.3f, 0.9f, 0.4f);
        public static readonly Color Bad = new Color(0.95f, 0.3f, 0.3f);
        public static readonly Color Info = new Color(0.55f, 0.8f, 1f);

        static Texture2D white;
        static readonly Dictionary<string, GUIStyle> cache = new Dictionary<string, GUIStyle>();
        static int cachedHeight = -1;

        public static float Scale => Screen.height / RefHeight;
        /// <summary>Screen width in virtual units.</summary>
        public static float Width => Screen.width / Scale;
        public static float S(float v) => v * Scale;
        public static Rect R(float x, float y, float w, float h) => new Rect(S(x), S(y), S(w), S(h));

        public static Texture2D White
        {
            get
            {
                if (white == null)
                {
                    white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    white.SetPixel(0, 0, Color.white);
                    white.Apply();
                }
                return white;
            }
        }

        /// <summary>Filled rectangle, virtual units.</summary>
        public static void Fill(float x, float y, float w, float h, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(R(x, y, w, h), White);
            GUI.color = old;
        }

        /// <summary>Rectangle outline of thickness t, virtual units.</summary>
        public static void Frame(float x, float y, float w, float h, Color c, float t)
        {
            Fill(x, y, w, t, c);
            Fill(x, y + h - t, w, t, c);
            Fill(x, y, t, h, c);
            Fill(x + w - t, y, t, h, c);
        }

        public static void Label(float x, float y, float w, float h, string text, int size, Color color,
                                 TextAnchor align = TextAnchor.UpperLeft, bool bold = false)
        {
            GUI.Label(R(x, y, w, h), text, LabelStyle(size, color, align, bold));
        }

        public static GUIStyle LabelStyle(int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool bold = false)
        {
            ResetIfResized();
            string key = $"L{size}|{color}|{align}|{bold}";
            if (!cache.TryGetValue(key, out var st))
            {
                st = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(8, Mathf.RoundToInt(S(size))),
                    alignment = align,
                    fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                    richText = true,
                    wordWrap = true,
                    clipping = TextClipping.Overflow,
                };
                st.normal.textColor = color;
                cache[key] = st;
            }
            return st;
        }

        public static GUIStyle ButtonStyle(int size = 24)
        {
            ResetIfResized();
            string key = $"B{size}";
            if (!cache.TryGetValue(key, out var st))
            {
                st = new GUIStyle(GUI.skin.button)
                {
                    fontSize = Mathf.RoundToInt(S(size)),
                    fixedHeight = S(size * 2.1f),
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                };
                cache[key] = st;
            }
            return st;
        }

        public static GUIStyle FieldStyle(int size = 24)
        {
            ResetIfResized();
            string key = $"F{size}";
            if (!cache.TryGetValue(key, out var st))
            {
                st = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = Mathf.RoundToInt(S(size)),
                    fixedHeight = S(size * 1.8f),
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset((int)S(10), (int)S(10), 0, 0),
                };
                cache[key] = st;
            }
            return st;
        }

        public static GUIStyle ToggleStyle(int size = 22)
        {
            ResetIfResized();
            string key = $"T{size}";
            if (!cache.TryGetValue(key, out var st))
            {
                st = new GUIStyle(GUI.skin.toggle) { fontSize = Mathf.RoundToInt(S(size)), richText = true };
                st.normal.textColor = st.onNormal.textColor = Text;
                cache[key] = st;
            }
            return st;
        }

        /// <summary>Darkened panel with a thin top accent line.</summary>
        public static void PanelBox(float x, float y, float w, float h, Color? accent = null)
        {
            Fill(x, y, w, h, Panel);
            Fill(x, y, w, 4f, accent ?? Accent);
        }

        public static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString();

        static void ResetIfResized()
        {
            if (cachedHeight != Screen.height)
            {
                cache.Clear();
                cachedHeight = Screen.height;
            }
        }
    }
}
