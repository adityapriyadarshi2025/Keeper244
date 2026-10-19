using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Every texture in the game is generated in code at start-up: no image files in git, nothing
    /// to download, identical on Mac and Windows. Each function returns a ready-to-use Texture2D.
    /// </summary>
    public static class ProceduralTextures
    {
        static Texture2D New(int w, int h, bool mips, TextureWrapMode wrap, string name)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mips)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.DontSave,
            };
            return t;
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>One light and one dark mowing band with grass grain. Tile it along the pitch length.</summary>
        public static Texture2D PitchStripes(Color light, Color dark, int size = 256)
        {
            var t = New(size, size, true, TextureWrapMode.Repeat, "PitchStripes");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                // soft edge between the two bands
                float v = (float)y / size;
                float band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.47f, 0.53f, v)) - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.97f, 1.0f, v));
                Color baseCol = Color.Lerp(light, dark, Mathf.Clamp01(band));
                for (int x = 0; x < size; x++)
                {
                    float n = Hash(x, y, 1) * 0.6f + Hash(x / 4, y / 4, 2) * 0.4f;
                    float g = 0.92f + 0.16f * n;
                    px[y * size + x] = (Color)new Color(baseCol.r * g, baseCol.g * g, baseCol.b * g, 1f);
                }
            }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>White net strings on transparent; used with alpha clipping. `cells` cells per side.</summary>
        public static Texture2D NetGrid(int size = 128, int cells = 8, int line = 2)
        {
            var t = New(size, size, true, TextureWrapMode.Repeat, "NetGrid");
            var px = new Color32[size * size];
            int cell = size / cells;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // diamond mesh: lines along both diagonals of each cell
                    int a = (x + y) % cell, b = (x - y + size * 4) % cell;
                    bool on = a < line || b < line;
                    px[y * size + x] = on ? new Color32(240, 240, 240, 255) : new Color32(255, 255, 255, 0);
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>Classic 32-panel ball (truncated icosahedron) on the sphere primitive's UV layout.</summary>
        public static Texture2D BallPattern(int width = 512)
        {
            int height = width / 2;
            var t = New(width, height, true, TextureWrapMode.Clamp, "BallPattern");
            var px = new Color32[width * height];

            // 12 pentagon centres = icosahedron vertices; 20 hexagon centres = icosahedron face centres.
            float phi = (1f + Mathf.Sqrt(5f)) / 2f;
            var pent = new[]
            {
                new Vector3(-1, phi, 0), new Vector3(1, phi, 0), new Vector3(-1, -phi, 0), new Vector3(1, -phi, 0),
                new Vector3(0, -1, phi), new Vector3(0, 1, phi), new Vector3(0, -1, -phi), new Vector3(0, 1, -phi),
                new Vector3(phi, 0, -1), new Vector3(phi, 0, 1), new Vector3(-phi, 0, -1), new Vector3(-phi, 0, 1),
            };
            for (int i = 0; i < pent.Length; i++) pent[i] = pent[i].normalized;
            var hex = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 12; i++)
                for (int j = i + 1; j < 12; j++)
                    for (int k = j + 1; k < 12; k++)
                    {
                        float e = 1.2f; // adjacent icosahedron vertices are ~1.05 apart (unit sphere)
                        if (Vector3.Distance(pent[i], pent[j]) < e && Vector3.Distance(pent[j], pent[k]) < e && Vector3.Distance(pent[i], pent[k]) < e)
                            hex.Add((pent[i] + pent[j] + pent[k]).normalized);
                    }

            for (int y = 0; y < height; y++)
            {
                float lat = ((y + 0.5f) / height - 0.5f) * Mathf.PI;
                for (int x = 0; x < width; x++)
                {
                    float lon = (x + 0.5f) / width * 2f * Mathf.PI;
                    var d = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));

                    // nearest centre, pentagons slightly "smaller" so their edges sit where a real ball's do
                    float best = 9f, second = 9f;
                    bool bestIsPent = false;
                    foreach (var c in pent)
                    {
                        float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, c), -1f, 1f)) + 0.045f;
                        if (a < best) { second = best; best = a; bestIsPent = true; }
                        else if (a < second) second = a;
                    }
                    foreach (var c in hex)
                    {
                        float a = Mathf.Acos(Mathf.Clamp(Vector3.Dot(d, c), -1f, 1f));
                        if (a < best) { second = best; best = a; bestIsPent = false; }
                        else if (a < second) second = a;
                    }
                    bool seam = second - best < 0.018f;
                    Color32 c32 = seam ? new Color32(70, 70, 75, 255)
                        : bestIsPent ? new Color32(25, 25, 30, 255)
                        : new Color32(245, 245, 245, 255);
                    px[y * width + x] = c32;
                }
            }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>Rows of fans (heads + shirts) on dark seats, seen from a distance. Tile it over the stands.</summary>
        public static Texture2D Crowd(Color teamA, Color teamB, int seed, int width = 256, int height = 128, int rows = 8)
        {
            var t = New(width, height, true, TextureWrapMode.Repeat, "Crowd");
            var px = new Color32[width * height];
            var seat = new Color(0.16f, 0.18f, 0.24f);
            for (int i = 0; i < px.Length; i++)
            {
                int y = i / width;
                float stripe = (y % (height / rows)) < 2 ? 0.75f : 1f; // step edge between rows
                px[i] = (Color)(seat * stripe);
            }

            var skins = new[] { new Color(0.96f, 0.80f, 0.69f), new Color(0.87f, 0.67f, 0.52f), new Color(0.72f, 0.52f, 0.38f), new Color(0.50f, 0.35f, 0.25f) };
            var others = new[] { Color.white, new Color(0.9f, 0.9f, 0.2f), new Color(0.2f, 0.25f, 0.3f), new Color(0.8f, 0.15f, 0.2f), new Color(0.3f, 0.7f, 0.4f) };
            int rowH = height / rows;
            var rng = new System.Random(seed);
            for (int r = 0; r < rows; r++)
            {
                int baseY = r * rowH + 2;
                int x = rng.Next(0, 4);
                while (x < width)
                {
                    int w = 5 + rng.Next(0, 3);
                    if (rng.NextDouble() > 0.08) // 8 % empty seats
                    {
                        double p = rng.NextDouble();
                        Color shirt = p < 0.45 ? teamA : p < 0.75 ? teamB : others[rng.Next(others.Length)];
                        shirt *= 0.8f + 0.25f * (float)rng.NextDouble();
                        Color skin = skins[rng.Next(skins.Length)];
                        int shirtH = rowH - 7;
                        // shirt (body)
                        for (int yy = baseY; yy < baseY + shirtH; yy++)
                            for (int xx = x; xx < x + w && xx < width; xx++)
                                px[yy * width + xx] = (Color)shirt;
                        // head
                        int cx = x + w / 2, cy = baseY + shirtH + 2;
                        for (int yy = cy - 2; yy <= cy + 2; yy++)
                            for (int xx = cx - 2; xx <= cx + 2; xx++)
                                if (xx >= 0 && xx < width && yy < height && (xx - cx) * (xx - cx) + (yy - cy) * (yy - cy) <= 5)
                                    px[yy * width + xx] = (Color)skin;
                    }
                    x += w + 1 + rng.Next(0, 3);
                }
            }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>LED board: text in 5×7 dots, each dot a lit square with a dark gap. Repeat-wrapped for scrolling.</summary>
        public static Texture2D LedText(string text, Color on, Color off, int dot = 4)
        {
            int cols = PixelFont.Measure(text);
            int rows = PixelFont.Height + 2;
            int w = Mathf.Min(8192, cols * dot), h = rows * dot;
            var t = New(w, h, true, TextureWrapMode.Repeat, "LedText");
            var px = new Color32[w * h];
            var dark = new Color(0.02f, 0.02f, 0.03f);
            for (int r = 0; r < rows; r++)
            {
                int glyphRow = PixelFont.Height - 1 - (r - 1); // texture y grows upwards
                for (int c = 0; c < cols && c * dot < w; c++)
                {
                    int gi = c / (PixelFont.Width + 1), gc = c % (PixelFont.Width + 1);
                    bool lit = r >= 1 && r <= PixelFont.Height && gc < PixelFont.Width && gi < text.Length
                               && PixelFont.Lit(text[gi], gc, glyphRow);
                    Color col = lit ? on : off;
                    for (int yy = 0; yy < dot; yy++)
                        for (int xx = 0; xx < dot; xx++)
                        {
                            bool gap = xx == dot - 1 || yy == dot - 1;
                            px[(r * dot + yy) * w + c * dot + xx] = (Color)(gap ? dark : col);
                        }
                }
            }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>White rounded rectangle with soft edges, for 9-sliced UI panels (border = radius).</summary>
        public static Texture2D RoundedRect(int size = 64, int radius = 18)
        {
            var t = New(size, size, false, TextureWrapMode.Clamp, "RoundedRect");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }

        /// <summary>Soft round dot (particles, glows).</summary>
        public static Texture2D SoftDot(int size = 64)
        {
            var t = New(size, size, true, TextureWrapMode.Clamp, "SoftDot");
            var px = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        /// <summary>Five-pointed star, white with soft edge (results screen).</summary>
        public static Texture2D Star(int size = 96)
        {
            var t = New(size, size, true, TextureWrapMode.Clamp, "Star");
            var px = new Color32[size * size];
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float rad = (i % 2 == 0 ? 0.48f : 0.2f) * size;
                pts[i] = new Vector2(size / 2f + Mathf.Cos(ang) * rad, size / 2f + Mathf.Sin(ang) * rad);
            }
            const int ss = 3; // supersampling for smooth edges
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                            if (InPolygon(new Vector2(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss), pts)) inside++;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (ss * ss)));
                }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }

        static bool InPolygon(Vector2 p, Vector2[] poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    c = !c;
            return c;
        }

        /// <summary>Vertical alpha gradient (top → bottom), white. For screen-edge shading behind UI.</summary>
        public static Texture2D VerticalFade(float topAlpha, float bottomAlpha, int height = 64)
        {
            var t = New(1, height, false, TextureWrapMode.Clamp, "Fade");
            var px = new Color32[height];
            for (int y = 0; y < height; y++)
            {
                float a = Mathf.Lerp(bottomAlpha, topAlpha, (float)y / (height - 1));
                px[y] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            t.SetPixels32(px);
            t.Apply(false);
            return t;
        }
    }
}
