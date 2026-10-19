using Keeper244.Core;
using Keeper244.Game;
using UnityEngine;

namespace Keeper244.Look
{
    /// <summary>
    /// Puts the generated textures on the stadium at start-up and keeps the small animations
    /// going: scrolling LED boards and a bouncing crowd after a save.
    /// </summary>
    public sealed class StadiumDresser : MonoBehaviour
    {
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        [SerializeField] GameManager game;

        [Header("Pitch")]
        [SerializeField] Renderer pitch;
        [SerializeField] Color grassLight = new Color(0.30f, 0.58f, 0.25f);
        [SerializeField] Color grassDark = new Color(0.24f, 0.49f, 0.21f);
        [Tooltip("Texture repeats across (x) and along (z) the pitch. 10 along 105 m = 5.25 m mowing bands.")]
        [SerializeField] Vector2 pitchTiling = new Vector2(6f, 10f);

        [Header("Goal nets (quads)")]
        [SerializeField] Renderer[] nets = new Renderer[0];
        [Tooltip("Metres covered by one net texture tile (8 meshes).")]
        [SerializeField] float netTileMetres = 1.0f;

        [Header("Crowd (stand boxes, crowd on the top face)")]
        [SerializeField] Renderer[] crowd = new Renderer[0];
        [SerializeField] Color homeColour = new Color(0.95f, 0.5f, 0.1f);
        [SerializeField] Color awayColour = new Color(0.15f, 0.33f, 0.8f);
        [Tooltip("Metres of stand covered by one crowd tile (≈ 32 fans × 8 rows).")]
        [SerializeField] Vector2 crowdTileMetres = new Vector2(15f, 6.5f);

        [Header("LED boards (quads)")]
        [SerializeField] Renderer[] ledBoards = new Renderer[0];
        [SerializeField] string[] ledMessages = { "KEEPER244 * ES244 SSRP * TEAM 20 * IIT GANDHINAGAR * " , "SAVE IT WITH YOUR MUSCLES * EMG POWERED * " };
        [SerializeField] Color[] ledColours = { new Color(1f, 0.6f, 0.15f), new Color(0.35f, 0.75f, 1f) };
        [Tooltip("Scroll speed, metres per second.")]
        [SerializeField] float ledScrollSpeed = 2.5f;

        [Header("Ball")]
        [SerializeField] Renderer ball;

        Material[] ledMats = new Material[0];
        float[] ledTileMetres = new float[0];
        Material[] crowdMats = new Material[0];
        Vector2[] crowdBaseOffsets = new Vector2[0];
        float cheerT = -1f;

        void Awake()
        {
            if (pitch != null)
            {
                var m = pitch.material;
                m.SetTexture(BaseMap, ProceduralTextures.PitchStripes(grassLight, grassDark));
                m.SetTextureScale(BaseMap, pitchTiling);
            }

            if (nets.Length > 0)
            {
                var net = ProceduralTextures.NetGrid();
                foreach (var r in nets)
                {
                    if (r == null) continue;
                    var m = r.material;
                    m.SetTexture(BaseMap, net);
                    var s = r.transform.lossyScale;
                    m.SetTextureScale(BaseMap, new Vector2(Mathf.Abs(s.x) / netTileMetres, Mathf.Abs(s.y) / netTileMetres));
                }
            }

            crowdMats = new Material[crowd.Length];
            crowdBaseOffsets = new Vector2[crowd.Length];
            for (int i = 0; i < crowd.Length; i++)
            {
                if (crowd[i] == null) continue;
                var m = crowd[i].material;
                m.SetTexture(BaseMap, ProceduralTextures.Crowd(homeColour * 0.9f, awayColour * 0.9f, 17 + i));
                var s = crowd[i].transform.lossyScale; // box: x = along the stand, z = up the slope
                m.SetTextureScale(BaseMap, new Vector2(Mathf.Abs(s.x) / crowdTileMetres.x, Mathf.Abs(s.z) / crowdTileMetres.y));
                crowdMats[i] = m;
                crowdBaseOffsets[i] = m.GetTextureOffset(BaseMap);
            }

            ledMats = new Material[ledBoards.Length];
            ledTileMetres = new float[ledBoards.Length];
            var ledTex = new Texture2D[ledMessages.Length];
            for (int i = 0; i < ledMessages.Length; i++)
                ledTex[i] = ProceduralTextures.LedText(ledMessages[i], ledColours[i % ledColours.Length], new Color(0.06f, 0.06f, 0.08f));
            for (int i = 0; i < ledBoards.Length; i++)
            {
                if (ledBoards[i] == null || ledTex.Length == 0) continue;
                var tex = ledTex[i % ledTex.Length];
                var m = ledBoards[i].material;
                m.SetTexture(BaseMap, tex);
                var s = ledBoards[i].transform.lossyScale; // quad: x = length, y = height
                float tileWidth = Mathf.Abs(s.y) * tex.width / tex.height; // keep the dots square
                ledTileMetres[i] = tileWidth;
                m.SetTextureScale(BaseMap, new Vector2(Mathf.Abs(s.x) / tileWidth, 1f));
                m.SetTextureOffset(BaseMap, new Vector2(i * 0.37f, 0f));
                ledMats[i] = m;
            }

            if (ball != null)
                ball.material.SetTexture(BaseMap, ProceduralTextures.BallPattern());
        }

        void OnEnable()
        {
            if (game != null) game.ShotResolved += OnShot;
        }

        void OnDisable()
        {
            if (game != null) game.ShotResolved -= OnShot;
        }

        void OnShot(ShotResult r)
        {
            if (r.Saved) cheerT = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < ledMats.Length; i++)
            {
                if (ledMats[i] == null || ledTileMetres[i] <= 0f) continue;
                var o = ledMats[i].GetTextureOffset(BaseMap);
                o.x = Mathf.Repeat(o.x + ledScrollSpeed * dt / ledTileMetres[i], 1f);
                ledMats[i].SetTextureOffset(BaseMap, o);
            }

            if (cheerT >= 0f)
            {
                cheerT += dt;
                float k = Mathf.Clamp01(1f - cheerT / 1.8f);
                for (int i = 0; i < crowdMats.Length; i++)
                {
                    if (crowdMats[i] == null) continue;
                    float jump = 0.015f * Mathf.Abs(Mathf.Sin(cheerT * 11f + i)) * k;
                    crowdMats[i].SetTextureOffset(BaseMap, crowdBaseOffsets[i] + new Vector2(0f, jump));
                }
                if (k <= 0f) cheerT = -1f;
            }
        }
    }
}
