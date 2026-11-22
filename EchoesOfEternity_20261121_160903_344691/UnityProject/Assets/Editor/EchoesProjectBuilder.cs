using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Echoes.Editor
{
    /// <summary>
    /// Automatic project generator for Echoes of Eternity.
    ///  - Menu: Echoes > Build Project / Validate Project / Self-Test Rebuild
    ///  - CLI:  -executeMethod Echoes.Editor.ProjectBuilder.BuildProject
    ///          -executeMethod Echoes.Editor.ProjectBuilder.ValidateProject
    /// Everything generated lives in Assets/_Echoes/Generated and under the scene root "[Echoes Generated]".
    /// Re-running replaces only that root; objects under "[User Content]" (or anywhere else in the scene),
    /// existing generated materials and the tuning asset are preserved.
    /// </summary>
    public static partial class ProjectBuilder
    {
        public const string EchoesRoot = "Assets/_Echoes";
        public const string ModelsDir = EchoesRoot + "/Art/Models";
        public const string TexDir = EchoesRoot + "/Art/Textures";
        public const string UIDir = EchoesRoot + "/Art/UI";
        public const string AudioDir = EchoesRoot + "/Audio";
        public const string GenDir = EchoesRoot + "/Generated";
        public const string MatDir = GenDir + "/Materials";
        public const string PrefabDir = GenDir + "/Prefabs";
        public const string SceneDir = GenDir + "/Scenes";
        public const string ScenePath = SceneDir + "/EchoesOfEternity.unity";
        public const string LightingPath = GenDir + "/EchoesLighting.lighting";
        public const string TuningPath = EchoesRoot + "/Settings/EchoesTuning.asset";
        public const string GeneratedRootName = "[Echoes Generated]";
        public const string UserRootName = "[User Content]";

        public static readonly string[] ModelNames =
        {
            "floor_clean", "floor_moss", "wall_clean", "wall_ruined", "pillar", "pillar_broken", "archway", "stairs",
            "moving_platform", "platform_rail", "door_frame", "door_panel", "door_lock", "chest_base", "chest_lid",
            "key_sun", "rune_sun", "key_leaf", "rune_leaf", "key_wave", "rune_wave", "heart", "heart_pedestal",
            "hourglass", "spike_base", "spikes", "pressure_plate", "torch", "checkpoint", "key_pedestal", "rubble",
            "vine", "fern", "emblem", "bridge_2m", "wood_crate"
        };

        static readonly string[] Palette = { "Moss", "Stone", "Blue", "Gold", "Wood", "Shadow", "Brass", "Danger", "Parchment" };

        // ================================================================ entry points

        [MenuItem("Echoes/Build Project (generate scene)", priority = 1)]
        public static void BuildProject()
        {
            bool ok = false;
            try
            {
                BuildAll();
                ok = true;
                Debug.Log("[EchoesBuilder] BuildProject finished successfully. Scene: " + ScenePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[EchoesBuilder] BuildProject FAILED: " + e);
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        [MenuItem("Echoes/Validate Project", priority = 2)]
        public static void ValidateProject()
        {
            List<string> errors;
            List<string> info;
            try
            {
                Validate(out errors, out info);
            }
            catch (Exception e)
            {
                errors = new List<string> { "Validation threw: " + e };
                info = new List<string>();
            }
            var sb = new StringBuilder();
            sb.AppendLine("Echoes of Eternity validation  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Unity " + Application.unityVersion);
            foreach (var i in info) sb.AppendLine("INFO  " + i);
            foreach (var e in errors) sb.AppendLine("ERROR " + e);
            sb.AppendLine(errors.Count == 0 ? "VALIDATION PASSED" : "VALIDATION FAILED (" + errors.Count + " errors)");
            string report = sb.ToString();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/EchoesValidation.txt", report);
            if (errors.Count == 0) Debug.Log("[EchoesBuilder] " + report);
            else Debug.LogError("[EchoesBuilder] " + report);
            if (Application.isBatchMode) EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }

        /// <summary>Builds twice with a user object in the scene and checks nothing is duplicated or lost.</summary>
        [MenuItem("Echoes/Self-Test Rebuild (idempotence)", priority = 3)]
        public static void SelfTestRebuild()
        {
            bool ok = false;
            try
            {
                BuildAll();
                var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                int before = CountGenerated(scene);
                var userRoot = scene.GetRootGameObjects().First(g => g.name == UserRootName);
                var marker = new GameObject("UserMarker_SelfTest");
                marker.transform.SetParent(userRoot.transform, false);
                marker.transform.position = new Vector3(1f, 2f, 3f);
                var looseMarker = new GameObject("UserLooseMarker_SelfTest");
                EditorSceneManager.SaveScene(scene);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/EE_Moss.mat");
                Color userColor = new Color(0.5f, 0.9f, 0.5f);
                Color originalColor = mat.color;
                mat.color = userColor;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();

                BuildAll();

                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                int after = CountGenerated(scene);
                var roots = scene.GetRootGameObjects();
                int generatedRoots = roots.Count(g => g.name == GeneratedRootName);
                var userRoot2 = roots.FirstOrDefault(g => g.name == UserRootName);
                bool markerKept = userRoot2 != null && userRoot2.transform.Find("UserMarker_SelfTest") != null;
                bool looseKept = roots.Any(g => g.name == "UserLooseMarker_SelfTest");
                mat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/EE_Moss.mat");
                bool matKept = mat.color == userColor;

                Debug.Log($"[EchoesBuilder] SelfTest: objects before={before} after={after} generatedRoots={generatedRoots} userMarkerKept={markerKept} looseMarkerKept={looseKept} materialEditKept={matKept}");
                ok = before == after && generatedRoots == 1 && markerKept && looseKept && matKept;

                // clean up test edits
                if (userRoot2 != null)
                {
                    var m = userRoot2.transform.Find("UserMarker_SelfTest");
                    if (m != null) UnityEngine.Object.DestroyImmediate(m.gameObject);
                }
                foreach (var g in roots.Where(g => g != null && g.name == "UserLooseMarker_SelfTest")) UnityEngine.Object.DestroyImmediate(g);
                EditorSceneManager.SaveScene(scene);
                mat.color = originalColor;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
                Debug.Log(ok ? "[EchoesBuilder] SELFTEST PASSED" : "[EchoesBuilder] SELFTEST FAILED");
            }
            catch (Exception e)
            {
                Debug.LogError("[EchoesBuilder] SelfTest threw: " + e);
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static int CountGenerated(Scene scene)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == GeneratedRootName);
            return root == null ? 0 : root.GetComponentsInChildren<Transform>(true).Length;
        }

        [MenuItem("Echoes/Open Generated Scene", priority = 20)]
        public static void OpenScene()
        {
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Debug.LogWarning("Run Echoes > Build Project first.");
        }

        // ================================================================ pipeline

        public static void BuildAll()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building.");
            EnsureLayers();
            EnsureFolders();
            ConfigureTextures();
            ConfigureAudio();
            var mats = CreateMaterials();
            ConfigureModels(mats);
            var prefabs = CreatePrefabs(mats);
            var tuning = EnsureTuning();
            BuildScene(prefabs, mats, tuning);
            ConfigureBuildSettings();
            ConfigurePlayerSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void EnsureLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 0; i < EchoesLayers.Indices.Length; i++)
            {
                var p = layers.GetArrayElementAtIndex(EchoesLayers.Indices[i]);
                string want = EchoesLayers.Names[i];
                if (string.IsNullOrEmpty(p.stringValue)) p.stringValue = want;
                else if (p.stringValue != want)
                    throw new Exception($"Layer {EchoesLayers.Indices[i]} is already used by '{p.stringValue}'. Free it or change EchoesLayers.");
            }
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            EchoesLayers.ApplyCollisionMatrix();
        }

        static void EnsureFolders()
        {
            foreach (var dir in new[] { GenDir, MatDir, PrefabDir, SceneDir, EchoesRoot + "/Settings" })
            {
                if (AssetDatabase.IsValidFolder(dir)) continue;
                string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
            }
        }

        static IEnumerable<string> FilesIn(string dir, string ext) =>
            Directory.GetFiles(dir, "*" + ext).Select(p => p.Replace('\\', '/')).OrderBy(p => p);

        static void ConfigureTextures()
        {
            foreach (var path in FilesIn(TexDir, ".png"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Bilinear;
                ti.SaveAndReimport();
            }
            foreach (var path in FilesIn(UIDir, ".png"))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.mipmapEnabled = false;
                ti.sRGBTexture = true;
                ti.spritePixelsPerUnit = 100f;
                ti.SaveAndReimport();
            }
        }

        static void ConfigureAudio()
        {
            foreach (var path in FilesIn(AudioDir, ".wav"))
            {
                var ai = (AudioImporter)AssetImporter.GetAtPath(path);
                ai.forceToMono = true;
                var s = ai.defaultSampleSettings;
                bool ambience = path.EndsWith("temple_ambience.wav");
                s.loadType = ambience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
            }
        }

        // ================================================================ materials

        public class MaterialSet
        {
            public readonly Dictionary<string, Material> byName = new Dictionary<string, Material>();
            public readonly Dictionary<Material, Material> presentSwap = new Dictionary<Material, Material>();
            public Material this[string n] => byName[n];
        }

        static MaterialSet CreateMaterials()
        {
            var set = new MaterialSet();
            Color blueGlow = new Color(0.21f, 0.68f, 1f);
            set.byName["EE_Stone"] = Mat("EE_Stone", "stone", Color.white);
            set.byName["EE_Stone_Present"] = Mat("EE_Stone_Present", "stone", new Color(0.74f, 0.79f, 0.68f));
            set.byName["EE_Moss"] = Mat("EE_Moss", "moss", Color.white);
            set.byName["EE_Blue"] = Mat("EE_Blue", "blue", Color.white, 0.25f, blueGlow * 0.9f);
            set.byName["EE_Blue_Present"] = Mat("EE_Blue_Present", "blue", new Color(0.6f, 0.7f, 0.75f), 0.15f, blueGlow * 0.18f);
            set.byName["EE_Gold"] = Mat("EE_Gold", "gold", Color.white, 0.45f, null, 0.35f);
            set.byName["EE_GoldGlow"] = Mat("EE_GoldGlow", "gold", Color.white, 0.6f, new Color(0.95f, 0.73f, 0.33f) * 0.9f, 0.4f);
            set.byName["EE_Wood"] = Mat("EE_Wood", "wood", Color.white);
            set.byName["EE_Shadow"] = Mat("EE_Shadow", "shadow", Color.white);
            set.byName["EE_Brass"] = Mat("EE_Brass", "brass", Color.white, 0.35f, null, 0.3f);
            set.byName["EE_Danger"] = Mat("EE_Danger", "danger", Color.white, 0.2f, Color.black);
            set.byName["EE_Parchment"] = Mat("EE_Parchment", "parchment", Color.white);
            set.byName["EE_Lamp"] = Mat("EE_Lamp", "blue", Color.white, 0.4f, blueGlow * 2.2f);
            set.byName["EE_PitBottom"] = Mat("EE_PitBottom", "shadow", new Color(0.12f, 0.12f, 0.14f));
            set.presentSwap[set["EE_Stone"]] = set["EE_Stone_Present"];
            set.presentSwap[set["EE_Blue"]] = set["EE_Blue_Present"];
            return set;
        }

        /// <summary>Creates a Built-in Standard material if missing. Existing materials are kept (user edits survive).</summary>
        static Material Mat(string name, string texture, Color tint, float smoothness = 0.08f, Color? emission = null, float metallic = 0f)
        {
            string path = MatDir + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var shader = Shader.Find("Standard");
            if (shader == null) throw new Exception("Standard shader not found - this builder targets the Built-in Render Pipeline.");
            var m = new Material(shader) { name = name };
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + texture + ".png"));
            m.SetColor("_Color", tint);
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m.enableInstancing = true;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ================================================================ models and prefabs

        static void ConfigureModels(MaterialSet mats)
        {
            foreach (var name in ModelNames)
            {
                string path = ModelsDir + "/" + name + ".obj";
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) throw new Exception("Missing model " + path);
                mi.globalScale = 1f;
                mi.useFileScale = true;
                mi.importAnimation = false;
                mi.animationType = ModelImporterAnimationType.None;
                mi.importCameras = false;
                mi.importLights = false;
                mi.importBlendShapes = false;
                mi.isReadable = false;
                mi.addCollider = false;
                mi.importNormals = ModelImporterNormals.Calculate;
                mi.normalSmoothingAngle = 20f;
                mi.importTangents = ModelImporterTangents.None;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                foreach (var p in Palette)
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "EE_" + p), mats["EE_" + p]);
                mi.SaveAndReimport();
            }
        }

        /// <summary>Gameplay colliders per model (local space). Walls and floors deliberately have none:
        /// the level uses explicit blockers instead of inferring collision from art.</summary>
        static void AddPrefabColliders(GameObject go, string name)
        {
            switch (name)
            {
                case "chest_base": Box(go, new Vector3(0, 0.3f, 0.03f), new Vector3(1.2f, 0.6f, 0.86f)); break;
                case "key_pedestal": Box(go, new Vector3(0, 0.465f, 0), new Vector3(0.8f, 0.93f, 0.8f)); break;
                case "heart_pedestal": Box(go, new Vector3(0, 0.485f, 0), new Vector3(2.3f, 0.97f, 2.3f)); break;
                case "wood_crate": Box(go, new Vector3(0, 0.4f, 0.015f), new Vector3(0.8f, 0.8f, 0.83f)); break;
                case "rune_sun": case "rune_leaf": case "rune_wave": Box(go, new Vector3(0, 0.55f, 0.025f), new Vector3(0.8f, 1.1f, 0.3f)); break;
                case "door_panel": Box(go, new Vector3(0, 1.4f, 0.035f), new Vector3(2.1f, 2.8f, 0.37f)); break;
                case "door_frame":
                    Box(go, new Vector3(-1.25f, 1.625f, 0), new Vector3(0.5f, 3.25f, 0.65f));
                    Box(go, new Vector3(1.25f, 1.625f, 0), new Vector3(0.5f, 3.25f, 0.65f));
                    break;
                case "archway": // separate upright colliders keep the opening traversable
                    Box(go, new Vector3(-1.6f, 2.45f, 0), new Vector3(0.65f, 4.9f, 0.7f));
                    Box(go, new Vector3(1.6f, 2.45f, 0), new Vector3(0.65f, 4.9f, 0.7f));
                    break;
                case "heart": Box(go, new Vector3(0, 0.55f, 0), new Vector3(0.72f, 1.1f, 0.62f)); break;
                case "key_sun": case "key_leaf": case "key_wave": Box(go, new Vector3(0, 0.45f, 0.01f), new Vector3(0.45f, 0.95f, 0.3f)); break;
                case "pillar": Box(go, new Vector3(0, 1.5f, 0), new Vector3(0.95f, 3f, 0.95f)); break;
                case "pillar_broken": Box(go, new Vector3(0, 0.72f, 0), new Vector3(0.95f, 1.44f, 0.95f)); break;
                case "torch":
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.center = new Vector3(0, 0.6f, 0); cap.radius = 0.23f; cap.height = 1.2f;
                    break;
                case "moving_platform":
                    Box(go, new Vector3(0, -0.2f, 0), new Vector3(2.4f, 0.4f, 2.4f));
                    go.layer = EchoesLayers.Platform;
                    break;
                case "platform_rail":
                    Box(go, new Vector3(0, 0.4f, 0), new Vector3(0.18f, 0.8f, 2f));
                    go.layer = EchoesLayers.Platform;
                    break;
            }
        }

        static BoxCollider Box(GameObject go, Vector3 center, Vector3 size)
        {
            var b = go.AddComponent<BoxCollider>();
            b.center = center;
            b.size = size;
            return b;
        }

        static Dictionary<string, GameObject> CreatePrefabs(MaterialSet mats)
        {
            var result = new Dictionary<string, GameObject>();
            foreach (var name in ModelNames)
            {
                string modelPath = ModelsDir + "/" + name + ".obj";
                var assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
                var mesh = assets.OfType<Mesh>().FirstOrDefault();
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (mesh == null || model == null) throw new Exception("Model did not import: " + modelPath);
                var sourceRenderer = model.GetComponentInChildren<MeshRenderer>();
                var slots = sourceRenderer != null ? sourceRenderer.sharedMaterials : new Material[0];
                var materials = new Material[mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++)
                {
                    Material src = i < slots.Length ? slots[i] : null;
                    materials[i] = ResolveMaterial(src, mats);
                }

                var go = new GameObject(name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = materials;
                AddPrefabColliders(go, name);
                string prefabPath = PrefabDir + "/" + name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
                UnityEngine.Object.DestroyImmediate(go);
                result[name] = prefab;
            }
            return result;
        }

        static Material ResolveMaterial(Material src, MaterialSet mats)
        {
            if (src != null)
            {
                if (mats.byName.ContainsValue(src)) return src;
                foreach (var p in Palette)
                    if (src.name.IndexOf("EE_" + p, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        src.name.Equals(p, StringComparison.OrdinalIgnoreCase))
                        return mats["EE_" + p];
            }
            return mats["EE_Stone"];
        }

        static EchoesTuning EnsureTuning()
        {
            var t = AssetDatabase.LoadAssetAtPath<EchoesTuning>(TuningPath);
            if (t != null) return t;
            t = ScriptableObject.CreateInstance<EchoesTuning>();
            AssetDatabase.CreateAsset(t, TuningPath);
            return t;
        }

        // ================================================================ settings

        static void ConfigureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "Echoes of Eternity";
            if (string.IsNullOrEmpty(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = "EchoesStudent";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
        }

        // ================================================================ validation

        static void Validate(out List<string> errors, out List<string> info)
        {
            errors = new List<string>();
            info = new List<string>();
            info.Add("Render pipeline: " + (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline == null ? "Built-in" : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.name));

            // Layers
            for (int i = 0; i < EchoesLayers.Indices.Length; i++)
                if (LayerMask.LayerToName(EchoesLayers.Indices[i]) != EchoesLayers.Names[i])
                    errors.Add($"Layer {EchoesLayers.Indices[i]} should be {EchoesLayers.Names[i]}");

            // Runtime code must not reference UnityEditor
            foreach (var file in Directory.GetFiles(EchoesRoot + "/Scripts/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                if (text.Contains("using UnityEditor") || text.Contains("UnityEditor."))
                    errors.Add("Runtime script references UnityEditor: " + file);
            }
            info.Add("Runtime scripts scanned for UnityEditor references.");

            // Assets
            foreach (var name in ModelNames)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + name + ".prefab");
                if (prefab == null) { errors.Add("Missing prefab " + name); continue; }
                var mf = prefab.GetComponent<MeshFilter>();
                var mr = prefab.GetComponent<MeshRenderer>();
                if (mf == null || mf.sharedMesh == null) errors.Add("Prefab without mesh: " + name);
                if (mr == null || mr.sharedMaterials.Any(m => m == null)) errors.Add("Prefab with missing material: " + name);
                else if (mr.sharedMaterials.Any(m => m.shader == null || m.shader.name != "Standard")) errors.Add("Prefab material not Standard: " + name);
            }
            int clips = Directory.GetFiles(AudioDir, "*.wav").Count(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p.Replace('\\', '/')) != null);
            int sprites = Directory.GetFiles(UIDir, "*.png").Count(p => AssetDatabase.LoadAssetAtPath<Sprite>(p.Replace('\\', '/')) != null);
            int textures = Directory.GetFiles(TexDir, "*.png").Count(p => AssetDatabase.LoadAssetAtPath<Texture2D>(p.Replace('\\', '/')) != null);
            info.Add($"Imported: {ModelNames.Length} prefabs, {textures} textures, {sprites} UI sprites, {clips} audio clips");
            if (clips != 13) errors.Add("Expected 13 audio clips, found " + clips);
            if (sprites != 12) errors.Add("Expected 12 UI sprites, found " + sprites);
            if (textures != 9) errors.Add("Expected 9 textures, found " + textures);

            // Build settings
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath && s.enabled)) errors.Add("Scene missing from build settings");

            // Scene
            if (!File.Exists(ScenePath)) { errors.Add("Scene not generated: " + ScenePath); return; }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var genRoots = roots.Where(g => g.name == GeneratedRootName).ToArray();
            if (genRoots.Length != 1) { errors.Add("Expected exactly one generated root, found " + genRoots.Length); if (genRoots.Length == 0) return; }
            var gen = genRoots[0];
            if (!roots.Any(g => g.name == UserRootName)) errors.Add("Missing user content root");

            ExpectOne<GameSession>(gen, errors);
            ExpectOne<TimelineManager>(gen, errors);
            ExpectOne<FirstPersonMotor>(gen, errors);
            ExpectOne<CheckpointManager>(gen, errors);
            ExpectOne<HUDController>(gen, errors);
            ExpectOne<MenuController>(gen, errors);
            ExpectOne<AudioManager>(gen, errors);
            ExpectOne<RunePuzzle>(gen, errors);
            ExpectOne<MovingPlatform>(gen, errors);
            ExpectOne<ChestInteractable>(gen, errors);
            ExpectOne<KeyDoor>(gen, errors);
            ExpectOne<HeartPickup>(gen, errors);
            ExpectOne<ExitTrigger>(gen, errors);
            if (roots.Sum(r => r.GetComponentsInChildren<AudioListener>(true).Length) != 1) errors.Add("Expected one AudioListener in the scene");
            if (roots.Sum(r => r.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length) != 1) errors.Add("Expected one EventSystem in the scene");

            var tm = gen.GetComponentInChildren<TimelineManager>(true);
            if (tm != null && tm.pastRoot != null && tm.presentRoot != null)
            {
                foreach (var root in new[] { tm.pastRoot, tm.presentRoot })
                {
                    foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        if (mb is KeyPickup || mb is RunePuzzle || mb is ChestInteractable || mb is GameSession || mb is CheckpointManager ||
                            mb is Checkpoint || mb is FlagGate || mb is TimedSpikeTrap || mb is MovingPlatform || mb is HeartPickup || mb is KeyDoor)
                            errors.Add("Stateful component inside timeline root: " + mb.GetType().Name + " on " + mb.name);
                    }
                    var solid = root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList();
                    var proxies = gen.GetComponentsInChildren<TimelineBlockerProxy>(true).Where(p => p.source != null && p.source.transform.IsChildOf(root.transform)).ToList();
                    if (solid.Count != proxies.Count) errors.Add($"{root.name}: {solid.Count} blockers but {proxies.Count} query proxies");
                    info.Add($"{root.name}: {solid.Count} timeline blockers mirrored by query proxies");
                    foreach (var p in proxies)
                    {
                        int want = p.timeline == Timeline.Past ? EchoesLayers.QueryPast : EchoesLayers.QueryPresent;
                        if (p.gameObject.layer != want) errors.Add("Proxy on wrong layer: " + p.name);
                    }
                }
                if (tm.pastRoot.activeSelf) errors.Add("Past root should start inactive (game starts in the Present)");
            }
            else errors.Add("TimelineManager roots not assigned");

            // Keys: identities distinct, exactly one source each
            var keyIds = gen.GetComponentsInChildren<KeyPickup>(true).Select(k => k.key).ToList();
            var chest = gen.GetComponentInChildren<ChestInteractable>(true);
            if (chest != null) keyIds.Add(chest.reward);
            if (keyIds.Count != 3 || keyIds.Distinct().Count() != 3) errors.Add("Each of the three keys must have exactly one source");

            // Checkpoints on common, stationary, clear floor
            var cm = gen.GetComponentInChildren<CheckpointManager>(true);
            if (cm != null)
            {
                var ids = cm.Ids;
                if (ids.Distinct().Count() != ids.Count) errors.Add("Duplicate checkpoint IDs");
                if (cm.Default == null) errors.Add("Default checkpoint missing");
                foreach (var cp in cm.checkpoints)
                {
                    if (cp == null) { errors.Add("Null checkpoint"); continue; }
                    Vector3 p = cp.SpawnPosition;
                    if (!Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.8f, 1, QueryTriggerInteraction.Ignore) || hit.collider.GetComponent<StationaryGround>() == null)
                        errors.Add("Checkpoint not on stationary shared floor: " + cp.id);
                    var zones = Physics.OverlapSphere(p + Vector3.up * 0.5f, 0.2f, EchoesLayers.ZoneMask, QueryTriggerInteraction.Collide);
                    if (!zones.Any(z => z.GetComponent<TimelineSafeZone>() != null)) errors.Add("Checkpoint not inside a safe zone: " + cp.id);
                    var blockers = Physics.OverlapCapsule(p + Vector3.up * 0.4f, p + Vector3.up * 1.4f, 0.34f,
                        EchoesLayers.QueryMask(cp.restoreTimeline) | 1, QueryTriggerInteraction.Ignore)
                        .Where(c => c.GetComponent<StationaryGround>() == null).ToArray();
                    if (blockers.Length > 0) errors.Add("Checkpoint spawn obstructed: " + cp.id + " by " + blockers[0].name);
                }
                info.Add("Checkpoints: " + string.Join(", ", ids));
            }

            // References on all Echoes components
            int checkedRefs = 0;
            foreach (var mb in gen.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) { errors.Add("Missing script in generated scene"); continue; }
                if (mb.GetType().Namespace != "Echoes") continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.name == "m_Script") continue;
                    checkedRefs++;
                    if (it.objectReferenceValue == null && !OptionalReference(mb, it.propertyPath))
                        errors.Add($"Unassigned reference {mb.GetType().Name}.{it.propertyPath} on {mb.name}");
                }
            }
            info.Add("Serialized references checked: " + checkedRefs);
        }

        static bool OptionalReference(MonoBehaviour mb, string path) => false;

        static void ExpectOne<T>(GameObject root, List<string> errors) where T : Component
        {
            int n = root.GetComponentsInChildren<T>(true).Length;
            if (n != 1) errors.Add($"Expected exactly one {typeof(T).Name}, found {n}");
        }
    }
}
