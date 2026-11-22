using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Echoes
{
    /// <summary>
    /// Switches between the two authored timelines. Only the mutually exclusive Past/Present roots
    /// are toggled; the player, progression and shared world never change. Before a switch the
    /// player's capsule is tested against always-enabled query proxies of the target timeline's
    /// blockers (layers EE_QueryPast / EE_QueryPresent), so hidden roots are never queried.
    /// </summary>
    public class TimelineManager : MonoBehaviour
    {
        [Serializable]
        public class LightingProfile
        {
            public Color sunColor = Color.white;
            public float sunIntensity = 1f;
            public Color ambient = new Color(0.4f, 0.4f, 0.4f);
            public Color fogColor = Color.gray;
            public float fogDensity = 0.02f;
            public Color fadeColor = new Color(0.6f, 0.85f, 1f);
        }

        public GameSession session;
        public FirstPersonMotor motor;
        public PlayerZoneSensor sensor;
        public PlayerInteractor interactor;
        public EchoesTuning tuning;
        public AudioManager audioManager;

        [Header("Timeline roots (art and selected blockers only)")]
        public GameObject pastRoot;
        public GameObject presentRoot;

        [Header("Lighting")]
        public Light sun;
        public LightingProfile pastLighting = new LightingProfile();
        public LightingProfile presentLighting = new LightingProfile();

        public Timeline Current { get; private set; } = Timeline.Present;
        public bool IsTransitioning => routine != null;
        public int SwitchCount { get; private set; }
        public string LastRejection { get; private set; }
        public static event Action<Timeline> Changed;

        float cooldownUntil;
        Coroutine routine;

        public static Timeline Other(Timeline t) => t == Timeline.Past ? Timeline.Present : Timeline.Past;

        /// <summary>Full validation of every rule for pressing Q right now.</summary>
        public bool CanSwitch(out string reason)
        {
            reason = null;
            if (session == null || session.State != GameState.Playing) { reason = "You cannot shift time right now."; return false; }
            if (routine != null) { reason = "Time is already shifting."; return false; }
            if (session.IsRespawning) { reason = "You cannot shift time right now."; return false; }
            if (Time.time < cooldownUntil) { reason = "The hourglass is still settling."; return false; }
            if (interactor != null && interactor.IsBusy) { reason = "Finish what you are doing first."; return false; }
            if (!motor.IsGrounded) { reason = "Find solid ground before shifting."; return false; }
            if (motor.CurrentPlatform != null) { reason = "You cannot shift while riding the platform."; return false; }
            if (sensor.InNoSwitchZone || !sensor.InSafeZone) { reason = "This ground is unstable across time."; return false; }
            Vector3 feet = motor.transform.position;
            if (!HasStationarySupport(feet)) { reason = "This ground is unstable across time."; return false; }
            Timeline target = Other(Current);
            if (!IsSpaceClear(feet, target, out _))
            {
                reason = target == Timeline.Past ? "Something solid stands here in the Past." : "Rubble fills this space in the Present.";
                return false;
            }
            return true;
        }

        public bool TrySwitch()
        {
            if (!CanSwitch(out string reason))
            {
                LastRejection = reason;
                if (session != null && session.hud != null) session.hud.ShowMessage(reason, 1.8f);
                return false;
            }
            LastRejection = null;
            routine = StartCoroutine(SwitchRoutine(Other(Current)));
            return true;
        }

        IEnumerator SwitchRoutine(Timeline target)
        {
            session.SetState(GameState.Transitioning);
            if (audioManager != null) audioManager.Play(audioManager.timelineSwitch, 0.8f);
            float half = Mathf.Max(0.05f, tuning.timelineFade * 0.5f);
            Color fade = (target == Timeline.Past ? pastLighting : presentLighting).fadeColor;

            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                session.hud.SetFade(t / half, fade);
                yield return null;
            }
            session.hud.SetFade(1f, fade);
            Apply(target); // atomic: roots, lighting and label in one call
            for (float t = 0f; t < half; t += Time.deltaTime)
            {
                session.hud.SetFade(1f - t / half, fade);
                yield return null;
            }
            session.hud.SetFade(0f, fade);
            SwitchCount++;
            cooldownUntil = Time.time + tuning.timelineCooldown;
            routine = null;
            if (session.State == GameState.Transitioning) session.SetState(GameState.Playing);
        }

        /// <summary>Applies a timeline immediately (also used for loads and respawns).</summary>
        public void Apply(Timeline t)
        {
            Current = t;
            if (pastRoot != null) pastRoot.SetActive(t == Timeline.Past);
            if (presentRoot != null) presentRoot.SetActive(t == Timeline.Present);
            ApplyLighting(t == Timeline.Past ? pastLighting : presentLighting);
            Physics.SyncTransforms();
            if (session != null && session.hud != null) session.hud.SetTimeline(t);
            Changed?.Invoke(t);
        }

        public void CancelTransition()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            if (session != null && session.hud != null) session.hud.SetFade(0f, Color.black);
        }

        public void ResetCooldown() => cooldownUntil = 0f;

        void ApplyLighting(LightingProfile p)
        {
            if (sun != null)
            {
                sun.color = p.sunColor;
                sun.intensity = p.sunIntensity;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = p.ambient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(p.ambient);
            RenderSettings.ambientProbe = sh;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
        }

        /// <summary>
        /// True when the player's capsule at <paramref name="feet"/> would not overlap any blocker
        /// that exists only in timeline <paramref name="t"/>. Floors are never on the query layers,
        /// so normal floor contact cannot fail the test.
        /// </summary>
        public bool IsSpaceClear(Vector3 feet, Timeline t, out Collider blocker)
        {
            blocker = null;
            motor.GetCapsule(feet + Vector3.up * 0.04f, -0.01f, out Vector3 top, out Vector3 bottom, out float radius);
            Collider[] hits = Physics.OverlapCapsule(top, bottom, radius, EchoesLayers.QueryMask(t), QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.isTrigger || h.transform.IsChildOf(motor.transform)) continue;
                blocker = h;
                return false;
            }
            return true;
        }

        /// <summary>True when shared, stationary floor (not the platform, not timeline art) supports the feet.</summary>
        public bool HasStationarySupport(Vector3 feet)
        {
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, 0.8f, 1 << EchoesLayers.Default, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.collider.GetComponent<StationaryGround>() == null) return false;
            if (pastRoot != null && hit.collider.transform.IsChildOf(pastRoot.transform)) return false;
            if (presentRoot != null && hit.collider.transform.IsChildOf(presentRoot.transform)) return false;
            return true;
        }
    }
}
