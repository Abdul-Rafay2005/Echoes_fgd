using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Echoes.Editor
{
    /// <summary>
    /// Builds the handcrafted temple. The floor plan is a 2 m grid authored below with rectangles
    /// (not procedural). Legend: '.' floor, 'c' courtyard, 'R' blocked by rubble in the Present,
    /// 'W' blocked by intact masonry in the Past, 'T' spike plate, '~' moving-platform pit.
    /// Grid cell (col,row) maps to world ((col-14)*2, 0, (11-row)*2); north is +Z.
    /// </summary>
    public static partial class ProjectBuilder
    {
        const int GridW = 25, GridH = 20, OriginCol = 14, OriginRow = 11;
        static char[,] grid;

        class Ctx
        {
            public Dictionary<string, GameObject> prefabs;
            public MaterialSet mats;
            public EchoesTuning tuning;
            public Transform root, shared, past, present, proxies, gameplay, sharedArt, floorColliders, wallColliders, safeZones, hints;
            public System.Random rng = new System.Random(1121);
            public AudioManager audio;
            public GameSession session;
            public HashSet<Vector2Int> reserved = new HashSet<Vector2Int>();
        }

        static Ctx C;

        // ================================================================ grid

        static void BuildGrid()
        {
            grid = new char[GridW, GridH];
            for (int c = 0; c < GridW; c++) for (int r = 0; r < GridH; r++) grid[c, r] = ' ';
            Fill(12, 2, 16, 5);          // Heart chamber
            Fill(19, 2, 23, 4);          // Spike passage end room (LEAF key)
            Fill(5, 3, 10, 3);           // WAVE return shortcut corridor
            Fill(10, 4, 10, 4);
            Fill(3, 4, 5, 6);            // Platform landing B (chest)
            Fill(6, 5, 8, 6, '~');       // Platform pit
            Fill(9, 5, 10, 6);           // Platform landing A
            Fill(11, 6, 11, 7);
            Fill(11, 8, 11, 8, 'R');     // Present rubble blocks the platform route
            Fill(14, 6, 14, 8);          // Heart neck
            Fill(19, 5, 19, 9);          // LEAF return shortcut
            Fill(18, 9, 18, 9);
            Fill(21, 5, 22, 11);         // Spike corridor
            Fill(21, 6, 22, 6, 'T');
            Fill(21, 8, 22, 8, 'T');
            Fill(21, 10, 22, 10, 'T');
            Fill(6, 8, 6, 8);            // Rune cache alcove (SUN key)
            Fill(4, 9, 8, 12);           // Rune Gallery
            Fill(9, 11, 10, 11);
            Fill(11, 9, 17, 13);         // Entrance hall (hub)
            Fill(18, 11, 18, 11, 'R');   // Present rubble blocks the spike route
            Fill(19, 11, 20, 11);
            Fill(5, 13, 5, 13, 'W');     // Past masonry hides the dead end
            Fill(5, 14, 5, 14);          // Dead-end nook
            Fill(14, 14, 14, 15);        // Entrance neck
            Fill(11, 16, 17, 18, 'c');   // Courtyard outside the entrance
        }

        static void Fill(int c0, int r0, int c1, int r1, char ch = '.')
        {
            for (int c = c0; c <= c1; c++) for (int r = r0; r <= r1; r++) grid[c, r] = ch;
        }

        static char At(int c, int r) => c < 0 || r < 0 || c >= GridW || r >= GridH ? ' ' : grid[c, r];
        static bool IsOpen(char ch) => ch != ' ';
        static bool IsFloor(char ch) => ".cRWT".IndexOf(ch) >= 0;
        static bool IsSafe(char ch) => ".cRW".IndexOf(ch) >= 0;
        static Vector3 Cell(float c, float r) => new Vector3((c - OriginCol) * 2f, 0f, (OriginRow - r) * 2f);
        static Vector3 Mid(int c0, int r0, int c1, int r1) => (Cell(c0, r0) + Cell(c1, r1)) * 0.5f;
        static readonly Vector2Int[] Dirs = { new Vector2Int(0, -1), new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(-1, 0) };
        static Vector3 WorldDir(Vector2Int d) => new Vector3(d.x, 0f, -d.y);
        static float YawFacing(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        static float YawToward(Vector3 from, Vector3 to) => YawFacing(to - from);

        static string GridToString()
        {
            var sb = new System.Text.StringBuilder();
            for (int r = 0; r < GridH; r++)
            {
                for (int c = 0; c < GridW; c++) sb.Append(grid[c, r]);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // ================================================================ helpers

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject Place(string prefab, Transform parent, Vector3 localPos, float yaw, bool presentTheme = false, Vector3? scale = null, bool isStatic = false)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(C.prefabs[prefab], parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            if (presentTheme) ApplyPresentTheme(go);
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void ApplyPresentTheme(GameObject go)
        {
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && C.mats.presentSwap.TryGetValue(mats[i], out var swap)) { mats[i] = swap; changed = true; }
                if (changed) mr.sharedMaterials = mats;
            }
        }

        static void SetMaterial(GameObject go, Material m)
        {
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                mr.sharedMaterials = Enumerable.Repeat(m, mr.sharedMaterials.Length).ToArray();
        }

        static BoxCollider BoxObject(Transform parent, string name, Vector3 localCenter, Vector3 size, int layer = 0, bool trigger = false)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            var b = go.AddComponent<BoxCollider>();
            b.size = size;
            b.isTrigger = trigger;
            return b;
        }

        static Light PointLight(Transform parent, string name, Vector3 localPos, Color color, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            return l;
        }

        static Transform Anchor(Transform parent, string name, Vector3 pos, float yaw)
        {
            var t = Child(parent, name);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        static float Rand() => (float)C.rng.NextDouble();

        // ================================================================ scene

        static void BuildScene(Dictionary<string, GameObject> prefabs, MaterialSet mats, EchoesTuning tuning)
        {
            if (!Application.isBatchMode) EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            Scene scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var g in scene.GetRootGameObjects())
                if (g.name == GeneratedRootName) Object.DestroyImmediate(g);
            if (!scene.GetRootGameObjects().Any(g => g.name == UserRootName))
            {
                var user = new GameObject(UserRootName);
                SceneManager.MoveGameObjectToScene(user, scene);
            }

            BuildGrid();
            Debug.Log("[EchoesBuilder] Level grid:\n" + GridToString());

            C = new Ctx { prefabs = prefabs, mats = mats, tuning = tuning };
            var rootGo = new GameObject(GeneratedRootName);
            SceneManager.MoveGameObjectToScene(rootGo, scene);
            C.root = rootGo.transform;

            // ---- systems
            var systems = Child(C.root, "[Systems]");
            C.session = systems.gameObject.AddComponent<GameSession>();
            var timeline = systems.gameObject.AddComponent<TimelineManager>();
            var checkpointMgr = systems.gameObject.AddComponent<CheckpointManager>();
            var levelInfo = systems.gameObject.AddComponent<EchoesLevelInfo>();
            systems.gameObject.AddComponent<EchoesAutoTest>();
            var audioT = Child(systems, "Audio");
            C.audio = audioT.gameObject.AddComponent<AudioManager>();
            BuildAudio(C.audio, audioT, tuning);

            // ---- world roots
            var world = Child(C.root, "World");
            C.shared = Child(world, "Shared");
            C.past = Child(world, "Past");
            C.present = Child(world, "Present");
            C.proxies = Child(world, "TimelineQueryProxies");
            C.floorColliders = Child(C.shared, "FloorColliders");
            C.wallColliders = Child(C.shared, "WallColliders");
            C.safeZones = Child(C.shared, "SafeZones");
            C.sharedArt = Child(C.shared, "Art");
            C.gameplay = Child(C.shared, "Gameplay");
            C.hints = Child(C.gameplay, "Hints");

            ReserveCells();
            BuildFloorsAndWalls();
            BuildTimelineBarriers();
            BuildLandmarks();

            // ---- lighting
            var sun = BuildSun(C.root);
            var torches = BuildTorches();

            // ---- gameplay
            var exitGate = BuildGate("ExitDoor", Mid(14, 15, 14, 16), 0f, EchoesIds.FlagHeart);
            AddNote(exitGate, "Inspect the sealed entrance", "The entrance is sealed by the temple's magic. Only the Heart of Eternity can open it again.");
            var heartGate = BuildGate("HeartChamberDoor", Mid(14, 9, 14, 8), 180f, EchoesIds.DoorHeart);
            var keyDoor = heartGate.gameObject.AddComponent<KeyDoor>();
            keyDoor.keySlots = BuildKeySlots(heartGate.panel);
            var leafGate = BuildGate("LeafShortcutDoor", Mid(17, 9, 18, 9), -90f, EchoesIds.ShortcutLeaf);
            AddNote(leafGate, "Inspect the heavy door", "A heavy door with a LEAF mark. It must open from the far side.");
            var waveGate = BuildGate("WaveShortcutDoor", Mid(5, 4, 5, 3), 180f, EchoesIds.ShortcutWave);
            AddNote(waveGate, "Inspect the heavy door", "A heavy door with a WAVE mark. Perhaps the chest nearby holds the answer.");
            var cacheGate = BuildGate("RuneCacheDoor", Mid(6, 9, 6, 8), 180f, EchoesIds.PuzzleRune);
            AddNote(cacheGate, "Inspect the rune cache", "A small cache sealed with rune-work. The tablets in this gallery must be pressed in the right order.");

            var sunKey = BuildKey(KeyId.Sun, Cell(6, 8), 180f, EchoesIds.PuzzleRune, null);
            var leafKey = BuildKey(KeyId.Leaf, Cell(23, 2) + new Vector3(0.2f, 0f, 0.2f), YawToward(Cell(23, 2), Cell(21, 3)), null, EchoesIds.ShortcutLeaf);
            var rune = BuildRunePuzzle();
            var traps = BuildTraps();
            var platform = BuildPlatform(out Transform endA, out Transform endB);
            var chest = BuildChest();
            var heart = BuildHeart();
            var exitTrigger = BoxObject(C.gameplay, "ExitTrigger", new Vector3(0f, 1f, -12.6f), new Vector3(12f, 2f, 4.8f), EchoesLayers.Zones, true);
            exitTrigger.gameObject.AddComponent<ExitTrigger>();
            var guide = BuildExitGuide();

            var checkpoints = new[]
            {
                BuildCheckpoint("hub", 14, 12, Timeline.Present, 0f),
                BuildCheckpoint("gallery", 8, 12, Timeline.Present, -90f),
                BuildCheckpoint("spike_start", 20, 11, Timeline.Past, 90f),
                BuildCheckpoint("spike_end", 20, 3, Timeline.Past, 90f),
                BuildCheckpoint("platform_a", 10, 6, Timeline.Past, -90f),
                BuildCheckpoint("platform_b", 4, 6, Timeline.Past, 90f),
            };
            checkpointMgr.defaultId = "hub";
            checkpointMgr.checkpoints = checkpoints;

            BuildHints();

            // ---- player and UI
            var start = Anchor(C.gameplay, "PlayerStart", Cell(14, 15) + Vector3.up * 0.05f, 0f);
            var player = BuildPlayer(start, tuning, out var motor, out var sensor, out var interactor, out var cam);
            BuildUI(C.root, C.session, rune, out var hud, out var menus);

            // ---- timeline query proxies (after all timeline colliders exist)
            BuildProxies(C.past, Child(C.proxies, "Past"), Timeline.Past);
            BuildProxies(C.present, Child(C.proxies, "Present"), Timeline.Present);

            // ---- wiring
            var s = C.session;
            s.tuning = tuning; s.motor = motor; s.sensor = sensor; s.interactor = interactor; s.timeline = timeline;
            s.checkpoints = checkpointMgr; s.hud = hud; s.menus = menus; s.audioManager = C.audio;
            s.platforms = new[] { platform }; s.traps = traps; s.runePuzzles = new[] { rune }; s.exitGate = exitGate; s.startPoint = start;

            timeline.session = s; timeline.motor = motor; timeline.sensor = sensor; timeline.interactor = interactor;
            timeline.tuning = tuning; timeline.audioManager = C.audio; timeline.pastRoot = C.past.gameObject; timeline.presentRoot = C.present.gameObject;
            timeline.sun = sun;
            timeline.pastLighting = new TimelineManager.LightingProfile
            {
                sunColor = new Color(1f, 0.96f, 0.88f), sunIntensity = 1.2f, ambient = new Color(0.47f, 0.52f, 0.6f),
                fogColor = new Color(0.62f, 0.76f, 0.9f), fogDensity = 0.012f, fadeColor = new Color(0.6f, 0.86f, 1f)
            };
            timeline.presentLighting = new TimelineManager.LightingProfile
            {
                sunColor = new Color(1f, 0.87f, 0.68f), sunIntensity = 0.95f, ambient = new Color(0.37f, 0.41f, 0.31f),
                fogColor = new Color(0.42f, 0.5f, 0.36f), fogDensity = 0.022f, fadeColor = new Color(0.78f, 0.92f, 0.62f)
            };

            levelInfo.startPoint = start;
            var anchors = Child(systems, "TestAnchors");
            levelInfo.hubSafe = Anchor(anchors, "HubSafe", Cell(14, 10), 0f);
            levelInfo.wallTest = Anchor(anchors, "WallTest", Cell(17, 12), 90f);
            levelInfo.presentBarrierCell = Anchor(anchors, "PresentBarrierCell", Cell(11, 8), 0f);
            levelInfo.pastBarrierCell = Anchor(anchors, "PastBarrierCell", Cell(5, 13), 0f);
            levelInfo.cacheFront = Anchor(anchors, "CacheFront", Cell(6, 9) + new Vector3(0f, 0f, -0.2f), 0f);
            levelInfo.trapPlate = Anchor(anchors, "TrapPlate", Cell(21, 8), 0f);
            levelInfo.spikeStart = Anchor(anchors, "SpikeStart", Cell(20, 11), 90f);
            levelInfo.leafShortcutInside = Anchor(anchors, "LeafShortcutInside", Cell(19, 9), -90f);
            levelInfo.landingA = Anchor(anchors, "LandingA", new Vector3(-9.7f, 0f, 11f), -90f);
            levelInfo.landingB = Anchor(anchors, "LandingB", new Vector3(-18.3f, 0f, 11f), 90f);
            levelInfo.pitDrop = Anchor(anchors, "PitDrop", new Vector3(-15.8f, 0.6f, 11f), 0f);
            levelInfo.heartDoorFront = Anchor(anchors, "HeartDoorFront", new Vector3(0f, 0f, 3.5f), 0f);
            levelInfo.heartFront = Anchor(anchors, "HeartFront", new Vector3(0f, 0f, 14.3f), 0f);
            levelInfo.exitInside = Anchor(anchors, "ExitInside", Cell(14, 17), 180f);
            levelInfo.exitApproach = Anchor(anchors, "ExitApproach", Cell(14, 15), 180f);
            levelInfo.sunKey = sunKey; levelInfo.leafKey = leafKey; levelInfo.chest = chest; levelInfo.heartDoor = keyDoor;
            levelInfo.heart = heart; levelInfo.runeCache = cacheGate; levelInfo.leafShortcut = leafGate; levelInfo.waveShortcut = waveGate;
            levelInfo.heartGate = heartGate; levelInfo.exitGate = exitGate; levelInfo.exitGuide = guide;

            ConfigureRenderSettings(sun, timeline.presentLighting);
            C.past.gameObject.SetActive(false);   // the run starts in the Present
            C.present.gameObject.SetActive(true);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new System.Exception("Could not save scene " + ScenePath);
            C = null;
        }

        static void BuildAudio(AudioManager audio, Transform t, EchoesTuning tuning)
        {
            AudioClip Clip(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/" + n + ".wav");
            audio.tuning = tuning;
            audio.ambience = Clip("temple_ambience");
            audio.damage = Clip("damage");
            audio.defeat = Clip("defeat");
            audio.doorOpen = Clip("door_open");
            audio.footstep = Clip("footstep");
            audio.heartPickup = Clip("heart_pickup");
            audio.interact = Clip("interact");
            audio.keyPickup = Clip("key_pickup");
            audio.platformMove = Clip("platform_move");
            audio.runeSolved = Clip("rune_solved");
            audio.spikeWarning = Clip("spike_warning");
            audio.timelineSwitch = Clip("timeline_switch");
            audio.victory = Clip("victory");
            var amb = Child(t, "Ambience").gameObject.AddComponent<AudioSource>();
            amb.playOnAwake = false; amb.loop = true; amb.spatialBlend = 0f; amb.volume = tuning.ambienceVolume;
            var ui = Child(t, "UI").gameObject.AddComponent<AudioSource>();
            ui.playOnAwake = false; ui.spatialBlend = 0f;
            audio.ambienceSource = amb;
            audio.uiSource = ui;
        }

        static void ReserveCells()
        {
            int[,] cells =
            {
                { 14, 15 }, { 14, 14 }, { 14, 12 }, { 14, 9 }, { 14, 8 }, { 6, 8 }, { 6, 9 }, { 6, 10 }, { 6, 11 }, { 6, 12 }, { 4, 9 }, { 4, 10 }, { 4, 11 },
                { 8, 12 }, { 20, 11 }, { 20, 3 }, { 23, 2 }, { 10, 6 }, { 4, 6 }, { 3, 4 }, { 5, 4 }, { 5, 3 }, { 17, 9 }, { 18, 9 }, { 14, 3 },
                { 13, 3 }, { 15, 3 }, { 14, 4 }, { 11, 8 }, { 18, 11 }, { 5, 13 },
            };
            for (int i = 0; i < cells.GetLength(0); i++) C.reserved.Add(new Vector2Int(cells[i, 0], cells[i, 1]));
        }

        // ================================================================ floors, walls, zones

        static void BuildFloorsAndWalls()
        {
            var pastFloor = Child(C.past, "Floor");
            var presentFloor = Child(C.present, "Floor");
            var pastWalls = Child(C.past, "Walls");
            var presentWalls = Child(C.present, "Walls");
            var pitArt = Child(C.sharedArt, "PitShaft");

            for (int r = 0; r < GridH; r++)
            {
                for (int c = 0; c < GridW; c++)
                {
                    char ch = At(c, r);
                    if (!IsOpen(ch)) continue;
                    Vector3 p = Cell(c, r);
                    if (IsFloor(ch) && ch != 'T')
                    {
                        Place("floor_clean", pastFloor, p, 0f, false, null, true);
                        bool moss = ch == 'c' ? Rand() < 0.75f : Rand() < 0.45f;
                        Place(moss ? "floor_moss" : "floor_clean", presentFloor, p, 90f * C.rng.Next(4), !moss, null, true);
                    }
                    foreach (var d in Dirs)
                    {
                        char n = At(c + d.x, r + d.y);
                        Vector3 wd = WorldDir(d);
                        float yaw = d.x != 0 ? 90f : 0f;
                        if (!IsOpen(n))
                        {
                            Vector3 wallPos = p + wd * 1.3f;
                            Vector3 size = d.x != 0 ? new Vector3(0.6f, 3.2f, 2.1f) : new Vector3(2.1f, 3.2f, 0.6f);
                            BoxObject(C.wallColliders, $"Wall_{c}_{r}_{DirName(d)}", wallPos + Vector3.up * 1.6f, size);
                            bool courtyard = ch == 'c';
                            Place(courtyard ? "wall_ruined" : "wall_clean", pastWalls, wallPos, yaw, false, null, true);
                            bool ruined = courtyard || Rand() < 0.62f;
                            Place(ruined ? "wall_ruined" : "wall_clean", presentWalls, wallPos, yaw, true, null, true);
                            if (ch == '~')
                            {
                                Place("wall_clean", pitArt, wallPos + Vector3.down * 3.05f, yaw, false, null, true);
                            }
                            else
                            {
                                if (Rand() < (courtyard ? 0.4f : 0.18f))
                                    Place("vine", presentWalls, p + wd * 0.86f + Vector3.up * 0.25f, YawFacing(-wd), false, null, true);
                                if (!courtyard && Rand() < 0.09f)
                                    Place("emblem", pastWalls, p + wd * 0.97f + Vector3.up * 1.55f, YawFacing(-wd), false, null, true);
                            }
                        }
                        else if (ch == '~' && n != '~')
                        {
                            // Face of the landing seen from inside the pit.
                            Place("wall_clean", pitArt, p + wd * 1.3f + Vector3.down * 3.05f, yaw, false, null, true);
                        }
                    }
                }
            }

            // Merged floor colliders and safe zones per row.
            for (int r = 0; r < GridH; r++)
            {
                RowRuns(r, IsFloor, (c0, c1) =>
                {
                    var b = BoxObject(C.floorColliders, $"Floor_r{r}_{c0}-{c1}", Mid(c0, r, c1, r) + Vector3.down * 0.5f,
                        new Vector3((c1 - c0 + 1) * 2f, 1f, 2f));
                    b.gameObject.AddComponent<StationaryGround>();
                });
                RowRuns(r, IsSafe, (c0, c1) =>
                {
                    var z = BoxObject(C.safeZones, $"SafeZone_r{r}_{c0}-{c1}", Mid(c0, r, c1, r) + Vector3.up * 0.6f,
                        new Vector3((c1 - c0 + 1) * 2f, 1.6f, 2f), EchoesLayers.Zones, true);
                    z.gameObject.AddComponent<TimelineSafeZone>();
                });
            }

            // Pit floor far below (visual only) - falling is caught by EchoesTuning.fallRespawnY.
            var bottom = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(bottom.GetComponent<Collider>());
            bottom.name = "PitBottom";
            bottom.transform.SetParent(pitArt, false);
            bottom.transform.position = Mid(6, 5, 8, 6) + Vector3.down * 6f;
            bottom.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            bottom.transform.localScale = new Vector3(6f, 4f, 1f);
            bottom.GetComponent<MeshRenderer>().sharedMaterial = C.mats["EE_PitBottom"];
        }

        static string DirName(Vector2Int d) => d.y < 0 ? "N" : d.y > 0 ? "S" : d.x > 0 ? "E" : "W";

        static void RowRuns(int r, System.Func<char, bool> pred, System.Action<int, int> emit)
        {
            int start = -1;
            for (int c = 0; c <= GridW; c++)
            {
                bool ok = c < GridW && pred(At(c, r));
                if (ok && start < 0) start = c;
                if (!ok && start >= 0) { emit(start, c - 1); start = -1; }
            }
        }

        static void BuildTimelineBarriers()
        {
            var presentBlockers = Child(C.present, "SelectedBlockers");
            var pastBlockers = Child(C.past, "SelectedBlockers");
            for (int r = 0; r < GridH; r++)
            {
                for (int c = 0; c < GridW; c++)
                {
                    char ch = At(c, r);
                    if (ch != 'R' && ch != 'W') continue;
                    bool northSouth = !IsOpen(At(c - 1, r)) && !IsOpen(At(c + 1, r));
                    float yaw = northSouth ? 0f : 90f;
                    if (ch == 'R')
                    {
                        var b = Child(presentBlockers, $"RubbleBlocker_{c}_{r}");
                        b.position = Cell(c, r);
                        var col = b.gameObject.AddComponent<BoxCollider>();
                        col.center = new Vector3(0f, 1.5f, 0f);
                        col.size = new Vector3(2f, 3f, 2f);
                        Place("wall_ruined", b, Vector3.zero, yaw, true);
                        Vector3 along = northSouth ? Vector3.forward : Vector3.right;
                        Place("rubble", b, along * 0.7f + Vector3.Cross(Vector3.up, along) * 0.4f, 30f);
                        Place("rubble", b, -along * 0.7f - Vector3.Cross(Vector3.up, along) * 0.3f, 140f, false, new Vector3(1.3f, 1.2f, 1.3f));
                        Place("rubble", b, along * 0.6f - Vector3.Cross(Vector3.up, along) * 0.5f, 250f, false, new Vector3(0.9f, 0.8f, 0.9f));
                        Place("vine", b, -along * 0.32f, YawFacing(-along));
                    }
                    else
                    {
                        var b = Child(pastBlockers, $"MasonryBlocker_{c}_{r}");
                        b.position = Cell(c, r);
                        var col = b.gameObject.AddComponent<BoxCollider>();
                        col.center = new Vector3(0f, 1.5f, 0f);
                        col.size = new Vector3(2f, 3f, 2f);
                        Place("wall_clean", b, Vector3.zero, yaw);
                        Vector3 along = northSouth ? Vector3.forward : Vector3.right;
                        Place("emblem", b, along * 0.33f + Vector3.up * 1.5f, YawFacing(along));
                        Place("emblem", b, -along * 0.33f + Vector3.up * 1.5f, YawFacing(-along));
                    }
                }
            }
        }

        static void BuildLandmarks()
        {
            var pastDecor = Child(C.past, "Decor");
            var presentDecor = Child(C.present, "Decor");

            // Hall pillars (both timelines, with colliders mirrored by query proxies).
            Vector3[] pillars = { new Vector3(-4, 0, 2), new Vector3(4, 0, 2), new Vector3(-4, 0, -2), new Vector3(4, 0, -2) };
            for (int i = 0; i < pillars.Length; i++)
            {
                Place("pillar", pastDecor, pillars[i], 0f, false, null, true);
                Place(i == 0 || i == 3 ? "pillar_broken" : "pillar", presentDecor, pillars[i], 0f, true, null, true);
            }
            // Courtyard pillars and arch (shared arch with upright colliders).
            foreach (var x in new[] { -5f, 5f })
            {
                Place("pillar", pastDecor, new Vector3(x, 0, -14f), 0f, false, null, true);
                Place("pillar_broken", presentDecor, new Vector3(x, 0, -14f), 0f, true, null, true);
            }
            Place("archway", C.sharedArt, new Vector3(0f, 0f, -13.4f), 0f, false, null, true);
            // Heart chamber pillars.
            foreach (var x in new[] { -3f, 3f })
            {
                Place("pillar", pastDecor, new Vector3(x, 0, 18f), 0f, false, null, true);
                Place("pillar", presentDecor, new Vector3(x, 0, 18f), 0f, true, null, true);
            }

            // Dead-end nook (Present only reachable).
            Vector3 nook = Cell(5, 14);
            Place("wood_crate", presentDecor, nook + new Vector3(-0.45f, 0, -0.45f), 20f);
            Place("rubble", presentDecor, nook + new Vector3(0.5f, 0, -0.5f), 70f);
            Place("fern", presentDecor, nook + new Vector3(0.55f, 0, 0.3f), 0f);

            // Sparse vegetation and rubble in the Present; a few ferns in the Past courtyard.
            for (int r = 0; r < GridH; r++)
            {
                for (int c = 0; c < GridW; c++)
                {
                    char ch = At(c, r);
                    if ((ch != '.' && ch != 'c') || C.reserved.Contains(new Vector2Int(c, r))) continue;
                    Vector3 corner = new Vector3(Rand() < 0.5f ? -0.65f : 0.65f, 0f, Rand() < 0.5f ? -0.65f : 0.65f);
                    float roll = Rand();
                    if (roll < 0.10f) Place("rubble", presentDecor, Cell(c, r) + corner, Rand() * 360f, false, Vector3.one * (0.6f + Rand() * 0.5f), true);
                    else if (roll < 0.22f) Place("fern", presentDecor, Cell(c, r) + corner, Rand() * 360f, false, Vector3.one * (0.8f + Rand() * 0.6f), true);
                    if (ch == 'c' && Rand() < 0.15f) Place("fern", pastDecor, Cell(c, r) - corner, Rand() * 360f, false, null, true);
                }
            }
        }

        static Light BuildSun(Transform root)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(root, false);
            go.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.75f;
            l.intensity = 1f;
            return l;
        }

        static List<Light> BuildTorches()
        {
            var lights = new List<Light>();
            var t = Child(C.shared, "Torches");
            Vector3[] spots =
            {
                new Vector3(-6.4f, 0, 2.0f), new Vector3(6.4f, 0, 2.0f), new Vector3(-6.4f, 0, -4.4f), new Vector3(6.4f, 0, -4.4f),
                new Vector3(-20.4f, 0, 4.4f), new Vector3(-20.4f, 0, -2.4f), new Vector3(-4.4f, 0, 18.4f), new Vector3(4.4f, 0, 18.4f),
                new Vector3(18.4f, 0, 13.6f), new Vector3(-22.4f, 0, 9.6f), new Vector3(-6.4f, 0, -9.6f), new Vector3(6.4f, 0, -9.6f),
                new Vector3(16.4f, 0, 0.4f),
            };
            foreach (var p in spots)
            {
                var torch = Place("torch", t, p, 0f);
                var l = PointLight(torch.transform, "Flame", new Vector3(0, 1.35f, 0), new Color(1f, 0.68f, 0.38f), 7f, 1.25f);
                l.gameObject.AddComponent<TorchFlicker>();
                lights.Add(l);
            }
            return lights;
        }

        // ================================================================ gameplay pieces

        static FlagGate BuildGate(string name, Vector3 edgePos, float yaw, string flag)
        {
            var g = Child(C.gameplay, name);
            g.position = edgePos;
            g.rotation = Quaternion.Euler(0f, yaw, 0f);
            Place("door_frame", g, Vector3.zero, 0f);
            var panel = Place("door_panel", g, Vector3.zero, 0f);
            Place("door_lock", panel.transform, new Vector3(0f, 1.15f, 0.22f), 0f);
            var gate = g.gameObject.AddComponent<FlagGate>();
            gate.flag = flag;
            gate.panel = panel.transform;
            gate.blockers = panel.GetComponents<Collider>();
            gate.audioManager = C.audio;
            return gate;
        }

        static void AddNote(FlagGate gate, string prompt, string message)
        {
            var n = gate.gameObject.AddComponent<SealedNote>();
            n.flag = gate.flag;
            n.prompt = prompt;
            n.message = message;
        }

        static SpriteRenderer[] BuildKeySlots(Transform panel)
        {
            var result = new SpriteRenderer[3];
            string[] icons = { "sun", "leaf", "wave" };
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("KeySlot_" + icons[i]);
                go.transform.SetParent(panel, false);
                go.transform.localPosition = new Vector3((1 - i) * 0.55f, 1.95f, 0.25f); // reads SUN, LEAF, WAVE from the hall
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                go.transform.localScale = Vector3.one * 0.32f;
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UIDir + "/" + icons[i] + ".png");
                result[i] = sr;
            }
            return result;
        }

        static KeyPickup BuildKey(KeyId key, Vector3 pos, float yaw, string requiredFlag, string grantDoor)
        {
            string n = EchoesIds.KeyName(key);
            var root = Child(C.gameplay, "KeyPickup_" + n);
            root.position = pos;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            Place("key_pedestal", root, Vector3.zero, 0f);
            var holder = Child(root, "KeyVisual");
            holder.localPosition = new Vector3(0f, 0.95f, 0f);
            holder.gameObject.AddComponent<PickupBob>();
            var k = Place("key_" + n, holder, Vector3.zero, 0f);
            var light = PointLight(root, "Glow", new Vector3(0f, 1.5f, 0f), EchoesIds.KeyColor(key), 3.5f, 1.4f);
            var kp = root.gameObject.AddComponent<KeyPickup>();
            kp.key = key;
            kp.requiredFlag = requiredFlag;
            kp.grantDoor = grantDoor;
            kp.visual = holder.gameObject;
            kp.pickupColliders = k.GetComponents<Collider>();
            kp.glow = light;
            return kp;
        }

        static RunePuzzle BuildRunePuzzle()
        {
            var root = Child(C.gameplay, "RunePuzzle");
            root.position = Cell(6, 10);
            var puzzle = root.gameObject.AddComponent<RunePuzzle>();
            puzzle.session = C.session;
            puzzle.solution = new[] { KeyId.Sun, KeyId.Wave, KeyId.Leaf }; // level_spec.json rune_sequence

            // Present: three interactive tablets in a deliberately different order.
            var console = Child(C.present, "RuneConsole");
            var controls = new List<RuneControl>();
            (KeyId glyph, float z)[] layout = { (KeyId.Leaf, 3f), (KeyId.Sun, 1f), (KeyId.Wave, -1f) };
            foreach (var (glyph, z) in layout)
            {
                string n = EchoesIds.KeyName(glyph);
                var p = new Vector3(-16f, 0f, z);
                Place("pressure_plate", console, p, 90f, true);
                var tab = Place("rune_" + n, console, p + Vector3.up * 0.18f, 90f, true);
                tab.name = "RuneControl_" + n;
                var rc = tab.AddComponent<RuneControl>();
                rc.puzzle = puzzle;
                rc.glyph = glyph;
                rc.glyphRenderer = tab.GetComponent<MeshRenderer>();
                rc.glyphLight = PointLight(tab.transform, "GlyphLight", new Vector3(0f, 0.8f, 0.5f), new Color(0.35f, 0.7f, 1f), 2.2f, 0.35f);
                controls.Add(rc);
            }
            puzzle.controls = controls.ToArray();

            // Past: the clue wall shows SUN, WAVE, LEAF from left to right (facing west).
            var clue = Child(C.past, "RuneClueWall");
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            (KeyId glyph, float z, string numeral)[] clueLayout = { (KeyId.Sun, -0.6f, "I"), (KeyId.Wave, 1.0f, "II"), (KeyId.Leaf, 2.6f, "III") };
            foreach (var (glyph, z, numeral) in clueLayout)
            {
                string n = EchoesIds.KeyName(glyph);
                Place("rune_" + n, clue, new Vector3(-20.75f, 0f, z), 90f);
                var icon = new GameObject("ClueGlyph_" + n);
                icon.transform.SetParent(clue, false);
                icon.transform.localPosition = new Vector3(-20.62f, 1.75f, z);
                icon.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                icon.transform.localScale = Vector3.one * 0.42f;
                var sr = icon.AddComponent<SpriteRenderer>();
                sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UIDir + "/" + n + ".png");
                sr.color = new Color(0.7f, 0.9f, 1f);
                var label = new GameObject("ClueOrder_" + numeral);
                label.transform.SetParent(clue, false);
                label.transform.localPosition = new Vector3(-20.62f, 2.35f, z);
                label.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                var tm = label.AddComponent<TextMesh>();
                tm.text = numeral;
                tm.font = font;
                tm.fontSize = 64;
                tm.characterSize = 0.06f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(0.6f, 0.85f, 1f);
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            Place("emblem", clue, new Vector3(-20.95f, 2.6f, 1.0f), 90f);
            PointLight(clue, "ClueLight", new Vector3(-19.4f, 2.2f, 1.0f), new Color(0.45f, 0.75f, 1f), 6f, 1.5f);
            return puzzle;
        }

        static TimedSpikeTrap[] BuildTraps()
        {
            var traps = new List<TimedSpikeTrap>();
            int[] rows = { 10, 8, 6 };
            float[] delays = { 0f, 1.4f, 2.8f };
            var parent = Child(C.gameplay, "SpikeTraps");
            for (int i = 0; i < rows.Length; i++)
            {
                int row = rows[i];
                var root = Child(parent, "SpikeTrap_" + i);
                root.position = Mid(21, row, 22, row);
                var trap = root.gameObject.AddComponent<TimedSpikeTrap>();
                var spikes = new List<Transform>();
                var renderers = new List<Renderer>();
                foreach (int col in new[] { 21, 22 })
                {
                    Vector3 local = Cell(col, row) - root.position;
                    var plate = Place("spike_base", root, local, 0f);
                    renderers.Add(plate.GetComponent<MeshRenderer>());
                    var s = Place("spikes", root, local + Vector3.up * trap.hiddenY, 0f);
                    spikes.Add(s.transform);
                }
                BoxObject(root, "DamageZone", new Vector3(0f, 0.5f, 0f), new Vector3(3.6f, 1f, 1.6f), EchoesLayers.Zones, true);
                trap.tuning = C.tuning;
                trap.audioManager = C.audio;
                trap.startDelay = delays[i];
                trap.spikes = spikes.ToArray();
                trap.warningRenderers = renderers.ToArray();
                trap.warningLight = PointLight(root, "WarningLight", new Vector3(0f, 0.6f, 0f), new Color(1f, 0.3f, 0.2f), 4f, 0f);
                traps.Add(trap);
            }
            return traps.ToArray();
        }

        static MovingPlatform BuildPlatform(out Transform endA, out Transform endB)
        {
            var parent = Child(C.gameplay, "PlatformChamber");
            endA = Anchor(parent, "EndpointA_East", new Vector3(-12.2f, 0f, 11f), 0f);
            endB = Anchor(parent, "EndpointB_West", new Vector3(-15.8f, 0f, 11f), 0f);
            var p = Child(parent, "MovingPlatform");
            p.position = endA.position;
            p.gameObject.layer = EchoesLayers.Platform;
            var rb = p.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
            Place("moving_platform", p, Vector3.zero, 0f);
            Place("platform_rail", p, new Vector3(0f, 0f, 1.11f), 90f);
            Place("platform_rail", p, new Vector3(0f, 0f, -1.11f), 90f);
            var mp = p.gameObject.AddComponent<MovingPlatform>();
            mp.tuning = C.tuning;
            mp.audioManager = C.audio;
            mp.endA = endA;
            mp.endB = endB;
            // Landing markers so endpoints are visible.
            PointLight(parent, "LandingA_Light", new Vector3(-10f, 1.2f, 11f), new Color(0.4f, 0.75f, 1f), 3.5f, 0.8f);
            PointLight(parent, "LandingB_Light", new Vector3(-18f, 1.2f, 11f), new Color(0.4f, 0.75f, 1f), 3.5f, 0.8f);
            return mp;
        }

        static ChestInteractable BuildChest()
        {
            var root = Child(C.gameplay, "ChestRoot");
            root.position = Cell(3, 4) + new Vector3(0.1f, 0f, -0.1f);
            root.rotation = Quaternion.Euler(0f, 90f, 0f);
            Place("chest_base", root, Vector3.zero, 0f);
            var lid = Child(root, "LidPivot");
            lid.localPosition = new Vector3(0f, 0.6f, -0.4f);
            Place("chest_lid", lid, Vector3.zero, 0f);
            var reward = Child(root, "RewardKey");
            reward.localPosition = new Vector3(0f, 0.35f, 0f);
            var key = Place("key_wave", reward, Vector3.zero, 0f);
            foreach (var c in key.GetComponents<Collider>()) c.enabled = false;
            reward.gameObject.SetActive(false);
            PointLight(root, "ChestLight", new Vector3(0f, 1.6f, 0.8f), new Color(1f, 0.8f, 0.5f), 3.5f, 0.9f);
            var chest = root.gameObject.AddComponent<ChestInteractable>();
            chest.lid = lid;
            chest.rewardVisual = reward.gameObject;
            return chest;
        }

        static HeartPickup BuildHeart()
        {
            var root = Child(C.gameplay, "HeartOfEternity");
            root.position = Cell(14, 3);
            Place("heart_pedestal", root, Vector3.zero, 0f);
            var vis = Child(root, "HeartVisual");
            vis.localPosition = new Vector3(0f, 0.97f, 0f);
            var bob = vis.gameObject.AddComponent<PickupBob>();
            bob.spin = 25f;
            var h = Place("heart", vis, Vector3.zero, 0f);
            SetMaterial(h, C.mats["EE_GoldGlow"]);
            var light = PointLight(root, "HeartGlow", new Vector3(0f, 1.7f, 0f), new Color(1f, 0.78f, 0.4f), 7f, 2.2f);
            var hp = root.gameObject.AddComponent<HeartPickup>();
            hp.visual = vis.gameObject;
            hp.pickupColliders = h.GetComponents<Collider>();
            hp.glow = light;
            return hp;
        }

        static GameObject BuildExitGuide()
        {
            var controller = Child(C.gameplay, "ExitGuideController");
            var guide = Child(C.gameplay, "ExitGuide");
            Vector3[] lamps =
            {
                new Vector3(-1.5f, 0, 3f), new Vector3(1.5f, 0, 3f), new Vector3(-1.5f, 0, -1f), new Vector3(1.5f, 0, -1f),
                new Vector3(-1.5f, 0, -4.6f), new Vector3(1.5f, 0, -4.6f), new Vector3(-0.7f, 0, -6.6f), new Vector3(0.7f, 0, -6.6f),
            };
            foreach (var p in lamps)
            {
                var lamp = Place("checkpoint", guide, p, 0f, false, new Vector3(0.35f, 0.6f, 0.35f));
                SetMaterial(lamp, C.mats["EE_Lamp"]);
                PointLight(lamp.transform, "LampLight", new Vector3(0f, 1f, 0f), new Color(0.35f, 0.7f, 1f), 3.5f, 1.6f);
            }
            var marker = Place("emblem", guide, new Vector3(0f, 3.35f, -8.62f), 0f, false, Vector3.one * 1.4f);
            SetMaterial(marker, C.mats["EE_GoldGlow"]);
            PointLight(guide, "ExitMarkerLight", new Vector3(0f, 3f, -7.6f), new Color(1f, 0.8f, 0.45f), 7f, 2.2f);
            var act = controller.gameObject.AddComponent<FlagActivator>();
            act.flag = EchoesIds.FlagHeart;
            act.enableWhenSet = new[] { guide.gameObject };
            guide.gameObject.SetActive(false);
            return guide.gameObject;
        }

        static Checkpoint BuildCheckpoint(string id, int c, int r, Timeline t, float yaw)
        {
            var parent = C.gameplay.Find("Checkpoints") ?? Child(C.gameplay, "Checkpoints");
            var root = Child(parent, "Checkpoint_" + id);
            root.position = Cell(c, r);
            var cp = root.gameObject.AddComponent<Checkpoint>();
            cp.id = id;
            cp.restoreTimeline = t;
            var spawn = Child(root, "Spawn");
            spawn.localPosition = new Vector3(0f, 0.05f, 0f);
            spawn.rotation = Quaternion.Euler(0f, yaw, 0f);
            cp.spawnPoint = spawn;
            Place("checkpoint", root, Vector3.zero, 0f, false, new Vector3(1f, 0.16f, 1f));
            cp.markerLight = PointLight(root, "MarkerLight", new Vector3(0f, 0.8f, 0f), new Color(0.3f, 0.65f, 1f), 3f, 0.7f);
            BoxObject(root, "ActivationZone", new Vector3(0f, 0.75f, 0f), new Vector3(1.9f, 1.5f, 1.9f), EchoesLayers.Zones, true);
            return cp;
        }

        static void BuildHints()
        {
            Hint("Hint_Start", Cell(14, 13), new Vector3(4f, 2f, 2f),
                "Press Q on solid stone floor to shift between the Present and the Past. Rubble that blocks a path now may not exist in the Past.");
            Hint("Hint_Gallery", Mid(9, 11, 10, 11), new Vector3(4f, 2f, 2f),
                "The Rune Gallery. The tablets here have faded... perhaps the intact Past remembers their order.");
            Hint("Hint_Spikes", Mid(19, 11, 20, 11), new Vector3(4f, 2f, 2f),
                "Spike Passage. A red glow warns that spikes will rise - cross while the plates are dark.");
            Hint("Hint_Platform", Cell(11, 7), new Vector3(2f, 2f, 2f),
                "Step onto the platform while it rests at the landing. You cannot shift time while riding.");
            Hint("Hint_DeadEnd", Cell(5, 14), new Vector3(2f, 2f, 2f),
                "A collapsed dead end. Only roots and dust here.");
            Hint("Hint_HeartDoor", Cell(14, 9), new Vector3(2f, 2f, 2f),
                "The Heart chamber door bears three glyph locks: Sun, Leaf and Wave.");
        }

        static void Hint(string name, Vector3 center, Vector3 size, string message)
        {
            var b = BoxObject(C.hints, name, center + Vector3.up * size.y * 0.5f, size, EchoesLayers.Zones, true);
            b.gameObject.AddComponent<HintZone>().message = message;
        }

        static GameObject BuildPlayer(Transform start, EchoesTuning tuning, out FirstPersonMotor motor, out PlayerZoneSensor sensor,
            out PlayerInteractor interactor, out Camera cam)
        {
            var player = new GameObject("Player") { layer = EchoesLayers.Player };
            player.transform.SetParent(C.root, false);
            player.transform.position = start.position;
            player.transform.rotation = start.rotation;
            var cc = player.AddComponent<CharacterController>();
            cc.radius = 0.35f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.slopeLimit = 45f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;
            var pivot = Child(player.transform, "CameraPivot");
            pivot.localPosition = new Vector3(0f, 1.6f, 0f);
            pivot.gameObject.tag = "MainCamera";
            cam = pivot.gameObject.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 250f;
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.Skybox;
            pivot.gameObject.AddComponent<AudioListener>();

            motor = player.AddComponent<FirstPersonMotor>();
            motor.tuning = tuning;
            motor.cameraPivot = pivot;
            motor.audioManager = C.audio;
            sensor = player.AddComponent<PlayerZoneSensor>();
            sensor.motor = motor;
            interactor = player.AddComponent<PlayerInteractor>();
            interactor.session = C.session;
            interactor.viewCamera = cam;
            interactor.tuning = tuning;
            return player;
        }

        static void BuildProxies(Transform timelineRoot, Transform proxyRoot, Timeline t)
        {
            int layer = t == Timeline.Past ? EchoesLayers.QueryPast : EchoesLayers.QueryPresent;
            foreach (var col in timelineRoot.GetComponentsInChildren<Collider>(true))
            {
                if (col.isTrigger) continue;
                var go = new GameObject("Proxy_" + col.name) { layer = layer };
                go.transform.SetParent(proxyRoot, false);
                go.transform.position = col.transform.position;
                go.transform.rotation = col.transform.rotation;
                go.transform.localScale = col.transform.lossyScale;
                switch (col)
                {
                    case BoxCollider b:
                        var nb = go.AddComponent<BoxCollider>(); nb.center = b.center; nb.size = b.size; break;
                    case CapsuleCollider cap:
                        var nc = go.AddComponent<CapsuleCollider>(); nc.center = cap.center; nc.radius = cap.radius; nc.height = cap.height; nc.direction = cap.direction; break;
                    case SphereCollider sph:
                        var ns = go.AddComponent<SphereCollider>(); ns.center = sph.center; ns.radius = sph.radius; break;
                    default:
                        go.transform.rotation = Quaternion.identity;
                        go.transform.localScale = Vector3.one;
                        go.transform.position = col.bounds.center;
                        go.AddComponent<BoxCollider>().size = col.bounds.size; break;
                }
                var px = go.AddComponent<TimelineBlockerProxy>();
                px.timeline = t;
                px.source = col;
            }
        }

        static void ConfigureRenderSettings(Light sun, TimelineManager.LightingProfile p)
        {
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
            sun.color = p.sunColor;
            sun.intensity = p.sunIntensity;

            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
            if (ls == null)
            {
                ls = new LightingSettings { name = "EchoesLighting", bakedGI = false, realtimeGI = false };
                AssetDatabase.CreateAsset(ls, LightingPath);
            }
            Lightmapping.lightingSettings = ls;
        }
    }
}
