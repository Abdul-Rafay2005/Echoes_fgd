using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Echoes
{
    /// <summary>
    /// Automated acceptance run inside the real game (Editor Play Mode or standalone player).
    /// Inactive unless the player is launched with  -echoesAutoTest  [-echoesTestReport path/report.txt].
    /// Uses simulated input and a separate save folder, drives the actual scene, writes a PASS/FAIL report
    /// plus screenshots, then quits with exit code 0 (all passed) or 3 (failures).
    /// </summary>
    public class EchoesAutoTest : MonoBehaviour
    {
        static bool runnerCreated;
        bool isRunner;

        readonly StringBuilder report = new StringBuilder();
        int passed, failed;
        string reportPath, shotDir;
        float deadline;
        bool finished;
        int shotIndex;

        static GameSession S => GameSession.Instance;
        static EchoesLevelInfo L => S != null ? S.GetComponent<EchoesLevelInfo>() : null;

        public static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        void Awake()
        {
            if (isRunner || !HasArg("-echoesAutoTest")) return;
            // Isolate saves/settings before GameSession.Start reads them.
            string dir = Path.Combine(Application.persistentDataPath, "autotest_saves");
            SaveService.DirectoryOverride = dir;
            SaveService.MemorySettings = true;
            EchoesInput.Simulated = true;
            if (runnerCreated) return;
            runnerCreated = true;
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { }
            var go = new GameObject("EchoesAutoTestRunner");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<EchoesAutoTest>();
            runner.isRunner = true;
        }

        void Start()
        {
            if (!isRunner)
            {
                // Scene instance: block real mouse/keyboard UI input so nothing interferes with the run.
                if (HasArg("-echoesAutoTest") && UnityEngine.EventSystems.EventSystem.current != null)
                    UnityEngine.EventSystems.EventSystem.current.enabled = false;
                return;
            }
            Application.runInBackground = true;
            reportPath = Arg("-echoesTestReport") ?? Path.Combine(Application.persistentDataPath, "autotest_report.txt");
            shotDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)), "screenshots");
            Directory.CreateDirectory(shotDir);
            deadline = Time.realtimeSinceStartup + 420f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Line("Echoes of Eternity automated acceptance run");
            Line("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "   Unity " + Application.unityVersion + "   Platform " + Application.platform);
            Line("Mode: " + (Application.isBatchMode ? "headless (-batchmode)" : "windowed") + "   Device: " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.operatingSystem);
            StartCoroutine(RunAll());
        }

        void Update()
        {
            if (isRunner && !finished && Time.realtimeSinceStartup > deadline)
            {
                Check(false, "Watchdog", "test run exceeded time limit");
                Finish();
            }
        }

        // ------------------------------------------------------------------ helpers

        void Line(string s)
        {
            report.AppendLine(s);
            Debug.Log("[EchoesAutoTest] " + s);
        }

        bool Check(bool ok, string name, string detail = "")
        {
            if (ok) passed++; else failed++;
            Line((ok ? "PASS  " : "FAIL  ") + name + (string.IsNullOrEmpty(detail) ? "" : "   (" + detail + ")"));
            return ok;
        }

        void Info(string s) => Line("INFO  " + s);

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Real(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        bool waitOk;
        IEnumerator Until(Func<bool> cond, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            waitOk = false;
            while (Time.realtimeSinceStartup < end)
            {
                if (S != null && cond()) { waitOk = true; yield break; }
                yield return null;
            }
        }

        IEnumerator Shot(string label)
        {
            if (Application.isBatchMode) yield break; // headless run: nothing is rendered
            yield return new WaitForEndOfFrame();
            string file = Path.Combine(shotDir, $"{++shotIndex:00}_{label}_{Screen.width}x{Screen.height}.png");
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Destroy(tex);
            }
            catch (Exception e) { Info("Screenshot failed: " + e.Message); }
            yield return null;
        }

        IEnumerator Reload(GameSession.LaunchAction action)
        {
            var old = S;
            switch (action)
            {
                case GameSession.LaunchAction.NewGame: old.RequestNewGame(); break;
                case GameSession.LaunchAction.Continue: old.RequestContinue(); break;
                default: old.RequestMainMenu(); break;
            }
            yield return Until(() => S != null && S != old, 10f);
            yield return Frames(5);
        }

        IEnumerator Begin()
        {
            S.menus.OnBegin();
            yield return Frames(3);
        }

        IEnumerator Tp(Vector3 pos, float yaw)
        {
            EchoesInput.SimMove = Vector2.zero;
            S.motor.Teleport(pos, yaw);
            yield return Frames(8);
        }

        IEnumerator Tp(Transform t) => Tp(t.position, t.eulerAngles.y);

        IEnumerator Walk(Vector2 dir, float seconds)
        {
            EchoesInput.SimMove = dir;
            float end = Time.time + seconds;
            float realEnd = Time.realtimeSinceStartup + seconds * 3f + 1f;
            // Stop early if the game leaves Playing (e.g. victory freezes time).
            while (Time.time < end && Time.realtimeSinceStartup < realEnd && S != null &&
                   (S.State == GameState.Playing || S.State == GameState.Transitioning))
                yield return null;
            EchoesInput.SimMove = Vector2.zero;
            yield return Frames(3);
        }

        IEnumerator PressE(Vector3 stand, Vector3 look)
        {
            yield return Tp(stand, 0f);
            S.motor.LookAt(look);
            yield return Frames(3);
            yield return Until(() => !S.interactor.IsBusy, 1f);
            EchoesInput.SimPress(KeyCode.E);
            yield return Frames(4);
        }

        IEnumerator SwitchTo(Timeline target)
        {
            if (S.timeline.Current == target) yield break;
            yield return Until(() => S.timeline.CanSwitch(out _), 2.5f);
            EchoesInput.SimPress(KeyCode.Q);
            yield return Until(() => S.timeline.Current == target && !S.timeline.IsTransitioning && S.State == GameState.Playing, 4f);
            if (!waitOk) Check(false, "Switch to " + target, S.timeline.LastRejection);
        }

        Vector3 P => S.motor.transform.position;
        static bool CursorFree => Application.isBatchMode || Cursor.lockState == CursorLockMode.None;
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        bool UiInsideScreen(out string offender)
        {
            offender = null;
            var corners = new Vector3[4];
            foreach (var g in S.hud.GetComponentsInChildren<Graphic>(false))
            {
                var rt = g.rectTransform;
                if (!g.enabled || rt.anchorMin != rt.anchorMax) continue; // stretched backgrounds always fit
                if (g.GetComponentInParent<CanvasGroup>() is CanvasGroup cg && cg.alpha <= 0.01f) continue;
                rt.GetWorldCorners(corners);
                foreach (var c in corners)
                {
                    if (c.x < -2f || c.y < -2f || c.x > Screen.width + 2f || c.y > Screen.height + 2f)
                    {
                        offender = g.name + " " + c;
                        return false;
                    }
                }
            }
            return true;
        }

        IEnumerator Resolution(int w, int h, string label)
        {
            if (Application.isBatchMode)
            {
                Info($"SKIP UI fit at {w}x{h} ({label}): headless run - use the windowed test for layout and screenshots");
                yield break;
            }
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            yield return Real(0.8f);
            Info($"Requested {w}x{h}, got {Screen.width}x{Screen.height}");
            Check(UiInsideScreen(out string offender), $"UI fits on screen at {Screen.width}x{Screen.height} ({label})", offender);
            yield return Shot(label);
        }

        // ------------------------------------------------------------------ test run

        IEnumerator RunAll()
        {
            yield return Until(() => S != null, 10f);
            yield return Frames(10);
            IEnumerator[] phases =
            {
                PhaseMenu(), PhaseMovement(), PhaseTimeline(), PhaseRune(), PhaseSpikes(), PhasePlatform(),
                PhaseFallAndDeath(), PhaseSaves(), PhaseHeartAndExit(), PhaseSaveRobustness(), PhaseKeyOrders(),
            };
            foreach (var phase in phases)
            {
                yield return RunGuarded(phase);
                if (finished) yield break;
            }
            Finish();
        }

        IEnumerator RunGuarded(IEnumerator phase)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!phase.MoveNext()) yield break;
                    current = phase.Current;
                }
                catch (Exception e)
                {
                    Check(false, "Exception", e.ToString());
                    EchoesInput.ResetSimulation();
                    yield break;
                }
                yield return current;
            }
        }

        IEnumerator PhaseMenu()
        {
            Line("--- Menu and UI");
            Check(S.State == GameState.Menu && S.menus.mainPanel.activeSelf, "Boots into main menu");
            Check(!S.HasValidSave && !S.menus.continueButton.interactable, "Continue disabled without a save");
            Check(Time.timeScale == 0f && CursorFree, "Menu freezes time and frees the cursor");
            yield return Resolution(1280, 720, "menu");
            yield return Resolution(1920, 1080, "menu");
            S.menus.OnControls();
            yield return Frames(2);
            Check(S.menus.controlsPanel.activeSelf, "Controls panel opens");
            S.menus.OnBack();
            yield return Frames(2);
            Check(S.menus.mainPanel.activeSelf, "Controls back returns to main menu");
            S.menus.OnVolumeChanged(0.37f);

            yield return Reload(GameSession.LaunchAction.NewGame);
            Check(S.State == GameState.Menu && S.menus.introPanel.activeSelf, "New Game shows the introduction");
            yield return Shot("intro");
            yield return Begin();
            Check(S.State == GameState.Playing && Time.timeScale == 1f, "Begin enters Playing");
            Check(S.Health == 3 && S.Progress.KeyCount == 0 && S.Progress.score == 0, "New run: 3 HP, 0 keys, score 0");
            Check(S.timeline.Current == Timeline.Present, "Run starts in the Present");
            Check(L.exitGate.blockers[0].enabled, "Entrance is sealed");
            Info("Cursor lock state while playing: " + Cursor.lockState + " (depends on window focus)");
            yield return Real(1.0f);
            yield return Resolution(1280, 720, "hud_present");
            yield return Resolution(1920, 1080, "hud_present");
            yield return Resolution(1280, 720, "windowed");
        }

        IEnumerator PhaseMovement()
        {
            Line("--- First-person movement");
            yield return Tp(L.hubSafe);
            Vector3 a = P;
            yield return Walk(new Vector2(0f, 1f), 0.5f);
            float straight = Flat(a, P);
            yield return Tp(L.hubSafe);
            a = P;
            yield return Walk(new Vector2(1f, 1f), 0.5f);
            float diagonal = Flat(a, P);
            Check(straight > 1.5f, "WASD moves the player", $"{straight:0.00} m in 0.5 s");
            Check(diagonal <= straight * 1.05f, "Diagonal movement is not faster", $"straight {straight:0.00} diagonal {diagonal:0.00}");

            yield return Tp(L.wallTest);
            float wallX = L.wallTest.position.x + 1f; // inner face of the east wall
            yield return Walk(new Vector2(0f, 1f), 1.2f);
            Check(P.x <= wallX - 0.25f, "Walls block the player", $"x={P.x:0.00}, wall face {wallX:0.00}");

            EchoesInput.SimLook = new Vector2(0f, 900f);
            yield return Frames(2);
            float up = S.motor.Pitch;
            EchoesInput.SimLook = new Vector2(0f, -900f);
            yield return Frames(2);
            float down = S.motor.Pitch;
            EchoesInput.SimLook = Vector2.zero;
            Check(Mathf.Abs(up) <= S.tuning.maxPitch + 0.01f && Mathf.Abs(down) <= S.tuning.maxPitch + 0.01f, "Camera pitch is clamped", $"{up:0.0} / {down:0.0}");

            // Pause / resume via Escape
            EchoesInput.SimPress(KeyCode.Escape);
            yield return Frames(3);
            Check(S.State == GameState.Paused && Time.timeScale == 0f && CursorFree && S.menus.pausePanel.activeSelf, "Escape pauses and releases the cursor");
            yield return Shot("pause");
            EchoesInput.SimPress(KeyCode.Escape);
            yield return Frames(3);
            Check(S.State == GameState.Playing && Time.timeScale == 1f, "Escape resumes");
        }

        IEnumerator PhaseTimeline()
        {
            Line("--- Timeline switching");
            yield return Tp(L.hubSafe);
            Check(S.timeline.CanSwitch(out string r0), "Q allowed on safe shared floor", r0);
            int before = S.timeline.SwitchCount;
            EchoesInput.SimPress(KeyCode.Q);
            yield return Frames(2);
            Check(S.State == GameState.Transitioning, "Q starts a transition");
            EchoesInput.SimPress(KeyCode.Q);
            yield return Frames(2);
            EchoesInput.SimPress(KeyCode.Q);
            yield return Until(() => S.State == GameState.Playing && !S.timeline.IsTransitioning, 3f);
            yield return Real(0.3f);
            Check(S.timeline.Current == Timeline.Past && S.timeline.SwitchCount == before + 1, "Rapid Q presses produce exactly one switch", "switches " + (S.timeline.SwitchCount - before));
            Check(S.timeline.pastRoot.activeSelf && !S.timeline.presentRoot.activeSelf, "Past root shown, Present root hidden");
            Check(S.hud.timelineText.text.StartsWith("PAST"), "HUD shows the timeline name");
            yield return Shot("hud_past_hall");

            yield return Tp(L.hubSafe.position + Vector3.up * 3f, 0f);
            yield return Frames(1);
            bool airborne = !S.timeline.CanSwitch(out string rAir);
            Check(airborne && !S.timeline.TrySwitch() && S.timeline.Current == Timeline.Past, "Q rejected while airborne", rAir);
            yield return Frames(40);

            yield return Tp(L.presentBarrierCell);
            Check(S.timeline.HasStationarySupport(P), "Barrier cell has shared floor support");
            bool blocked = !S.timeline.CanSwitch(out string rBlock);
            Check(blocked && !S.timeline.TrySwitch() && S.timeline.Current == Timeline.Past, "Switch rejected when Present rubble would overlap the player", rBlock);
            Check(S.timeline.pastRoot.activeSelf && !S.timeline.presentRoot.activeSelf, "Rejected switch leaves the world unchanged");

            yield return Tp(L.hubSafe);
            yield return SwitchTo(Timeline.Present);
            Check(S.timeline.Current == Timeline.Present, "Switch back to the Present");
            yield return Tp(L.pastBarrierCell);
            bool blocked2 = !S.timeline.CanSwitch(out string rBlock2);
            Check(blocked2 && S.timeline.Current == Timeline.Present, "Switch rejected when Past masonry would overlap the player", rBlock2);
        }

        RuneControl Control(KeyId g)
        {
            foreach (var c in S.runePuzzles[0].controls) if (c.glyph == g) return c;
            return null;
        }

        IEnumerator PressRune(KeyId g)
        {
            var c = Control(g);
            Vector3 pos = c.transform.position;
            yield return PressE(new Vector3(pos.x + 1.3f, 0.05f, pos.z), pos + Vector3.up * 0.75f);
        }

        IEnumerator PhaseRune()
        {
            Line("--- Rune Gallery");
            var puzzle = S.runePuzzles[0];
            yield return Tp(L.cacheFront);
            S.motor.LookAt(L.sunKey.transform.position + Vector3.up * 1.35f);
            yield return Frames(3);
            var through = S.interactor.FindTarget();
            Check(!(through is KeyPickup), "Cannot interact with the key through the closed cache door", through != null ? through.GetType().Name : "none");

            yield return PressRune(KeyId.Sun);
            Check(puzzle.CurrentInput.Count == 1, "Rune press registers");
            yield return PressRune(KeyId.Leaf);
            Check(puzzle.CurrentInput.Count == 0 && !puzzle.Solved, "Wrong order resets the unfinished sequence");
            Check(S.Progress.KeyCount == 0, "Wrong order gives no key");
            yield return PressRune(KeyId.Sun);
            yield return PressRune(KeyId.Wave);
            yield return Shot("rune_input");
            yield return PressRune(KeyId.Leaf);
            Check(puzzle.Solved, "SUN, WAVE, LEAF solves the Rune Gallery");
            yield return Real(1.6f);
            Check(L.runeCache.IsOpen && !L.runeCache.blockers[0].enabled, "Cache opens");
            Vector3 key = L.sunKey.transform.position;
            yield return PressE(key + new Vector3(0f, 0.05f, -1.25f), key + Vector3.up * 1.35f);
            Check(S.Progress.HasKey(KeyId.Sun) && S.Progress.KeyCount == 1 && S.Progress.score == 100, "SUN key awarded (+100)", "score " + S.Progress.score);
            EchoesInput.SimPress(KeyCode.E);
            yield return Frames(4);
            yield return PressRune(KeyId.Sun);
            Check(S.Progress.KeyCount == 1 && S.Progress.score == 100, "SUN key and score awarded only once");

            yield return PressE(L.heartDoorFront.position, L.heartGate.transform.position + Vector3.up * 1.4f);
            Check(!S.Progress.HasFlag(EchoesIds.DoorHeart) && L.heartGate.blockers[0].enabled, "Heart door rejects 1 key");

            // Clue wall view in the Past for the record.
            yield return Tp(new Vector3(-14.5f, 0.05f, 1f), -90f);
            yield return SwitchTo(Timeline.Past);
            yield return Real(0.3f);
            yield return Shot("rune_clue_past");
            yield return SwitchTo(Timeline.Present);
        }

        IEnumerator PhaseSpikes()
        {
            Line("--- Spike Passage");
            yield return Tp(L.hubSafe);
            yield return SwitchTo(Timeline.Past);
            var trap = S.traps[1];
            // Step onto the plate only at the start of a safe phase so the first hit is observed.
            yield return Until(() => trap.CurrentPhase == TimedSpikeTrap.Phase.Recovery, 8f);
            yield return Until(() => trap.CurrentPhase == TimedSpikeTrap.Phase.Safe, 4f);
            int h0 = S.Health;
            yield return Tp(L.trapPlate);
            Check(!S.timeline.CanSwitch(out string r), "Q rejected on a trap plate", r);
            Check(S.Health == h0, "Safe phase does not damage", "health " + S.Health);
            bool sawWarning = false, warningBeforeActive = false, hit = false;
            float hitTime = 0f;
            var last = trap.CurrentPhase;
            float end = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < end && !hit)
            {
                if (trap.CurrentPhase == TimedSpikeTrap.Phase.Warning) sawWarning = true;
                if (trap.CurrentPhase == TimedSpikeTrap.Phase.Active && last != TimedSpikeTrap.Phase.Active) warningBeforeActive = sawWarning;
                if (S.Health < h0) { hit = true; hitTime = Time.time; }
                last = trap.CurrentPhase;
                if (trap.CurrentPhase == TimedSpikeTrap.Phase.Warning && !sawWarningShot) { sawWarningShot = true; StartCoroutine(Shot("spike_warning")); }
                yield return null;
            }
            Check(hit, "Active spikes damage the player");
            Check(warningBeforeActive, "Visible warning phase precedes damage");
            yield return Real(0.6f);
            Check(S.Health == h0 - 1, "Damage immunity prevents instant repeated hits", "health " + S.Health);
            yield return Tp(L.spikeStart);

            Vector3 leaf = L.leafKey.transform.position;
            Vector3 toward = (new Vector3(14f, 0f, 16f) - leaf).normalized;
            yield return PressE(leaf + toward * 1.25f + Vector3.up * 0.05f, leaf + Vector3.up * 1.35f);
            Check(S.Progress.HasKey(KeyId.Leaf) && S.Progress.KeyCount == 2 && S.Progress.score == 200, "LEAF key awarded (+100)");
            Check(S.Progress.HasFlag(EchoesIds.ShortcutLeaf), "LEAF key opens the permanent shortcut");
            yield return Real(1.6f);
            yield return Tp(L.leafShortcutInside);
            yield return Walk(new Vector2(0f, 1f), 1.3f);
            Check(P.x < L.leafShortcut.transform.position.x - 0.5f, "Shortcut leads back into the hall", $"x={P.x:0.00}");

            yield return Tp(L.hubSafe);
            yield return SwitchTo(Timeline.Present);
            Check(S.Progress.KeyCount == 2 && L.leafShortcut.IsOpen && L.runeCache.IsOpen && S.runePuzzles[0].Solved, "Keys, puzzles and shortcuts persist in the Present");
            yield return SwitchTo(Timeline.Past);
            Check(S.Progress.KeyCount == 2 && L.leafShortcut.IsOpen && !L.leafShortcut.blockers[0].enabled, "...and again in the Past");
        }
        bool sawWarningShot;

        IEnumerator Ride(int fps, bool fromA)
        {
            Application.targetFrameRate = fps;
            var plat = S.platforms[0];
            yield return Tp(fromA ? L.landingA : L.landingB);
            yield return Until(() => (fromA ? plat.IsDockedAtA : plat.IsDockedAtB) && plat.DwellRemaining > 1.2f, 15f);
            yield return Walk(new Vector2(0f, 1f), 0.72f);
            yield return Frames(3);
            Check(S.motor.CurrentPlatform == plat, $"Boarded platform at {(fromA ? "A" : "B")} ({fps} fps target)", $"x={P.x:0.00}");
            Check(!S.timeline.CanSwitch(out string r), "Q rejected while riding", r);
            yield return Until(() => fromA ? plat.IsDockedAtB : plat.IsDockedAtA, 12f);
            yield return Frames(3);
            Check(S.motor.CurrentPlatform == plat && Flat(P, plat.transform.position) < 1.25f && P.y > -0.2f,
                $"Platform carries the player to {(fromA ? "B" : "A")}", $"player {P} platform {plat.transform.position}");
            if (fps == 30) yield return Shot("platform_ride");
            yield return Walk(new Vector2(0f, 1f), 1.0f);
            yield return Frames(4);
            bool onLanding = fromA ? P.x < -17.1f : P.x > -10.9f;
            Check(S.motor.CurrentPlatform == null && onLanding && S.motor.IsGrounded && P.y > -0.3f,
                $"Walked off onto landing {(fromA ? "B" : "A")}", $"x={P.x:0.00} y={P.y:0.00} fps≈{1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):0}");
            Application.targetFrameRate = 60;
        }

        IEnumerator PhasePlatform()
        {
            Line("--- Moving Platform and chest");
            yield return Ride(30, true);
            yield return Ride(144, false);
            yield return Ride(60, true);

            var chest = L.chest;
            Vector3 c = chest.transform.position;
            yield return PressE(new Vector3(c.x + 1.45f, 0.05f, c.z), c + Vector3.up * 0.5f);
            Check(S.Progress.HasKey(KeyId.Wave) && S.Progress.KeyCount == 3 && S.Progress.score == 300, "Chest grants the WAVE key (+100)", "score " + S.Progress.score);
            Check(S.Progress.HasFlag(EchoesIds.PuzzleChest) && S.Progress.HasFlag(EchoesIds.ShortcutWave), "Chest opening opens the WAVE shortcut");
            EchoesInput.SimPress(KeyCode.E);
            yield return Frames(4);
            Check(S.Progress.KeyCount == 3 && S.Progress.score == 300, "Chest grants its key only once");
            yield return Real(1.6f);
            Check(L.waveShortcut.IsOpen && !L.waveShortcut.blockers[0].enabled, "WAVE shortcut door is open");
            Check(Quaternion.Angle(chest.lid.localRotation, Quaternion.Euler(chest.openAngle, 0f, 0f)) < 2f, "Chest lid animated open");
            yield return Shot("chest_open");
        }

        IEnumerator PhaseFallAndDeath()
        {
            Line("--- Falling, death and retry");
            var plat = S.platforms[0];
            yield return Until(() => plat.IsDockedAtA, 10f);
            string cpId = S.Progress.checkpointId;
            int h0 = S.Health;
            yield return Tp(L.pitDrop);
            yield return Until(() => S.IsRespawning, 4f);
            Check(waitOk, "Falling into the pit triggers recovery");
            Check(!S.timeline.TrySwitch(), "Q rejected while respawning");
            yield return Until(() => !S.IsRespawning, 4f);
            yield return Frames(6);
            var cp = S.checkpoints.Get(cpId);
            Check(S.Health == h0 - 1, "Fall costs one health", "health " + S.Health);
            Check(cp != null && Flat(P, cp.SpawnPosition) < 0.6f, "Respawned at the last checkpoint", "checkpoint " + cpId);
            Check(S.motor.IsGrounded && S.motor.CurrentPlatform == null && S.timeline.HasStationarySupport(P), "Respawn is on stationary shared floor");
            Check(plat.IsDockedAtA, "Platform reset to a safe endpoint");
            Check(S.timeline.IsSpaceClear(P, S.timeline.Current, out _), "Respawn timeline has no overlapping blockers");

            int deaths = S.Progress.deaths;
            int keys = S.Progress.KeyCount, score = S.Progress.score;
            while (S.State == GameState.Playing)
            {
                S.TryDamage(1);
                yield return Real(S.tuning.damageImmunity + 0.1f);
            }
            Check(S.State == GameState.Lost && S.menus.lostPanel.activeSelf, "Zero health shows the defeat screen");
            Check(S.Progress.deaths == deaths + 1, "Deaths counted");
            Check(CursorFree && Time.timeScale == 0f, "Defeat frees the cursor and freezes the game");
            yield return Shot("defeat");
            S.menus.OnRetry();
            yield return Frames(5);
            Check(S.State == GameState.Playing && S.Health == 3, "Retry restores 3 HP");
            Check(S.Progress.KeyCount == keys && S.Progress.score == score && S.runePuzzles[0].Solved && L.leafShortcut.IsOpen && L.waveShortcut.IsOpen,
                "Retry keeps keys, score, puzzles and shortcuts");
        }

        IEnumerator PhaseSaves()
        {
            Line("--- Save and Continue");
            bool ok = SaveService.TryLoadRun(S.checkpoints.Ids, out RunProgress saved, out string err);
            Check(ok && saved.keys.Count == 3 && saved.puzzles.Count == 2 && saved.doors.Contains(EchoesIds.ShortcutLeaf) && saved.deaths == 1,
                "Save file holds keys, puzzles, shortcuts and deaths", err);
            yield return Reload(GameSession.LaunchAction.Continue);
            Check(S.State == GameState.Playing, "Continue resumes play");
            Check(S.Progress.KeyCount == 3 && S.Progress.score == 300 && S.Progress.deaths == 1 && S.Health == 3, "Continue restores keys, score, deaths and full health");
            Check(L.runeCache.IsOpen && L.leafShortcut.IsOpen && L.waveShortcut.IsOpen && !L.leafShortcut.blockers[0].enabled, "Continue restores opened doors");
            Check(!L.sunKey.visual.activeSelf && !L.leafKey.visual.activeSelf && L.chest.IsOpen, "Collected keys stay collected after reload");
            var cp = S.checkpoints.Get(S.Progress.checkpointId);
            Check(Flat(P, cp.SpawnPosition) < 0.6f && S.timeline.IsSpaceClear(P, S.timeline.Current, out _), "Continue places the player at a clear checkpoint");
            Check(S.runePuzzles[0].CurrentInput.Count == 0, "Partial rune input is not restored");

            // Pause freezes traps and platform.
            var trap = S.traps[0];
            var plat = S.platforms[0];
            yield return Real(0.5f);
            S.Pause();
            yield return Frames(2);
            float t0 = trap.Timer;
            Vector3 p0 = plat.transform.position;
            yield return Real(0.7f);
            Check(Mathf.Approximately(t0, trap.Timer) && p0 == plat.transform.position, "Pause freezes traps and platform");
            S.Resume();
            yield return Frames(3);
            Check(S.State == GameState.Playing, "Resume after pause");
        }

        IEnumerator PhaseHeartAndExit()
        {
            Line("--- Heart chamber and escape");
            yield return PressE(L.heartDoorFront.position, L.heartGate.transform.position + Vector3.up * 1.4f);
            Check(S.Progress.HasFlag(EchoesIds.DoorHeart), "Heart door opens with 3 keys");
            Check(S.Progress.KeyCount == 3, "Keys are not consumed");
            yield return Real(1.6f);
            Check(L.heartGate.IsOpen && !L.heartGate.blockers[0].enabled, "Heart chamber permanently open");

            yield return Tp(L.exitInside);
            yield return Frames(10);
            Check(S.State == GameState.Playing, "Exit trigger rejects a run without the Heart");
            yield return Tp(L.heartFront);
            yield return Shot("heart_chamber");
            yield return PressE(L.heartFront.position, L.heart.transform.position + Vector3.up * 1.5f);
            Check(S.Progress.heartOwned && S.Progress.score == 600, "Heart collected (+300)", "score " + S.Progress.score);
            EchoesInput.SimPress(KeyCode.E);
            yield return Frames(4);
            Check(S.Progress.score == 600, "Heart awarded once");
            yield return Real(1.6f);
            Check(L.exitGate.IsOpen && L.exitGuide.activeSelf, "Heart unlocks the entrance and lights the guide lamps");

            yield return Reload(GameSession.LaunchAction.Continue);
            Check(S.Progress.heartOwned && L.exitGate.IsOpen && L.exitGuide.activeSelf, "Continue after taking the Heart keeps the exit open");

            yield return Tp(L.hubSafe);
            yield return SwitchTo(Timeline.Past);
            yield return Tp(L.exitApproach);
            yield return Shot("exit_guide");
            yield return Walk(new Vector2(0f, 1f), 1.6f);
            yield return Until(() => S.State == GameState.Won, 2f);
            Check(S.State == GameState.Won && S.menus.wonPanel.activeSelf, "Walking out with the Heart wins (in the Past)");
            Check(S.Progress.score == 1100, "Final score includes one escape reward", "score " + S.Progress.score);
            Check(S.BestScore == 1100 && SaveService.LoadBestScore() == 1100, "Best score updated by the completed run");
            Check(!SaveService.HasRunFile && !S.HasValidSave, "Active-run save cleared after victory");
            yield return Shot("victory");
            S.Win();
            Check(S.Progress.score == 1100, "Victory happens only once");
            yield return Reload(GameSession.LaunchAction.Continue);
            Check(S.State == GameState.Menu && !S.menus.continueButton.interactable, "Completed run cannot be continued");
        }

        IEnumerator PhaseSaveRobustness()
        {
            Line("--- Save robustness");
            var ids = S.checkpoints.Ids;
            File.WriteAllText(SaveService.RunPath, "{{{ not json");
            bool threw = false, loaded = true;
            try { loaded = SaveService.TryLoadRun(ids, out _, out _); } catch (Exception) { threw = true; }
            Check(!threw && !loaded, "Corrupt save is rejected without crashing");
            File.WriteAllText(SaveService.RunPath, "{\"version\":99,\"checkpointId\":\"hub\",\"timeline\":\"present\",\"keys\":[],\"puzzles\":[],\"doors\":[],\"awarded\":[]}");
            Check(!SaveService.TryLoadRun(ids, out _, out string e2), "Incompatible save version is rejected", e2);
            File.WriteAllText(SaveService.RunPath, "{\"version\":1,\"checkpointId\":\"hub\",\"timeline\":\"present\",\"keys\":[\"sun\",\"sun\"],\"puzzles\":[],\"doors\":[],\"awarded\":[]}");
            Check(!SaveService.TryLoadRun(ids, out _, out string e3), "Duplicate key IDs are rejected", e3);
            var good = new RunProgress { checkpointId = "gallery" };
            good.keys.Add("sun");
            good.awarded.Add("key_sun");
            good.score = 100;
            SaveService.ClearRun();
            SaveService.SaveRun(good);
            SaveService.SaveRun(good);
            File.WriteAllText(SaveService.RunPath, "garbage");
            Check(SaveService.TryLoadRun(ids, out RunProgress rec, out _) && rec.checkpointId == "gallery", "Backup save recovers a corrupted main save");
            File.WriteAllText(SaveService.RunPath, "garbage");
            File.WriteAllText(SaveService.RunPath + ".bak", "garbage");
            yield return Reload(GameSession.LaunchAction.ShowMenu);
            Check(S.State == GameState.Menu && !S.menus.continueButton.interactable && S.SaveStatus.Contains("could not be read"), "Menu reports unreadable save and offers New Game", S.SaveStatus);
            yield return Reload(GameSession.LaunchAction.NewGame);
            yield return Begin();
            Check(S.State == GameState.Playing && S.Progress.KeyCount == 0 && S.Progress.score == 0, "New Game works after a corrupt save");
            Check(S.BestScore == 1100, "New Game keeps the best score");
            Check(Mathf.Abs(AudioListener.volume - 0.37f) < 0.01f, "New Game keeps settings (volume)");
        }

        IEnumerator PhaseKeyOrders()
        {
            Line("--- All six key orders");
            KeyId[][] orders =
            {
                new[] { KeyId.Sun, KeyId.Leaf, KeyId.Wave }, new[] { KeyId.Sun, KeyId.Wave, KeyId.Leaf },
                new[] { KeyId.Leaf, KeyId.Sun, KeyId.Wave }, new[] { KeyId.Leaf, KeyId.Wave, KeyId.Sun },
                new[] { KeyId.Wave, KeyId.Sun, KeyId.Leaf }, new[] { KeyId.Wave, KeyId.Leaf, KeyId.Sun },
            };
            var door = L.heartDoor;
            int okCount = 0;
            foreach (var order in orders)
            {
                S.DebugResetRunInPlace();
                bool ok = true;
                for (int i = 0; i < 3; i++)
                {
                    door.Interact(S);
                    ok &= !S.Progress.HasFlag(EchoesIds.DoorHeart);
                    ok &= S.CollectKey(order[i]);
                    ok &= !S.CollectKey(order[i]);
                }
                door.Interact(S);
                ok &= S.Progress.HasFlag(EchoesIds.DoorHeart) && S.Progress.KeyCount == 3 && S.Progress.score == 300;
                ok &= S.CollectHeart() && !S.CollectHeart();
                if (ok) okCount++;
                yield return Frames(2);
            }
            Check(okCount == 6, "Every key order unlocks the Heart door only at 3 keys", okCount + "/6");
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            EchoesInput.ResetSimulation();
            Line("");
            Line($"RESULT: {passed} passed, {failed} failed");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
                File.WriteAllText(reportPath, report.ToString());
            }
            catch (Exception e) { Debug.LogError("[EchoesAutoTest] Could not write report: " + e.Message); }
            Application.Quit(failed == 0 ? 0 : 3);
        }
    }
}
