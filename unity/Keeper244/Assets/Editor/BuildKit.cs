using System.Collections.Generic;
using Keeper244.Look;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Keeper244.EditorTools
{
    /// <summary>
    /// Helpers the scene builder uses: materials, primitives, pitch lines, footballer rigs,
    /// particle systems and the post-processing profile. Everything is generated from code so the
    /// scene can be rebuilt identically on any machine.
    /// </summary>
    public static class BuildKit
    {
        public const string MaterialsDir = "Assets/Materials";
        public const float LineWidth = 0.12f;
        public const float LineY = 0.012f;

        public enum Shading { Lit, Unlit, UnlitCutoutDoubleSided, Sprite }

        // ---------------- materials ----------------

        /// <summary>Create or update a material asset. Properties are re-applied on every build so the look is reproducible.</summary>
        public static Material Mat(string name, Shading kind, Color color, float smoothness = 0.2f, float metallic = 0f)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            string shaderName = kind switch
            {
                Shading.Lit => "Universal Render Pipeline/Lit",
                Shading.Sprite => "Sprites/Default",
                _ => "Universal Render Pipeline/Unlit",
            };
            var shader = Shader.Find(shaderName) ?? Shader.Find("Standard");

            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);

            if (kind == Shading.UnlitCutoutDoubleSided)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.SetFloat("_Cull", 0f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material Skybox(string name)
        {
            string path = $"{MaterialsDir}/{name}.mat";
            var shader = Shader.Find("Skybox/Procedural");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetFloat("_SunDisk", 2f);               // high quality sun disk
            m.SetFloat("_SunSize", 0.035f);
            m.SetFloat("_SunSizeConvergence", 6f);
            m.SetFloat("_AtmosphereThickness", 1.05f);
            m.SetColor("_SkyTint", new Color(0.50f, 0.58f, 0.72f));
            m.SetColor("_GroundColor", new Color(0.32f, 0.31f, 0.30f));
            m.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- primitives ----------------

        public static GameObject Prim(PrimitiveType type, string name, Vector3 worldPos, Vector3 scale, Material mat, Transform parent,
                                      Quaternion? rotation = null, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(worldPos, rotation ?? Quaternion.identity);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // results are logical, no physics needed
            return go;
        }

        /// <summary>Primitive placed in its parent's local space (for rig parts).</summary>
        public static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>A quad whose visible face points along `facing` (Unity quads are seen from their −z side).</summary>
        public static GameObject FacingQuad(string name, Vector3 centre, Vector2 size, Vector3 facing, Material mat, Transform parent,
                                            bool shadows = false, Vector3? up = null)
        {
            var rot = Quaternion.LookRotation(-facing.normalized, up ?? Vector3.up);
            return Prim(PrimitiveType.Quad, name, centre, new Vector3(size.x, size.y, 1f), mat, parent, rot, shadows);
        }

        public static Transform Group(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.transform;
        }

        // ---------------- pitch markings ----------------

        /// <summary>Painted line between two ground points (y ignored).</summary>
        public static void Line(Transform parent, Material mat, string name, Vector3 a, Vector3 b)
        {
            a.y = b.y = LineY;
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            var rot = Quaternion.LookRotation(d / len, Vector3.up);
            Prim(PrimitiveType.Cube, name, (a + b) / 2f, new Vector3(LineWidth, 0.01f, len + LineWidth * 0.5f), mat, parent, rot, shadows: false);
        }

        /// <summary>Arc of a circle on the ground, from angle a0 to a1 (radians, 0 = +x, π/2 = +z).</summary>
        public static void Arc(Transform parent, Material mat, string name, Vector3 centre, float radius, float a0, float a1, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float t0 = Mathf.Lerp(a0, a1, (float)i / segments), t1 = Mathf.Lerp(a0, a1, (float)(i + 1) / segments);
                var p0 = centre + new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0)) * radius;
                var p1 = centre + new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1)) * radius;
                Line(parent, mat, $"{name}_{i}", p0, p1);
            }
        }

        public static void Spot(Transform parent, Material mat, string name, Vector3 at, float diameter = 0.24f)
        {
            Prim(PrimitiveType.Cylinder, name, new Vector3(at.x, LineY, at.z), new Vector3(diameter, 0.005f, diameter), mat, parent, shadows: false);
        }

        // ---------------- footballers ----------------

        public struct Kit
        {
            public Material jersey, shorts, socks, skin, hair, boots, hands;
        }

        /// <summary>
        /// Builds a primitive footballer under `root` (root at the feet, facing its +z) and returns
        /// the rig with every joint wired. Height ≈ 1.82 m.
        /// </summary>
        public static HumanoidRig Humanoid(Transform root, Kit kit, bool keeperGloves)
        {
            var rig = root.gameObject.AddComponent<HumanoidRig>();
            Transform J(string name, Transform parent, Vector3 pos)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent, false);
                t.localPosition = pos;
                return t;
            }

            var hips = J("Hips", root, new Vector3(0f, 0.95f, 0f));
            Part(PrimitiveType.Cube, "Pelvis", hips, new Vector3(0f, 0.02f, 0f), new Vector3(0.36f, 0.2f, 0.24f), kit.shorts);

            var spine = J("Spine", hips, new Vector3(0f, 0.08f, 0f));
            Part(PrimitiveType.Capsule, "Torso", spine, new Vector3(0f, 0.28f, 0f), new Vector3(0.46f, 0.3f, 0.28f), kit.jersey);
            // capsule lying sideways: its long axis (local y) is turned to point along x
            Part(PrimitiveType.Capsule, "Shoulders", spine, new Vector3(0f, 0.5f, 0f), new Vector3(0.12f, 0.28f, 0.22f), kit.jersey).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            var head = J("Head", spine, new Vector3(0f, 0.6f, 0f));
            Part(PrimitiveType.Cylinder, "Neck", head, new Vector3(0f, 0.03f, 0f), new Vector3(0.1f, 0.05f, 0.1f), kit.skin);
            Part(PrimitiveType.Sphere, "Face", head, new Vector3(0f, 0.17f, 0f), new Vector3(0.22f, 0.25f, 0.23f), kit.skin);
            Part(PrimitiveType.Sphere, "Hair", head, new Vector3(0f, 0.215f, -0.018f), new Vector3(0.235f, 0.2f, 0.235f), kit.hair);

            Transform Arm(string side, float x, out Transform fore)
            {
                var upper = J($"UpperArm{side}", spine, new Vector3(x, 0.5f, 0f));
                Part(PrimitiveType.Capsule, "Sleeve", upper, new Vector3(0f, -0.15f, 0f), new Vector3(0.13f, 0.16f, 0.13f), kit.jersey);
                fore = J($"ForeArm{side}", upper, new Vector3(0f, -0.3f, 0f));
                Part(PrimitiveType.Capsule, "Forearm", fore, new Vector3(0f, -0.13f, 0f), new Vector3(0.1f, 0.14f, 0.1f), keeperGloves ? kit.jersey : kit.skin);
                if (keeperGloves)
                    Part(PrimitiveType.Cube, "Glove", fore, new Vector3(0f, -0.3f, 0.01f), new Vector3(0.12f, 0.16f, 0.07f), kit.hands);
                else
                    Part(PrimitiveType.Sphere, "Hand", fore, new Vector3(0f, -0.29f, 0f), new Vector3(0.09f, 0.11f, 0.06f), kit.skin);
                return upper;
            }
            rig.upperArmL = Arm("L", -0.27f, out rig.foreArmL);
            rig.upperArmR = Arm("R", 0.27f, out rig.foreArmR);

            Transform Leg(string side, float x, out Transform shin)
            {
                var thigh = J($"Thigh{side}", hips, new Vector3(x, -0.04f, 0f));
                Part(PrimitiveType.Cylinder, "ShortsLeg", thigh, new Vector3(0f, -0.08f, 0f), new Vector3(0.2f, 0.1f, 0.2f), kit.shorts);
                Part(PrimitiveType.Capsule, "Thigh", thigh, new Vector3(0f, -0.23f, 0f), new Vector3(0.15f, 0.19f, 0.15f), kit.skin);
                shin = J($"Shin{side}", thigh, new Vector3(0f, -0.44f, 0f));
                Part(PrimitiveType.Capsule, "Sock", shin, new Vector3(0f, -0.21f, 0f), new Vector3(0.13f, 0.21f, 0.13f), kit.socks);
                Part(PrimitiveType.Cube, "Boot", shin, new Vector3(0f, -0.42f, 0.06f), new Vector3(0.11f, 0.08f, 0.26f), kit.boots);
                return thigh;
            }
            rig.thighL = Leg("L", -0.11f, out rig.shinL);
            rig.thighR = Leg("R", 0.11f, out rig.shinR);

            rig.hips = hips;
            rig.spine = spine;
            rig.head = head;
            rig.standingHipHeight = 0.95f;
            return rig;
        }

        // ---------------- particles ----------------

        public static ParticleSystem Particles(string name, Transform parent, Material mat, Gradient colours, Vector2 lifetime, Vector2 speed,
                                               Vector2 size, float gravity, float coneAngle, int maxParticles, bool flutter, bool fade)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startColor = new ParticleSystem.MinMaxGradient(colours) { mode = ParticleSystemGradientMode.RandomColor };
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 0.15f;

            if (flutter)
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.6f;
                noise.frequency = 0.8f;
            }
            if (fade)
            {
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        public static Gradient Colours(params Color[] cs)
        {
            var keys = new GradientColorKey[cs.Length];
            for (int i = 0; i < cs.Length; i++) keys[i] = new GradientColorKey(cs[i], cs.Length == 1 ? 0f : (float)i / (cs.Length - 1));
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // ---------------- post-processing ----------------

        public static VolumeProfile PostFxProfile(string path)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            T Get<T>() where T : VolumeComponent
            {
                if (!profile.TryGet(out T c))
                {
                    c = profile.Add<T>(true);
                    c.name = typeof(T).Name;
                    AssetDatabase.AddObjectToAsset(c, profile);
                }
                return c;
            }

            var bloom = Get<Bloom>();
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.75f);
            bloom.scatter.Override(0.65f);

            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var colour = Get<ColorAdjustments>();
            colour.postExposure.Override(0.35f);
            colour.contrast.Override(14f);
            colour.saturation.Override(12f);

            var vignette = Get<Vignette>();
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        public static void CopyComponentsArray<T>(SerializedObject so, string field, IList<T> items) where T : Object
        {
            var p = so.FindProperty(field);
            p.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }
}
