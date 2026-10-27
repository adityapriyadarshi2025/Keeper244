using System.Collections.Generic;
using System.IO;
using Keeper244.Controls;
using Keeper244.Core;
using Keeper244.Game;
using Keeper244.Look;
using Keeper244.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Keeper244.EditorTools.BuildKit;

namespace Keeper244.EditorTools
{
    /// <summary>
    /// Menu: Keeper244 → Build Main Scene.
    /// Builds Assets/Scenes/Main.unity from code: a floodlit stadium at real FIFA dimensions, both
    /// goals with nets, animated keeper and striker, effects, post-processing, and all game
    /// components wired. Rebuilding is one click, so the scene never needs hand-merging in git.
    /// Textures (grass stripes, crowd, LED boards, net, ball) are generated at runtime.
    /// </summary>
    public static class SceneBuilder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string LevelsDir = "Assets/Config/Levels";
        const string PostFxPath = "Assets/Config/PostFX.asset";
        const string FontsDir = "Assets/Fonts";

        const float PitchLength = 105f;
        const float HalfWidth = 34f;
        const float KeeperZ = 0.45f;
        const float MarkerZ = 1.6f;
        const float PostSize = 0.12f;
        const float NetDepth = 1.9f;

        [MenuItem("Keeper244/Build Main Scene", priority = 0)]
        public static void BuildMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Keeper244", "Main.unity already exists. Rebuild it from code?\n\nAny manual edits to the scene will be lost (level assets are kept).", "Rebuild", "Cancel"))
                return;

            // New scene FIRST: switching scenes unloads unused assets, which would kill references to
            // ScriptableObjects created before the switch (they would be saved as "None").
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var (parent, name) in new[] { ("Assets", "Scenes"), ("Assets", "Materials"), ("Assets", "Config"), ("Assets/Config", "Levels") })
                if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);

            LoadOrCreateLevel("L0_FreePlay", "L0", "L0 Free Play", 5, 1.5f, 1.2f, freePlay: true);
            LoadOrCreateLevel("L1_ThreeZones", "L1", "L1 Three Zones", 3, 1.5f, 1.2f);
            LoadOrCreateLevel("L2_FiveZones", "L2", "L2 Five Zones", 5, 1.5f, 1.2f);
            LoadOrCreateLevel("L3_Pro", "L3", "L3 Pro", 5, 0.9f, 0.8f);

            // ---------------- materials ----------------
            var grass = Mat("PitchGrass", Shading.Lit, Color.white, 0.12f);
            var surround = Mat("Surround", Shading.Lit, new Color(0.17f, 0.35f, 0.16f), 0.08f);
            var lines = Mat("Lines", Shading.Unlit, new Color(0.92f, 0.94f, 0.92f));
            var goalWhite = Mat("GoalFrame", Shading.Lit, Color.white, 0.65f);
            var net = Mat("Net", Shading.UnlitCutoutDoubleSided, new Color(0.92f, 0.92f, 0.92f));
            var crowdMat = Mat("Crowd", Shading.Lit, Color.white, 0.0f);
            var concrete = Mat("Concrete", Shading.Lit, new Color(0.40f, 0.41f, 0.44f), 0.1f);
            var dark = Mat("StandDark", Shading.Lit, new Color(0.10f, 0.11f, 0.13f), 0.2f);
            var glow = Mat("FloodGlow", Shading.Unlit, new Color(6f, 5.6f, 4.8f));
            var roofStrip = Mat("RoofLights", Shading.Unlit, new Color(3.2f, 3.1f, 2.9f));
            var boardBack = Mat("BoardBack", Shading.Lit, new Color(0.05f, 0.05f, 0.06f), 0.4f);
            var led = Mat("Led", Shading.Unlit, new Color(1.7f, 1.7f, 1.7f));
            var pole = Mat("Pole", Shading.Lit, new Color(0.55f, 0.57f, 0.6f), 0.5f, 0.6f);
            var flag = Mat("Flag", Shading.Lit, new Color(1f, 0.85f, 0.1f), 0.2f);
            var ballMat = Mat("Ball", Shading.Lit, Color.white, 0.55f);
            var markerMat = Mat("ZoneMarker", Shading.Unlit, new Color(1f, 0.85f, 0.1f));
            var trailMat = Mat("Trail", Shading.Sprite, new Color(1f, 1f, 1f, 1f));
            var particleMat = Mat("Particles", Shading.Sprite, Color.white);

            var skin1 = Mat("Skin1", Shading.Lit, new Color(0.86f, 0.66f, 0.50f), 0.3f);
            var skin2 = Mat("Skin2", Shading.Lit, new Color(0.60f, 0.43f, 0.31f), 0.3f);
            var hair = Mat("Hair", Shading.Lit, new Color(0.08f, 0.06f, 0.05f), 0.35f);
            var bootsBlack = Mat("BootsBlack", Shading.Lit, new Color(0.06f, 0.06f, 0.07f), 0.6f);
            var bootsWhite = Mat("BootsWhite", Shading.Lit, new Color(0.92f, 0.92f, 0.94f), 0.6f);
            var keeperKit = new Kit
            {
                jersey = Mat("KeeperJersey", Shading.Lit, new Color(1f, 0.52f, 0.08f), 0.35f),
                shorts = Mat("KeeperShorts", Shading.Lit, new Color(0.07f, 0.07f, 0.09f), 0.3f),
                socks = Mat("KeeperSocks", Shading.Lit, new Color(1f, 0.52f, 0.08f), 0.3f),
                skin = skin1, hair = hair, boots = bootsBlack,
                hands = Mat("Gloves", Shading.Lit, new Color(0.82f, 0.95f, 0.28f), 0.4f),
            };
            var strikerKit = new Kit
            {
                jersey = Mat("StrikerJersey", Shading.Lit, new Color(0.12f, 0.30f, 0.82f), 0.35f),
                shorts = Mat("StrikerShorts", Shading.Lit, new Color(0.95f, 0.95f, 0.97f), 0.3f),
                socks = Mat("StrikerSocks", Shading.Lit, new Color(0.12f, 0.30f, 0.82f), 0.3f),
                skin = skin2, hair = hair, boots = bootsWhite, hands = skin2,
            };

            // ---------------- sky, sun, fog ----------------
            var sky = Skybox("Sky");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.60f, 0.67f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.47f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.22f, 0.18f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.fogStartDistance = 90f;
            RenderSettings.fogEndDistance = 320f;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.88f, 0.75f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            sunGo.transform.rotation = Quaternion.Euler(32f, -32f, 0f);
            RenderSettings.sun = sun;

            // ---------------- camera + post-processing ----------------
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 4.0f, -5.25f);          // CameraDirector's behind-the-goal view (editor preview)
            camGo.transform.LookAt(new Vector3(0f, 0.6f, 6.0f));
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            var director = camGo.AddComponent<CameraDirector>();

            var fxGo = new GameObject("PostFX");
            var volume = fxGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.sharedProfile = PostFxProfile(PostFxPath);

            // ---------------- pitch ----------------
            var stadium = Group("Stadium");
            var ground = Group("Ground", stadium);
            Prim(PrimitiveType.Plane, "Surround", new Vector3(0f, -0.01f, PitchLength / 2f), new Vector3(16f, 1f, 22f), surround, ground);
            var pitch = Prim(PrimitiveType.Plane, "Pitch", new Vector3(0f, 0f, PitchLength / 2f), new Vector3(2f * HalfWidth / 10f, 1f, PitchLength / 10f), grass, ground);
            BuildMarkings(Group("Markings", ground), lines);

            // ---------------- goals ----------------
            var nets = new List<Renderer>();
            var homeBackParts = new List<Renderer>(); // hidden by the camera when it is behind this goal
            var homeGoal = BuildGoal(stadium, "HomeGoal", 0f, 0f, goalWhite, net, nets, homeBackParts, out var homeNet);
            BuildGoal(stadium, "FarGoal", PitchLength, 180f, goalWhite, net, nets, null, out _);

            // ---------------- stands, boards, lights ----------------
            var crowd = new List<Renderer>();
            var stands = Group("Stands", stadium);
            crowd.Add(Stand(stands, "Stand_Left", new Vector3(-42f, 0f, 54f), Vector3.left, 152f, crowdMat, concrete, dark, roofStrip));
            crowd.Add(Stand(stands, "Stand_Right", new Vector3(42f, 0f, 54f), Vector3.right, 152f, crowdMat, concrete, dark, roofStrip));
            crowd.Add(Stand(stands, "Stand_Home", new Vector3(0f, 0f, -12f), Vector3.back, 84f, crowdMat, concrete, dark, roofStrip));
            crowd.Add(Stand(stands, "Stand_Far", new Vector3(0f, 0f, PitchLength + 12f), Vector3.forward, 84f, crowdMat, concrete, dark, roofStrip));
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -24f, PitchLength + 24f })
                    Prim(PrimitiveType.Cube, "CornerBlock", new Vector3(sx * 54f, 11f, sz), new Vector3(24f, 22f, 24f), dark, stands);

            var leds = new List<Renderer>();
            var boards = Group("LedBoards", stadium);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float cz in new[] { 19f, 52.5f, 86f })
                    leds.Add(Board(boards, new Vector3(sx * 36.5f, 0f, cz), new Vector3(-sx, 0f, 0f), 32f, led, boardBack));
            foreach (float cx in new[] { -18f, 18f })
            {
                leds.Add(Board(boards, new Vector3(cx, 0f, -4f), Vector3.forward, 24f, led, boardBack));
                leds.Add(Board(boards, new Vector3(cx, 0f, PitchLength + 4f), Vector3.back, 24f, led, boardBack));
            }

            var lightsGrp = Group("Floodlights", stadium);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -30f, PitchLength + 30f })
                    Floodlight(lightsGrp, new Vector3(sx * 70f, 0f, sz), pole, dark, glow);

            var flags = Group("CornerFlags", stadium);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { 0f, PitchLength })
                {
                    Prim(PrimitiveType.Cylinder, "FlagPole", new Vector3(sx * HalfWidth, 0.75f, sz), new Vector3(0.04f, 0.75f, 0.04f), goalWhite, flags);
                    Prim(PrimitiveType.Cube, "Flag", new Vector3(sx * HalfWidth, 1.35f, sz + (sz > 1f ? -0.2f : 0.2f)), new Vector3(0.02f, 0.3f, 0.4f), flag, flags);
                }

            // ---------------- players ----------------
            var players = Group("Players");
            var keeperRoot = new GameObject("Keeper");
            keeperRoot.transform.SetParent(players, false);
            keeperRoot.transform.position = new Vector3(0f, 0f, KeeperZ);
            var keeper = keeperRoot.AddComponent<KeeperController>();
            var keeperRig = Humanoid(keeperRoot.transform, keeperKit, keeperGloves: true);
            var keeperAnim = keeperRoot.AddComponent<KeeperAnimator>();

            float r = FieldGeometry.BallDiameter / 2f;
            var ballGo = Prim(PrimitiveType.Sphere, "Ball", new Vector3(0f, r, FieldGeometry.PenaltyDistance), Vector3.one * FieldGeometry.BallDiameter, ballMat, players);
            var ball = ballGo.AddComponent<BallController>();
            var trail = ballGo.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.minVertexDistance = 0.05f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.16f), new Keyframe(1f, 0f));
            var trailGrad = new Gradient();
            trailGrad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.8f, 0.5f), 1f) },
                              new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = trailGrad;
            trail.sharedMaterial = trailMat;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;

            var strikerRoot = new GameObject("Striker");
            strikerRoot.transform.SetParent(players, false);
            strikerRoot.transform.SetPositionAndRotation(new Vector3(1.5f, 0f, 14.4f), Quaternion.LookRotation(new Vector3(-1.5f, 0f, -3.4f).normalized));
            var striker = strikerRoot.AddComponent<StrikerController>();
            var strikerRig = Humanoid(strikerRoot.transform, strikerKit, keeperGloves: false);
            var strikerAnim = strikerRoot.AddComponent<StrikerAnimator>();

            // ---------------- target marker ----------------
            var markerGo = Prim(PrimitiveType.Cube, "ZoneMarker", new Vector3(0f, 0.015f, MarkerZ), new Vector3(2.3f, 0.02f, 0.6f), markerMat, stadium, shadows: false);
            var marker = markerGo.AddComponent<ZoneMarker>();
            markerGo.GetComponent<Renderer>().enabled = false;

            // ---------------- effects ----------------
            var effectsGo = new GameObject("Effects");
            var dust = Particles("KickDust", effectsGo.transform, particleMat,
                Colours(new Color(0.62f, 0.66f, 0.52f, 0.7f), new Color(0.45f, 0.55f, 0.38f, 0.7f)),
                new Vector2(0.5f, 0.9f), new Vector2(0.6f, 1.6f), new Vector2(0.25f, 0.5f), -0.02f, 60f, 60, flutter: false, fade: true);
            var confetti = Particles("SaveConfetti", effectsGo.transform, particleMat,
                Colours(new Color(1f, 0.55f, 0.1f), new Color(0.2f, 0.45f, 1f), Color.white, new Color(1f, 0.9f, 0.2f), new Color(0.3f, 0.9f, 0.4f)),
                new Vector2(1.4f, 2.4f), new Vector2(4f, 8f), new Vector2(0.07f, 0.14f), 0.9f, 40f, 300, flutter: true, fade: true);
            var sparks = Particles("SaveSparks", effectsGo.transform, particleMat,
                Colours(new Color(3f, 2.4f, 0.8f), new Color(2.6f, 2.6f, 2.6f)),
                new Vector2(0.25f, 0.55f), new Vector2(4f, 9f), new Vector2(0.06f, 0.12f), 0.3f, 80f, 120, flutter: false, fade: true);
            var effects = effectsGo.AddComponent<EffectsDirector>();

            // ---------------- game logic ----------------
            var gameGo = new GameObject("Game");
            var input = gameGo.AddComponent<KeyboardInput>();
            var emgIn = gameGo.AddComponent<EmgReceiver>();       // UDP 5005 ← Python bridge
            var gm = gameGo.AddComponent<GameManager>();
            var udpEvents = gameGo.AddComponent<EventSender>();   // UDP 5006 → Python bridge
            var flow = gameGo.AddComponent<GameFlow>();
            var hud = gameGo.AddComponent<HudOverlay>();
            var debugPanel = gameGo.AddComponent<DebugStatusOverlay>();
            gameGo.AddComponent<AudioSource>();
            var sfx = gameGo.AddComponent<AudioFeedback>();
            var dresser = stadium.gameObject.AddComponent<StadiumDresser>();

            Wire(gm, ("keeper", keeper), ("ball", ball), ("striker", striker), ("marker", marker));
            Wire(striker, ("ball", ballGo.transform));
            Wire(keeperAnim, ("rig", keeperRig), ("keeper", keeper), ("game", gm));
            Wire(strikerAnim, ("rig", strikerRig), ("striker", striker));
            Wire(hud, ("game", gm), ("flow", flow));
            Wire(debugPanel, ("game", gm), ("emg", emgIn), ("events", udpEvents));
            Wire(udpEvents, ("game", gm));
            Wire(sfx, ("game", gm));
            Wire(effects, ("game", gm), ("cameraDirector", director), ("ballTrail", trail), ("kickDust", dust),
                 ("saveBurst", confetti), ("saveSparks", sparks), ("homeNet", homeNet));

            var soDresser = new SerializedObject(dresser);
            soDresser.FindProperty("game").objectReferenceValue = gm;
            soDresser.FindProperty("pitch").objectReferenceValue = pitch.GetComponent<Renderer>();
            soDresser.FindProperty("ball").objectReferenceValue = ballGo.GetComponent<Renderer>();
            CopyComponentsArray(soDresser, "nets", nets);
            CopyComponentsArray(soDresser, "crowd", crowd);
            CopyComponentsArray(soDresser, "ledBoards", leds);
            soDresser.ApplyModifiedPropertiesWithoutUndo();

            var soCam = new SerializedObject(director);
            CopyComponentsArray(soCam, "hideWhenBehindGoal", homeBackParts);
            soCam.ApplyModifiedPropertiesWithoutUndo();

            string[] levelFiles = { "L0_FreePlay", "L1_ThreeZones", "L2_FiveZones", "L3_Pro" };
            var soFlow = new SerializedObject(flow);
            soFlow.FindProperty("game").objectReferenceValue = gm;
            soFlow.FindProperty("keyboard").objectReferenceValue = input;
            soFlow.FindProperty("emg").objectReferenceValue = emgIn;
            soFlow.FindProperty("events").objectReferenceValue = udpEvents;
            soFlow.FindProperty("cameraDirector").objectReferenceValue = director;
            // bump GameFlow.SceneVersionCurrent whenever this builder wires something new
            soFlow.FindProperty("sceneVersion").intValue = GameFlow.SceneVersionCurrent;
            soFlow.FindProperty("displayFont").objectReferenceValue = LoadFont("BebasNeue-Regular.ttf");
            soFlow.FindProperty("bodyFont").objectReferenceValue = LoadFont("BarlowCondensed-SemiBold.ttf");
            var levelsProp = soFlow.FindProperty("levels");
            levelsProp.arraySize = levelFiles.Length;
            for (int i = 0; i < levelFiles.Length; i++)
            {
                var cfg = AssetDatabase.LoadAssetAtPath<LevelConfig>($"{LevelsDir}/{levelFiles[i]}.asset"); // fresh handle
                levelsProp.GetArrayElementAtIndex(i).objectReferenceValue = cfg;
                if (cfg == null) Debug.LogError($"[Keeper244] SceneBuilder: level asset {levelFiles[i]} missing.");
            }
            soFlow.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = gameGo;
            Debug.Log($"[Keeper244] Built {ScenePath}. Press Play and pick a level in the menu. A/D = near, Shift = far, C = camera, Esc = pause.");
        }

        // ---------------- pieces ----------------

        static void BuildMarkings(Transform p, Material m)
        {
            float L = PitchLength, W = HalfWidth;
            Line(p, m, "Touchline_L", new Vector3(-W, 0, 0), new Vector3(-W, 0, L));
            Line(p, m, "Touchline_R", new Vector3(W, 0, 0), new Vector3(W, 0, L));
            Line(p, m, "GoalLine_Home", new Vector3(-W, 0, 0), new Vector3(W, 0, 0));
            Line(p, m, "GoalLine_Far", new Vector3(-W, 0, L), new Vector3(W, 0, L));
            Line(p, m, "Halfway", new Vector3(-W, 0, L / 2f), new Vector3(W, 0, L / 2f));
            Arc(p, m, "CentreCircle", new Vector3(0, 0, L / 2f), 9.15f, 0f, 2f * Mathf.PI, 56);
            Spot(p, m, "CentreSpot", new Vector3(0, 0, L / 2f), 0.3f);

            foreach (var (z0, dir, tag) in new[] { (0f, 1f, "Home"), (L, -1f, "Far") })
            {
                // penalty area 40.32 × 16.5, goal area 18.32 × 5.5
                float pa = z0 + dir * 16.5f, ga = z0 + dir * 5.5f;
                Line(p, m, $"PA_Front_{tag}", new Vector3(-20.16f, 0, pa), new Vector3(20.16f, 0, pa));
                Line(p, m, $"PA_L_{tag}", new Vector3(-20.16f, 0, z0), new Vector3(-20.16f, 0, pa));
                Line(p, m, $"PA_R_{tag}", new Vector3(20.16f, 0, z0), new Vector3(20.16f, 0, pa));
                Line(p, m, $"GA_Front_{tag}", new Vector3(-9.16f, 0, ga), new Vector3(9.16f, 0, ga));
                Line(p, m, $"GA_L_{tag}", new Vector3(-9.16f, 0, z0), new Vector3(-9.16f, 0, ga));
                Line(p, m, $"GA_R_{tag}", new Vector3(9.16f, 0, z0), new Vector3(9.16f, 0, ga));

                var spot = new Vector3(0, 0, z0 + dir * FieldGeometry.PenaltyDistance);
                Spot(p, m, $"PenaltySpot_{tag}", spot);
                // the "D": the part of the 9.15 m circle around the spot that lies outside the box
                float half = Mathf.Acos(5.5f / 9.15f); // box edge is 5.5 m beyond the spot
                float mid = dir > 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                Arc(p, m, $"PenaltyArc_{tag}", spot, 9.15f, mid - half, mid + half, 18);

                Arc(p, m, $"CornerArc_L_{tag}", new Vector3(-W, 0, z0), 1f, dir > 0 ? 0f : -Mathf.PI / 2f, dir > 0 ? Mathf.PI / 2f : 0f, 6);
                Arc(p, m, $"CornerArc_R_{tag}", new Vector3(W, 0, z0), 1f, dir > 0 ? Mathf.PI / 2f : -Mathf.PI, dir > 0 ? Mathf.PI : -Mathf.PI / 2f, 6);
            }
        }

        /// <summary>
        /// Goal with frame, back stanchions and net panels. `yaw` 0 = faces +z (home), 180 = faces −z (far).
        /// `backParts` (optional) collects everything behind the goal line (net panels and back bars).
        /// </summary>
        static Transform BuildGoal(Transform parent, string name, float z, float yaw, Material frame, Material netMat,
                                   List<Renderer> nets, List<Renderer> backParts, out Transform netGroup)
        {
            var root = Group(name, parent);
            root.position = new Vector3(0f, 0f, z);
            root.rotation = Quaternion.Euler(0f, yaw, 0f);

            float halfW = FieldGeometry.GoalWidth / 2f;
            float postH = FieldGeometry.GoalHeight + PostSize;
            Vector3 W(float x, float y, float zz) => root.TransformPoint(new Vector3(x, y, zz));
            var rot = root.rotation;

            Prim(PrimitiveType.Cube, "Post_Left", W(-(halfW + PostSize / 2f), postH / 2f, -PostSize / 2f), new Vector3(PostSize, postH, PostSize), frame, root, rot);
            Prim(PrimitiveType.Cube, "Post_Right", W(halfW + PostSize / 2f, postH / 2f, -PostSize / 2f), new Vector3(PostSize, postH, PostSize), frame, root, rot);
            Prim(PrimitiveType.Cube, "Crossbar", W(0f, FieldGeometry.GoalHeight + PostSize / 2f, -PostSize / 2f), new Vector3(FieldGeometry.GoalWidth + 2f * PostSize, PostSize, PostSize), frame, root, rot);

            // thin stanchions holding the net at the back
            float back = -NetDepth;
            const float bar = 0.05f;
            var back4 = new[]
            {
                Prim(PrimitiveType.Cube, "BackBar_Top", W(0f, FieldGeometry.GoalHeight, back), new Vector3(FieldGeometry.GoalWidth + 0.2f, bar, bar), frame, root, rot),
                Prim(PrimitiveType.Cube, "BackBar_Ground", W(0f, bar / 2f, back), new Vector3(FieldGeometry.GoalWidth + 0.2f, bar, bar), frame, root, rot),
                Prim(PrimitiveType.Cube, "BackPost_L", W(-(halfW + 0.1f), FieldGeometry.GoalHeight / 2f, back), new Vector3(bar, FieldGeometry.GoalHeight, bar), frame, root, rot),
                Prim(PrimitiveType.Cube, "BackPost_R", W(halfW + 0.1f, FieldGeometry.GoalHeight / 2f, back), new Vector3(bar, FieldGeometry.GoalHeight, bar), frame, root, rot),
            };

            netGroup = Group("Net", root);
            netGroup.localPosition = Vector3.zero;
            netGroup.localRotation = Quaternion.identity;
            float nw = FieldGeometry.GoalWidth + 0.2f, nh = FieldGeometry.GoalHeight;
            Vector3 fwd = root.forward;
            var panels = new[]
            {
                FacingQuad("Net_Back", W(0f, nh / 2f, back), new Vector2(nw, nh), fwd, netMat, netGroup).GetComponent<Renderer>(),
                FacingQuad("Net_Roof", W(0f, nh, back / 2f), new Vector2(nw, NetDepth), Vector3.down, netMat, netGroup, up: fwd).GetComponent<Renderer>(),
                FacingQuad("Net_Left", W(-(halfW + 0.1f), nh / 2f, back / 2f), new Vector2(NetDepth, nh), root.right, netMat, netGroup).GetComponent<Renderer>(),
                FacingQuad("Net_Right", W(halfW + 0.1f, nh / 2f, back / 2f), new Vector2(NetDepth, nh), -root.right, netMat, netGroup).GetComponent<Renderer>(),
            };
            nets.AddRange(panels);
            if (backParts != null)
            {
                backParts.AddRange(panels);
                foreach (var go in back4) backParts.Add(go.GetComponent<Renderer>());
            }
            return root;
        }

        /// <summary>
        /// One stand: front wall, a sloped block whose top face carries the crowd texture, back wall,
        /// cantilever roof with a strip of lights. `front` = centre of the front edge on the ground.
        /// Returns the crowd renderer.
        /// </summary>
        static Renderer Stand(Transform parent, string name, Vector3 front, Vector3 outward, float length,
                              Material crowdMat, Material concrete, Material dark, Material roofLights)
        {
            var g = Group(name, parent);
            outward = outward.normalized;
            const float wallH = 1.8f, rise = 19f, run = 23f;
            float slopeLen = Mathf.Sqrt(rise * rise + run * run);
            var faceRot = Quaternion.LookRotation(outward, Vector3.up); // local x along the stand, z outward

            Prim(PrimitiveType.Cube, "FrontWall", front + Vector3.up * wallH / 2f, new Vector3(length, wallH, 0.6f), concrete, g, faceRot);

            Vector3 slope = (outward * run + Vector3.up * rise).normalized;
            Vector3 normal = (-outward * rise + Vector3.up * run).normalized;
            Vector3 slabCentre = front + outward * 0.6f + Vector3.up * wallH + slope * (slopeLen / 2f) - normal * 0.2f;
            var slab = Prim(PrimitiveType.Cube, "Crowd", slabCentre, new Vector3(length, 0.4f, slopeLen), crowdMat, g,
                            Quaternion.LookRotation(slope, normal));

            float top = wallH + rise;
            Prim(PrimitiveType.Cube, "BackWall", front + outward * (run + 1.2f) + Vector3.up * (top + 5f) / 2f, new Vector3(length, top + 5f, 0.8f), dark, g, faceRot);
            Prim(PrimitiveType.Cube, "Roof", front + outward * (run - 8f) + Vector3.up * (top + 5f), new Vector3(length, 0.7f, 20f), dark, g, faceRot);
            Prim(PrimitiveType.Cube, "RoofLights", front + outward * (run - 17.6f) + Vector3.up * (top + 4.55f), new Vector3(length - 4f, 0.25f, 0.5f), roofLights, g, faceRot, shadows: false);
            return slab.GetComponent<Renderer>();
        }

        /// <summary>LED advertising board: dark backing box with a screen quad facing the pitch.</summary>
        static Renderer Board(Transform parent, Vector3 baseCentre, Vector3 facing, float length, Material led, Material back)
        {
            const float h = 0.95f;
            facing = facing.normalized;
            var rot = Quaternion.LookRotation(facing, Vector3.up);
            Prim(PrimitiveType.Cube, "BoardBack", baseCentre + Vector3.up * h / 2f, new Vector3(length, h, 0.2f), back, parent, rot);
            var screen = FacingQuad("BoardScreen", baseCentre + Vector3.up * (h / 2f + 0.02f) + facing * 0.105f, new Vector2(length - 0.1f, h - 0.1f), facing, led, parent);
            return screen.GetComponent<Renderer>();
        }

        static void Floodlight(Transform parent, Vector3 baseAt, Material pole, Material housing, Material glow)
        {
            const float height = 46f;
            var g = Group("Floodlight", parent);
            Prim(PrimitiveType.Cylinder, "Mast", baseAt + Vector3.up * height / 2f, new Vector3(1.1f, height / 2f, 1.1f), pole, g);
            Vector3 head = baseAt + Vector3.up * (height + 2.5f);
            Vector3 toCentre = (new Vector3(0f, 0f, PitchLength / 2f) - head).normalized;
            var rot = Quaternion.LookRotation(toCentre, Vector3.up);
            Prim(PrimitiveType.Cube, "Housing", head, new Vector3(10f, 5.2f, 0.8f), housing, g, rot);
            Prim(PrimitiveType.Cube, "Lamps", head + toCentre * 0.45f, new Vector3(9.2f, 4.4f, 0.15f), glow, g, rot, shadows: false);
        }

        // ---------------- assets ----------------

        static Font LoadFont(string file)
        {
            var f = AssetDatabase.LoadAssetAtPath<Font>($"{FontsDir}/{file}");
            if (f == null) Debug.LogWarning($"[Keeper244] Font {FontsDir}/{file} not found; using Unity's default font.");
            return f;
        }

        static void Wire(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                var p = so.FindProperty(field);
                if (p == null) { Debug.LogError($"[Keeper244] SceneBuilder: {target.GetType().Name}.{field} not found"); continue; }
                if (p.propertyType == SerializedPropertyType.ObjectReference) p.objectReferenceValue = value;
            }
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
    }
}
