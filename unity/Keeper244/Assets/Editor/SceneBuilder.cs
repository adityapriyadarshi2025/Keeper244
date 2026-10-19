using System.IO;
using Keeper244.Controls;
using Keeper244.Core;
using Keeper244.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Keeper244.EditorTools
{
    /// <summary>
    /// Menu: Keeper244 → Build Main Scene.
    /// Builds Assets/Scenes/Main.unity from code with real pitch dimensions, creates the materials
    /// and the L1–L3 LevelConfig assets if missing, wires every reference, and sets the build list.
    /// Rebuilding is one click, so the scene never needs hand-merging in git.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MaterialsDir = "Assets/Materials";
        const string LevelsDir = "Assets/Config/Levels";

        const float LineWidth = 0.12f;
        const float LineY = 0.006f;
        const float PostSize = 0.12f;
        const float KeeperZ = 0.45f;
        const float MarkerZ = 1.6f;
        const float StrikerZ = 12.4f;

        [MenuItem("Keeper244/Build Main Scene", priority = 0)]
        public static void BuildMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Keeper244", "Main.unity already exists. Rebuild it from code?\n\nAny manual edits to the scene will be lost (assets and materials are kept).", "Rebuild", "Cancel"))
                return;

            // New scene FIRST: switching scenes unloads unused assets, which would kill references to
            // ScriptableObjects created before the switch (the level would be saved as "None").
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets", "Config");
            EnsureFolder("Assets/Config", "Levels");

            LoadOrCreateLevel("L0_FreePlay", "L0", "L0 Free Play", 5, 1.5f, 1.2f, freePlay: true);
            LoadOrCreateLevel("L1_ThreeZones", "L1", "L1 Three Zones", 3, 1.5f, 1.2f);
            LoadOrCreateLevel("L2_FiveZones", "L2", "L2 Five Zones", 5, 1.5f, 1.2f);
            LoadOrCreateLevel("L3_Pro", "L3", "L3 Pro", 5, 0.9f, 0.8f);

            var grass = Mat("Pitch", new Color(0.20f, 0.52f, 0.22f));
            var lines = Mat("Lines", new Color(0.95f, 0.95f, 0.95f), unlit: true);
            var goalWhite = Mat("GoalFrame", Color.white);
            var keeperMat = Mat("Keeper", new Color(0.95f, 0.55f, 0.10f));
            var gloveMat = Mat("Gloves", new Color(0.85f, 0.95f, 0.20f));
            var strikerMat = Mat("Striker", new Color(0.15f, 0.35f, 0.85f));
            var ballMat = Mat("Ball", new Color(0.97f, 0.97f, 0.97f));
            var markerMat = Mat("ZoneMarker", new Color(1f, 0.85f, 0.1f), unlit: true);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);

            // ---------- Light + camera ----------
            var sun = new GameObject("Sun");
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.53f, 0.74f, 0.92f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();
            // Behind and above the keeper, fixed (brief §3.8): screen-right = keeper's right.
            camGo.transform.position = new Vector3(0f, 3.0f, -5.0f);
            camGo.transform.LookAt(new Vector3(0f, 1.0f, 7.0f));

            // ---------- Pitch + markings (FIFA dimensions) ----------
            var env = new GameObject("Environment").transform;
            Prim(PrimitiveType.Plane, "Pitch", new Vector3(0f, 0f, 10f), new Vector3(6f, 1f, 4f), grass, env); // 60 × 40 m

            var markings = new GameObject("Markings").transform;
            markings.SetParent(env, false);
            Line(markings, lines, "GoalLine", 0f, 0f, 60f, alongX: true);
            // Goal area: 18.32 × 5.5 m
            Line(markings, lines, "GoalArea_Front", 0f, 5.5f, 18.32f, alongX: true);
            Line(markings, lines, "GoalArea_Left", -9.16f, 2.75f, 5.5f, alongX: false);
            Line(markings, lines, "GoalArea_Right", 9.16f, 2.75f, 5.5f, alongX: false);
            // Penalty area: 40.32 × 16.5 m
            Line(markings, lines, "PenaltyArea_Front", 0f, 16.5f, 40.32f, alongX: true);
            Line(markings, lines, "PenaltyArea_Left", -20.16f, 8.25f, 16.5f, alongX: false);
            Line(markings, lines, "PenaltyArea_Right", 20.16f, 8.25f, 16.5f, alongX: false);
            Prim(PrimitiveType.Cylinder, "PenaltySpot", new Vector3(0f, LineY, FieldGeometry.PenaltyDistance), new Vector3(0.22f, 0.005f, 0.22f), lines, markings);

            // ---------- Goal: 7.32 × 2.44 m inside the posts ----------
            var goal = new GameObject("Goal").transform;
            float halfW = FieldGeometry.GoalWidth / 2f;
            float postH = FieldGeometry.GoalHeight + PostSize;
            Prim(PrimitiveType.Cube, "Post_Left", new Vector3(-(halfW + PostSize / 2f), postH / 2f, -PostSize / 2f), new Vector3(PostSize, postH, PostSize), goalWhite, goal);
            Prim(PrimitiveType.Cube, "Post_Right", new Vector3(halfW + PostSize / 2f, postH / 2f, -PostSize / 2f), new Vector3(PostSize, postH, PostSize), goalWhite, goal);
            Prim(PrimitiveType.Cube, "Crossbar", new Vector3(0f, FieldGeometry.GoalHeight + PostSize / 2f, -PostSize / 2f), new Vector3(FieldGeometry.GoalWidth + 2f * PostSize, PostSize, PostSize), goalWhite, goal);

            // ---------- Keeper (pivot at the feet so the lean tips the body) ----------
            var players = new GameObject("Players").transform;
            var keeperRoot = new GameObject("Keeper");
            keeperRoot.transform.SetParent(players, false);
            keeperRoot.transform.position = new Vector3(0f, 0f, KeeperZ);
            float kh = FieldGeometry.KeeperHeight;
            Prim(PrimitiveType.Capsule, "Body", new Vector3(0f, kh / 2f, KeeperZ), new Vector3(0.7f, kh / 2f, 0.5f), keeperMat, keeperRoot.transform);
            Prim(PrimitiveType.Cube, "Arms", new Vector3(0f, 1.45f, KeeperZ), new Vector3(1.8f, 0.12f, 0.14f), keeperMat, keeperRoot.transform);
            Prim(PrimitiveType.Sphere, "Glove_Left", new Vector3(-0.95f, 1.45f, KeeperZ), Vector3.one * 0.22f, gloveMat, keeperRoot.transform);
            Prim(PrimitiveType.Sphere, "Glove_Right", new Vector3(0.95f, 1.45f, KeeperZ), Vector3.one * 0.22f, gloveMat, keeperRoot.transform);
            var keeper = keeperRoot.AddComponent<KeeperController>();

            // ---------- Striker (faces the goal) ----------
            var strikerRoot = new GameObject("Striker");
            strikerRoot.transform.SetParent(players, false);
            strikerRoot.transform.SetPositionAndRotation(new Vector3(0f, 0f, StrikerZ), Quaternion.LookRotation(Vector3.back));
            Prim(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.9f, StrikerZ), new Vector3(0.6f, 0.9f, 0.45f), strikerMat, strikerRoot.transform);
            var striker = strikerRoot.AddComponent<StrikerController>();

            // ---------- Ball on the penalty spot ----------
            float r = FieldGeometry.BallDiameter / 2f;
            var ballGo = Prim(PrimitiveType.Sphere, "Ball", new Vector3(0f, r, FieldGeometry.PenaltyDistance), Vector3.one * FieldGeometry.BallDiameter, ballMat, players);
            var ball = ballGo.AddComponent<BallController>();

            // ---------- Target zone marker ----------
            var markerGo = Prim(PrimitiveType.Cube, "ZoneMarker", new Vector3(0f, 0.012f, MarkerZ), new Vector3(2.3f, 0.02f, 0.6f), markerMat, env);
            var marker = markerGo.AddComponent<ZoneMarker>();
            markerGo.GetComponent<Renderer>().enabled = false;
            markerGo.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // ---------- Game logic ----------
            var gameGo = new GameObject("Game");
            var input = gameGo.AddComponent<KeyboardInput>();
            var gm = gameGo.AddComponent<GameManager>();
            var flow = gameGo.AddComponent<GameFlow>();
            var hud = gameGo.AddComponent<HudOverlay>();
            var debugPanel = gameGo.AddComponent<DebugStatusOverlay>();
            gameGo.AddComponent<AudioSource>();
            var sfx = gameGo.AddComponent<AudioFeedback>();

            var so = new SerializedObject(gm);
            so.FindProperty("keeper").objectReferenceValue = keeper;
            so.FindProperty("ball").objectReferenceValue = ball;
            so.FindProperty("striker").objectReferenceValue = striker;
            so.FindProperty("marker").objectReferenceValue = marker;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Fresh handles to the level assets (see the note at NewScene).
            string[] levelFiles = { "L0_FreePlay", "L1_ThreeZones", "L2_FiveZones", "L3_Pro" };
            var soFlow = new SerializedObject(flow);
            soFlow.FindProperty("game").objectReferenceValue = gm;
            soFlow.FindProperty("keyboard").objectReferenceValue = input;
            var levelsProp = soFlow.FindProperty("levels");
            levelsProp.arraySize = levelFiles.Length;
            for (int i = 0; i < levelFiles.Length; i++)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<LevelConfig>($"{LevelsDir}/{levelFiles[i]}.asset");
                levelsProp.GetArrayElementAtIndex(i).objectReferenceValue = cfg;
                if (cfg == null) Debug.LogError($"[Keeper244] SceneBuilder: level asset {levelFiles[i]} missing.");
            }
            soFlow.ApplyModifiedPropertiesWithoutUndo();

            Wire(hud, ("game", gm), ("flow", flow));
            Wire(debugPanel, ("game", gm));
            Wire(sfx, ("game", gm));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = gameGo;
            Debug.Log($"[Keeper244] Built {ScenePath}. Press Play, pick a level in the menu. A/D = near, Shift = far, Esc = pause.");
        }

        // ---------- helpers ----------

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
                AssetDatabase.CreateFolder(parent, name);
        }

        static void Wire(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
                so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static LevelConfig LoadOrCreateLevel(string fileName, string id, string displayName, int zones, float cue, float flight, bool freePlay = false)
        {
            string path = $"{LevelsDir}/{fileName}.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<LevelConfig>(path);
            if (cfg != null) return cfg; // keep any tuning already done in the Inspector

            cfg = ScriptableObject.CreateInstance<LevelConfig>();
            cfg.settings = new LevelSettings
            {
                levelId = id,
                levelName = displayName,
                zones = zones,
                freePlay = freePlay,
                cueSeconds = cue,
                flightSeconds = flight,
                feedbackSeconds = 1.5f,
                restGateSeconds = 0.5f,
                shotsPerTarget = 3,
                unlockSaveFraction = 0.7f,
                randomSeed = 0,
            };
            AssetDatabase.CreateAsset(cfg, path);
            return cfg;
        }

        static Material Mat(string name, Color color, bool unlit = false)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m; // keep colours tweaked by hand

            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Prim(PrimitiveType type, string name, Vector3 worldPos, Vector3 scale, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // results are logical, no physics needed
            return go;
        }

        static void Line(Transform parent, Material mat, string name, float x, float z, float length, bool alongX)
        {
            var scale = alongX ? new Vector3(length, 0.01f, LineWidth) : new Vector3(LineWidth, 0.01f, length);
            var go = Prim(PrimitiveType.Cube, name, new Vector3(x, LineY, z), scale, mat, parent);
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
