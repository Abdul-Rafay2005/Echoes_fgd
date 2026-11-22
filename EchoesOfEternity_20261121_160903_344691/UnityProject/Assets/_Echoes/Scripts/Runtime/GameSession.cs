using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Echoes
{
    /// <summary>
    /// Owns the explicit game state machine (Menu, Playing, Transitioning, Paused, Lost, Won), health,
    /// score, run progress, checkpoints, saving, death/retry and victory.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameSession : MonoBehaviour
    {
        public enum LaunchAction { ShowMenu, NewGame, Continue }

        /// <summary>What to do after the scene (re)loads. Menus reload the scene for a clean world.</summary>
        public static LaunchAction PendingLaunch = LaunchAction.ShowMenu;
        public static GameSession Instance { get; private set; }
        /// <summary>(progress, instant). Raised whenever permanent progress changes or is loaded.</summary>
        public static event Action<RunProgress, bool> ProgressChanged;

        [Header("References")]
        public EchoesTuning tuning;
        public FirstPersonMotor motor;
        public PlayerZoneSensor sensor;
        public PlayerInteractor interactor;
        public TimelineManager timeline;
        public CheckpointManager checkpoints;
        public HUDController hud;
        public MenuController menus;
        public AudioManager audioManager;
        public MovingPlatform[] platforms;
        public TimedSpikeTrap[] traps;
        public RunePuzzle[] runePuzzles;
        public FlagGate exitGate;
        public Transform startPoint;

        public GameState State { get; private set; } = GameState.Menu;
        public RunProgress Progress { get; private set; } = new RunProgress();
        public int Health { get; private set; }
        public bool IsRespawning { get; private set; }
        public bool HasValidSave { get; private set; }
        public string SaveStatus { get; private set; }
        public int BestScore { get; private set; }
        public bool IsImmune => Time.time < immuneUntil;
        public event Action<GameState> StateChanged;

        GameState stateBeforePause = GameState.Playing;
        float immuneUntil;
        bool won;

        void Awake()
        {
            Instance = this;
            EchoesLayers.ApplyCollisionMatrix();
            Health = tuning.maxHealth;
            BestScore = SaveService.LoadBestScore();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            RefreshSaveAvailability();
            LaunchAction action = PendingLaunch;
            PendingLaunch = LaunchAction.ShowMenu;
            switch (action)
            {
                case LaunchAction.NewGame: BeginNewRun(); break;
                case LaunchAction.Continue:
                    if (!BeginContinue()) ShowMainMenu();
                    break;
                default: ShowMainMenu(); break;
            }
        }

        // ------------------------------------------------------------------ state

        public void SetState(GameState s)
        {
            State = s;
            bool live = s == GameState.Playing || s == GameState.Transitioning;
            Time.timeScale = live ? 1f : 0f;
            AudioListener.pause = s == GameState.Paused;
            ApplyCursor();
            if (motor != null) motor.inputEnabled = s == GameState.Playing && !IsRespawning;
            if (hud != null) hud.SetVisible(live || s == GameState.Paused);
            StateChanged?.Invoke(s);
        }

        void ApplyCursor()
        {
            bool live = State == GameState.Playing || State == GameState.Transitioning;
            Cursor.lockState = live ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !live;
        }

        void Update()
        {
            if (EchoesInput.PausePressed)
            {
                if (State == GameState.Playing || State == GameState.Transitioning) Pause();
                else if (State == GameState.Paused && menus != null) menus.BackOrResume();
            }

            bool qPressed = EchoesInput.TimelinePressed; // consumed every frame; presses during transitions are ignored
            if (motor != null) motor.inputEnabled = State == GameState.Playing && !IsRespawning;
            if (State != GameState.Playing) return;

            if (!IsRespawning && motor.transform.position.y < tuning.fallRespawnY)
            {
                OnFell();
                return;
            }

            foreach (var trap in sensor.Traps)
                if (trap.IsDamaging) TryDamage(tuning.spikeDamage);
            if (State != GameState.Playing) return;

            foreach (var hint in sensor.Hints) hint.Trigger(this);

            var cp = sensor.Checkpoint;
            if (cp != null && cp.id != Progress.checkpointId && !IsRespawning) ActivateCheckpoint(cp);

            if (sensor.Exit != null && !IsRespawning) sensor.Exit.PlayerInside(this);
            if (State != GameState.Playing) return;

            if (qPressed) timeline.TrySwitch();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && !EchoesInput.Simulated && (State == GameState.Playing || State == GameState.Transitioning)) Pause();
            else ApplyCursor();
        }

        public void Pause()
        {
            if (State != GameState.Playing && State != GameState.Transitioning) return;
            stateBeforePause = State;
            SetState(GameState.Paused);
            if (menus != null) menus.ShowPause();
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            if (menus != null) menus.HideAll();
            SetState(stateBeforePause);
        }

        // ------------------------------------------------------------------ run lifecycle

        public void RequestNewGame() => ReloadWith(LaunchAction.NewGame);
        public void RequestContinue() => ReloadWith(LaunchAction.Continue);
        public void RequestMainMenu() => ReloadWith(LaunchAction.ShowMenu);

        void ReloadWith(LaunchAction action)
        {
            PendingLaunch = action;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().buildIndex : 0);
        }

        public void RefreshSaveAvailability()
        {
            HasValidSave = SaveService.TryLoadRun(checkpoints.Ids, out _, out string error);
            SaveStatus = HasValidSave ? "Continue from your last checkpoint." :
                SaveService.HasRunFile ? "Saved run could not be read (" + error + "). Start a New Game." : "No saved run.";
        }

        void ShowMainMenu()
        {
            Progress = new RunProgress { checkpointId = checkpoints.defaultId };
            Health = tuning.maxHealth;
            timeline.Apply(Timeline.Present);
            PlaceAtStart();
            Broadcast(true);
            SetState(GameState.Menu);
            if (menus != null) menus.ShowMain();
        }

        void PlaceAtStart()
        {
            if (startPoint != null) motor.Teleport(startPoint.position, startPoint.eulerAngles.y);
        }

        void BeginNewRun()
        {
            SaveService.ClearRun();
            Progress = new RunProgress { checkpointId = checkpoints.defaultId, timeline = EchoesIds.TimelineName(Timeline.Present) };
            Health = tuning.maxHealth;
            won = false;
            timeline.Apply(Timeline.Present);
            PlaceAtStart();
            checkpoints.MarkCurrent(Progress.checkpointId);
            Broadcast(true);
            SaveRun();
            HasValidSave = true;
            SetState(GameState.Menu);
            if (menus != null) menus.ShowIntro();
        }

        /// <summary>Called by the intro "Begin" button.</summary>
        public void BeginPlay()
        {
            if (menus != null) menus.HideAll();
            SetState(GameState.Playing);
            if (exitGate != null) exitGate.PlaySeal();
            hud.ShowMessage("The entrance seals behind you. Find the three keys.", 4f);
        }

        bool BeginContinue()
        {
            if (!SaveService.TryLoadRun(checkpoints.Ids, out RunProgress loaded, out string error))
            {
                HasValidSave = false;
                SaveStatus = "Saved run could not be read (" + error + "). Start a New Game.";
                return false;
            }
            Progress = loaded;
            RecomputeScore();
            Health = tuning.maxHealth;
            won = false;
            Broadcast(true);
            RespawnAtCheckpoint();
            SetState(GameState.Playing);
            hud.ShowMessage("Run restored at checkpoint.", 2.5f);
            return true;
        }

        void RecomputeScore()
        {
            int s = 0;
            foreach (var a in Progress.awarded) s += PointsFor(a);
            Progress.score = s;
        }

        int PointsFor(string award)
        {
            if (award == EchoesIds.AwardHeart) return tuning.heartScore;
            if (award == EchoesIds.AwardEscape) return tuning.escapeScore;
            return award.StartsWith("key_") ? tuning.keyScore : 0;
        }

        // ------------------------------------------------------------------ progression

        bool AwardOnce(string id)
        {
            if (Progress.awarded.Contains(id)) return false;
            Progress.awarded.Add(id);
            Progress.score += PointsFor(id);
            return true;
        }

        void Broadcast(bool instant)
        {
            ProgressChanged?.Invoke(Progress, instant);
            if (hud != null) hud.RefreshAll();
        }

        public void SaveRun()
        {
            if (won) return;
            SaveService.SaveRun(Progress);
        }

        public bool CollectKey(KeyId key, string grantDoor = null)
        {
            if (Progress.HasKey(key)) return false;
            Progress.keys.Add(EchoesIds.KeyName(key));
            AwardOnce(EchoesIds.KeyAward(key));
            if (!string.IsNullOrEmpty(grantDoor) && !Progress.doors.Contains(grantDoor)) Progress.doors.Add(grantDoor);
            if (audioManager != null) audioManager.Play(audioManager.keyPickup, 0.8f);
            string extra = string.IsNullOrEmpty(grantDoor) ? "" : " A shortcut back to the hall opens.";
            hud.ShowMessage(EchoesIds.KeyTitle(key) + " key recovered (" + Progress.KeyCount + "/3)." + extra, 3.5f);
            Broadcast(false);
            SaveRun();
            return true;
        }

        public bool OpenChest(string puzzleId, KeyId reward, string grantDoor)
        {
            if (Progress.HasFlag(puzzleId)) return false;
            Progress.puzzles.Add(puzzleId);
            if (audioManager != null) audioManager.Play(audioManager.interact, 0.6f);
            if (!CollectKey(reward, grantDoor))
            {
                Broadcast(false);
                SaveRun();
            }
            return true;
        }

        public bool SolvePuzzle(string puzzleId, string message)
        {
            if (Progress.HasFlag(puzzleId)) return false;
            Progress.puzzles.Add(puzzleId);
            if (audioManager != null) audioManager.Play(audioManager.runeSolved, 0.8f);
            hud.ShowMessage(message, 3.5f);
            Broadcast(false);
            SaveRun();
            return true;
        }

        public bool SetDoorFlag(string doorId, string message)
        {
            if (Progress.doors.Contains(doorId)) return false;
            if (doorId == EchoesIds.DoorHeart && Progress.KeyCount < 3) return false;
            Progress.doors.Add(doorId);
            if (!string.IsNullOrEmpty(message)) hud.ShowMessage(message, 3.5f);
            Broadcast(false);
            SaveRun();
            return true;
        }

        public bool CollectHeart()
        {
            if (Progress.heartOwned || !Progress.HasFlag(EchoesIds.DoorHeart)) return false;
            Progress.heartOwned = true;
            AwardOnce(EchoesIds.AwardHeart);
            if (audioManager != null) audioManager.Play(audioManager.heartPickup, 0.9f);
            hud.ShowMessage("The Heart of Eternity is yours! The temple trembles - follow the blue lamps to the entrance.", 5f);
            Broadcast(false);
            SaveRun();
            return true;
        }

        public string GetObjective()
        {
            int k = Progress.KeyCount;
            if (Progress.heartOwned) return "Escape! Return to the entrance - follow the blue lamps.";
            if (Progress.HasFlag(EchoesIds.DoorHeart)) return "Take the Heart of Eternity from its pedestal.";
            if (k >= 3) return "All keys found. Open the Heart chamber north of the hall.";
            return "Recover the Sun, Leaf and Wave keys (" + k + "/3). Press Q to shift time.";
        }

        void ActivateCheckpoint(Checkpoint cp)
        {
            Progress.checkpointId = cp.id;
            Progress.timeline = EchoesIds.TimelineName(cp.restoreTimeline);
            checkpoints.MarkCurrent(cp.id);
            SaveRun();
            hud.ShowMessage("Checkpoint reached. Progress saved.", 2f);
        }

        // ------------------------------------------------------------------ damage, death, recovery

        public bool TryDamage(int amount)
        {
            if (State != GameState.Playing || IsRespawning || IsImmune || amount <= 0) return false;
            Health = Mathf.Max(0, Health - amount);
            immuneUntil = Time.time + Mathf.Max(1f, tuning.damageImmunity);
            if (hud != null) hud.FlashDamage();
            if (audioManager != null) audioManager.Play(audioManager.damage, 0.8f);
            if (Health <= 0) Lose();
            return true;
        }

        void OnFell()
        {
            if (IsRespawning) return;
            Health = Mathf.Max(0, Health - tuning.fallDamage);
            if (audioManager != null) audioManager.Play(audioManager.damage, 0.8f);
            if (hud != null) hud.FlashDamage();
            if (Health <= 0) { Lose(); return; }
            StartCoroutine(FallRecovery());
        }

        IEnumerator FallRecovery()
        {
            IsRespawning = true;
            motor.inputEnabled = false;
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                hud.SetFade(t / 0.35f, Color.black);
                yield return null;
            }
            RespawnAtCheckpoint();
            hud.ShowMessage("You fell. Back to the last checkpoint.", 2.5f);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                hud.SetFade(1f - t / 0.35f, Color.black);
                yield return null;
            }
            hud.SetFade(0f, Color.black);
            IsRespawning = false;
        }

        void Lose()
        {
            if (State == GameState.Lost) return;
            timeline.CancelTransition();
            Progress.deaths++;
            SaveRun();
            if (audioManager != null) audioManager.Play(audioManager.defeat, 0.8f);
            SetState(GameState.Lost);
            if (menus != null) menus.ShowLost(Progress.score, Progress.deaths);
        }

        /// <summary>Retry after defeat: full health at the checkpoint; all permanent progress is kept.</summary>
        public void Retry()
        {
            if (State != GameState.Lost) return;
            StopAllCoroutines();
            IsRespawning = false;
            Health = tuning.maxHealth;
            if (menus != null) menus.HideAll();
            RespawnAtCheckpoint();
            hud.SetFade(0f, Color.black);
            SetState(GameState.Playing);
            hud.ShowMessage("Back at the checkpoint. Your keys and progress remain.", 3f);
        }

        /// <summary>
        /// Restores the player at the saved checkpoint in a validated timeline, clears transient state
        /// (velocity, platform support, partial rune input) and gives traps a safe interval.
        /// </summary>
        public void RespawnAtCheckpoint()
        {
            timeline.CancelTransition();
            Checkpoint cp = checkpoints.Get(Progress.checkpointId) ?? checkpoints.Default;
            Timeline t = cp.restoreTimeline;
            if (!timeline.IsSpaceClear(cp.SpawnPosition, t, out _))
            {
                t = TimelineManager.Other(t);
                if (!timeline.IsSpaceClear(cp.SpawnPosition, t, out _))
                {
                    cp = checkpoints.Default;
                    t = cp.restoreTimeline;
                }
            }
            Progress.checkpointId = cp.id;
            Progress.timeline = EchoesIds.TimelineName(t);
            checkpoints.MarkCurrent(cp.id);

            foreach (var p in platforms) if (p != null) p.ResetToSafeEndpoint();
            timeline.Apply(t);
            motor.Teleport(cp.SpawnPosition, cp.SpawnYaw);
            foreach (var trap in traps) if (trap != null) trap.Normalize(tuning.trapRespawnGrace);
            foreach (var r in runePuzzles) if (r != null) r.ResetPartial();
            timeline.ResetCooldown();
            immuneUntil = Time.time + 1f;
            sensor.Refresh();
        }

        // ------------------------------------------------------------------ victory

        public void Win()
        {
            if (won || State != GameState.Playing || !Progress.heartOwned) return;
            AwardOnce(EchoesIds.AwardEscape);
            won = true;
            if (Progress.score > BestScore)
            {
                BestScore = Progress.score;
                SaveService.SaveBestScore(BestScore);
            }
            SaveService.ClearRun();
            HasValidSave = false;
            if (audioManager != null) audioManager.Play(audioManager.victory, 0.9f);
            SetState(GameState.Won);
            if (menus != null) menus.ShowWon(Progress.score, Progress.deaths, BestScore);
        }

        // ------------------------------------------------------------------ test support

        /// <summary>Resets the run in place (no scene reload). Used by the automated tests only.</summary>
        public void DebugResetRunInPlace()
        {
            Progress = new RunProgress { checkpointId = checkpoints.defaultId };
            Health = tuning.maxHealth;
            won = false;
            Broadcast(true);
        }
    }
}
