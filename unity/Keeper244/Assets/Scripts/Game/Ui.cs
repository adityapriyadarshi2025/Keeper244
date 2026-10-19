using System.Collections.Generic;
using Keeper244.Look;
using UnityEngine;

namespace Keeper244.Game
{
    /// <summary>
    /// Tiny IMGUI toolkit shared by the HUD and the menus: a virtual 1080-pixel-high layout space,
    /// sporty fonts (Bebas Neue for headings and numbers, Barlow Condensed for text), rounded
    /// panels and buttons generated in code. Must only be used inside OnGUI.
    /// </summary>
    public static class Ui
    {
        public const float RefHeight = 1080f;

        public static readonly Color Panel = new Color(0.04f, 0.06f, 0.09f, 0.82f);
        public static readonly Color PanelSoft = new Color(0.04f, 0.06f, 0.09f, 0.55f);
        public static readonly Color PanelLight = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color Text = new Color(0.97f, 0.97f, 0.97f);
        public static readonly Color Muted = new Color(0.66f, 0.71f, 0.77f);
        public static readonly Color Accent = new Color(1f, 0.58f, 0.12f);    // keeper orange
        public static readonly Color Cue = new Color(1f, 0.84f, 0.1f);        // target yellow
        public static readonly Color Good = new Color(0.30f, 0.90f, 0.42f);
        public static readonly Color Bad = new Color(0.97f, 0.30f, 0.30f);
        public static readonly Color Info = new Color(0.45f, 0.78f, 1f);

        /// <summary>Set once at start-up (GameFlow). Null falls back to Unity's default font.</summary>
        public static Font Display;
        public static Font Body;

        static Texture2D white, round, star, fadeDown;
        static readonly Dictionary<string, GUIStyle> cache = new Dictionary<string, GUIStyle>();
        static readonly Dictionary<Color, Texture2D> tinted = new Dictionary<Color, Texture2D>();
        static int cachedHeight = -1;

        public static float Scale => Screen.height / RefHeight;
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

        static Texture2D Round => round != null ? round : (round = ProceduralTextures.RoundedRect(64, 20));
        public static Texture2D StarTex => star != null ? star : (star = ProceduralTextures.Star(96));
        static Texture2D FadeDown => fadeDown != null ? fadeDown : (fadeDown = ProceduralTextures.VerticalFade(0.85f, 0f));

        // ---------- primitives (virtual units) ----------

        public static void Fill(float x, float y, float w, float h, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(R(x, y, w, h), White);
            GUI.color = old;
        }

        public static void Frame(float x, float y, float w, float h, Color c, float t)
        {
            Fill(x, y, w, t, c);
            Fill(x, y + h - t, w, t, c);
            Fill(x, y, t, h, c);
            Fill(x + w - t, y, t, h, c);
        }

        /// <summary>Rounded rectangle (9-sliced so corners stay round at any size).</summary>
        public static void RoundBox(float x, float y, float w, float h, Color c)
        {
            // 9-slicing needs room for both corners; tiny boxes are drawn square
            if (S(Mathf.Min(w, h)) < 42f) { Fill(x, y, w, h, c); return; }
            var old = GUI.color;
            GUI.color = c;
            GUI.Box(R(x, y, w, h), GUIContent.none, RoundStyle());
            GUI.color = old;
        }

        /// <summary>Rounded panel with a coloured accent bar on the left.</summary>
        public static void PanelBox(float x, float y, float w, float h, Color? accent = null)
        {
            RoundBox(x, y, w, h, Panel);
            if (accent.HasValue) RoundBox(x, y, 8f, h, accent.Value);
        }

        /// <summary>Dark-to-clear gradient from the top of the screen (makes the top HUD readable).</summary>
        public static void TopShade(float height)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(R(0, 0, Width, height), FadeDown, ScaleMode.StretchToFill);
            GUI.color = old;
        }

        public static void Star(float x, float y, float size, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(R(x, y, size, size), StarTex);
            GUI.color = old;
        }

        public static void Label(float x, float y, float w, float h, string text, int size, Color color,
                                 TextAnchor align = TextAnchor.UpperLeft, bool display = false)
        {
            GUI.Label(R(x, y, w, h), text, LabelStyle(size, color, align, display));
        }

        /// <summary>Label with a soft drop shadow (for big text over the 3D scene).</summary>
        public static void ShadowLabel(float x, float y, float w, float h, string text, int size, Color color,
                                       TextAnchor align = TextAnchor.UpperCenter, bool display = true)
        {
            var shadow = new Color(0f, 0f, 0f, 0.55f * color.a);
            string plain = System.Text.RegularExpressions.Regex.Replace(text, "</?color[^>]*>", "");
            Label(x + 3, y + 4, w, h, plain, size, shadow, align, display);
            Label(x, y, w, h, text, size, color, align, display);
        }

        // ---------- styles ----------

        public static GUIStyle LabelStyle(int size, Color color, TextAnchor align = TextAnchor.UpperLeft, bool display = false)
        {
            ResetIfResized();
            string key = $"L{size}|{color}|{align}|{display}";
            if (!cache.TryGetValue(key, out var st))
            {
                st = new GUIStyle(GUI.skin.label)
                {
                    font = display ? Display : Body,
                    fontSize = Mathf.Max(8, Mathf.RoundToInt(S(size))),
                    alignment = align,
                    richText = true,
                    wordWrap = true,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(0, 0, 0, 0),
                };
                if (st.font == null) st.fontStyle = display ? FontStyle.Bold : FontStyle.Normal;
                st.normal.textColor = color;
                cache[key] = st;
            }
            return st;
        }

        static GUIStyle RoundStyle()
        {
            ResetIfResized();
            if (!cache.TryGetValue("round", out var st))
            {
                st = new GUIStyle { border = new RectOffset(20, 20, 20, 20) };
                st.normal.background = Round;
                cache["round"] = st;
            }
            return st;
        }

        /// <summary>Rounded button: dark at rest, lighter on hover, orange when pressed.</summary>
        public static GUIStyle ButtonStyle(int size = 26, bool primary = false)
        {
            ResetIfResized();
            string key = $"B{size}|{primary}";
            if (!cache.TryGetValue(key, out var st))
            {
                Color rest = primary ? Accent : new Color(0.16f, 0.2f, 0.26f, 0.95f);
                Color hover = primary ? Color.Lerp(Accent, Color.white, 0.2f) : new Color(0.24f, 0.3f, 0.38f, 0.98f);
                Color press = primary ? Color.Lerp(Accent, Color.black, 0.2f) : Accent;
                st = new GUIStyle
                {
                    font = Display,
                    fontSize = Mathf.RoundToInt(S(size)),
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                    border = new RectOffset(20, 20, 20, 20),
                    padding = new RectOffset((int)S(18), (int)S(18), (int)S(6), (int)S(6)),
                    fixedHeight = S(size * 2.0f),
                };
                st.normal.background = Tint(rest);
                st.hover.background = Tint(hover);
                st.active.background = Tint(press);
                st.normal.textColor = primary ? Color.black : Text;
                st.hover.textColor = primary ? Color.black : Color.white;
                st.active.textColor = Color.black;
                cache[key] = st;
            }
            return st;
        }

        /// <summary>Taller left-aligned button used for the level list.</summary>
        public static GUIStyle CardStyle()
        {
            ResetIfResized();
            if (!cache.TryGetValue("card", out var st))
            {
                st = new GUIStyle(ButtonStyle(28))
                {
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = S(92),
                    padding = new RectOffset((int)S(24), (int)S(24), (int)S(10), (int)S(10)),
                    font = Body,
                    fontSize = Mathf.RoundToInt(S(22)),
                };
                cache["card"] = st;
            }
            return st;
        }

        public static GUIStyle FieldStyle(int size = 28)
        {
            ResetIfResized();
            string key = $"F{size}";
            if (!cache.TryGetValue(key, out var st))
            {
                st = new GUIStyle(GUI.skin.textField)
                {
                    font = Body,
                    fontSize = Mathf.RoundToInt(S(size)),
                    fixedHeight = S(size * 1.8f),
                    alignment = TextAnchor.MiddleLeft,
                    border = new RectOffset(20, 20, 20, 20),
                    padding = new RectOffset((int)S(16), (int)S(16), 0, 0),
                };
                st.normal.background = st.hover.background = Tint(new Color(1f, 1f, 1f, 0.1f));
                st.focused.background = Tint(new Color(1f, 1f, 1f, 0.18f));
                st.normal.textColor = st.hover.textColor = st.focused.textColor = Text;
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
                st = new GUIStyle(GUI.skin.toggle) { font = Body, fontSize = Mathf.RoundToInt(S(size)), richText = true };
                st.normal.textColor = st.onNormal.textColor = st.hover.textColor = st.onHover.textColor = Text;
                cache[key] = st;
            }
            return st;
        }

        public static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString();

        /// <summary>A rounded-rect texture pre-tinted with a colour (IMGUI can't tint per button state).</summary>
        static Texture2D Tint(Color c)
        {
            if (tinted.TryGetValue(c, out var t) && t != null) return t;
            var src = Round.GetPixels32();
            var dst = new Color32[src.Length];
            Color32 cc = c;
            for (int i = 0; i < src.Length; i++)
                dst[i] = new Color32(cc.r, cc.g, cc.b, (byte)(src[i].a * cc.a / 255));
            t = new Texture2D(Round.width, Round.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(dst);
            t.Apply(false);
            tinted[c] = t;
            return t;
        }

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
